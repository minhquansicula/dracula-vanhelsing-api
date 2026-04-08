using DraculaVanHelsing.Api.Helpers;
using DraculaVanHelsing.Api.Models.Enums;
using DraculaVanHelsing.Api.Models.GameState;
using DraculaVanHelsing.Api.Services.Interfaces;

namespace DraculaVanHelsing.Api.Services
{
    public class GameEngineService : IGameEngineService
    {
        private readonly IGameStateService _gameStateService;

        public GameEngineService(IGameStateService gameStateService)
        {
            _gameStateService = gameStateService;
        }

        public async Task<GameRoomState> CreateRoomAsync(Guid userId, string connectionId)
        {
            var roomCode = GameHelper.GenerateRoomCode();

            var state = new GameRoomState
            {
                RoomId = Guid.NewGuid(),
                RoomCode = roomCode,
                Status = RoomStatus.Waiting,
                Players = new List<PlayerInGame>
                {
                    new PlayerInGame
                    {
                        UserId = userId,
                        ConnectionId = connectionId,
                        Health = 0
                    }
                }
            };

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            await _gameStateService.SetUserRoomAsync(userId, roomCode);
            return state;
        }

        public async Task<GameRoomState?> JoinRoomAsync(Guid userId, string connectionId, string roomCode)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Waiting || state.Players.Count >= 2)
            {
                return null;
            }

            state.Players.Add(new PlayerInGame
            {
                UserId = userId,
                ConnectionId = connectionId,
                Health = 0
            });

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            await _gameStateService.SetUserRoomAsync(userId, roomCode);
            return state;
        }

        public async Task<GameRoomState?> SelectRoleAsync(Guid userId, string roomCode, FactionType requestedFaction)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Waiting) return null;

            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return null;

            player.RequestedFaction = requestedFaction;

            if (state.Players.Count == 2 && state.Players.All(p => p.RequestedFaction.HasValue))
            {
                var player1 = state.Players[0];
                var player2 = state.Players[1];

                if (player1.RequestedFaction != player2.RequestedFaction)
                {
                    player1.Faction = player1.RequestedFaction;
                    player2.Faction = player2.RequestedFaction;
                }
                else
                {
                    var random = new Random();
                    bool player1KeepsRole = random.Next(2) == 0;

                    player1.Faction = player1KeepsRole ? player1.RequestedFaction : GetOppositeFaction(player1.RequestedFaction!.Value);
                    player2.Faction = player1KeepsRole ? GetOppositeFaction(player2.RequestedFaction!.Value) : player2.RequestedFaction;
                }

                InitializeGame(state);

                state.Status = RoomStatus.Playing;
                await _gameStateService.SaveGameStateAsync(roomCode, state);
            }
            else
            {
                await _gameStateService.SaveGameStateAsync(roomCode, state);
            }

            return state;
        }

        private void InitializeGame(GameRoomState state)
        {
            state.Zones = new List<BoardZoneState>();
            for (int i = 1; i <= 5; i++)
            {
                state.Zones.Add(new BoardZoneState
                {
                    ZoneIndex = i,
                    HumanTokens = 4,
                    VampireTokens = 0
                });
            }

            state.ColorRanking = GameHelper.GenerateRandomColorRanking();

            var allCards = GameHelper.GenerateStandardDeck();
            var deckIds = allCards.Select(c => c.CardId).ToList();
            state.DrawPile = GameHelper.Shuffle(deckIds);
            state.DiscardPile = new List<int>();

            foreach (var p in state.Players)
            {
                p.Health = p.Faction == FactionType.Dracula ? 12 : 0;
                p.Hand = new List<CardInHand>();

                for (int i = 0; i < 5; i++)
                {
                    var cardId = state.DrawPile[0];
                    state.DrawPile.RemoveAt(0);

                    p.Hand.Add(new CardInHand { CardId = cardId, IsRevealed = false });
                }
            }

            var draculaPlayer = state.Players.First(p => p.Faction == FactionType.Dracula);
            state.CurrentTurnUserId = draculaPlayer.UserId;
            state.RoundNumber = 1;
        }

        public async Task<GameRoomState?> DrawCardAsync(Guid userId, string roomCode)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Playing) return null;
            if (state.CurrentTurnUserId != userId) return null;

            if (state.PendingSkillValue != null) return null;

            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null || player.DrawnCard != null) return null;

            if (state.DrawPile.Count == 0)
            {
                return await CheckAndResolveRoundAsync(roomCode, state);
            }

            var cardId = state.DrawPile[0];
            state.DrawPile.RemoveAt(0);

            player.DrawnCard = new CardInHand { CardId = cardId, IsRevealed = false };

            // LOGIC MỚI: Rút xong phải tắt cờ lộ bài top deck
            state.IsTopDeckCardRevealed = false;

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        public async Task<GameRoomState?> PlayCardAsync(Guid userId, string roomCode, int discardedCardId)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Playing) return null;
            if (state.CurrentTurnUserId != userId) return null;

            if (state.PendingSkillValue != null) return null;

            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null || player.DrawnCard == null) return null;

            int playedCardValue = ((discardedCardId - 1) % 8) + 1;

            // Lá 8 yêu cầu phải có ít nhất 6 lá trong Mộ bài (nghĩa là 5 lá đang nằm sẵn trong mộ + 1 lá đang chuẩn bị vứt vào)
            if (playedCardValue == 8 && state.DiscardPile.Count < 5)
            {
                return null;
            }

            CardInHand? cardToDiscard = null;
            bool isDiscardingDrawnCard = player.DrawnCard.CardId == discardedCardId;

            if (isDiscardingDrawnCard)
            {
                cardToDiscard = player.DrawnCard;
                player.DrawnCard = null;
            }
            else
            {
                var cardInHand = player.Hand.FirstOrDefault(c => c.CardId == discardedCardId);
                if (cardInHand == null) return null;

                cardToDiscard = cardInHand;
                int index = player.Hand.IndexOf(cardInHand);

                player.Hand[index] = player.DrawnCard;
                player.DrawnCard = null;
            }

            state.DiscardPile.Add(cardToDiscard.CardId);

            bool requiresTarget = playedCardValue == 1 || playedCardValue == 3 || playedCardValue == 4 || playedCardValue == 6 || playedCardValue == 7;

            if (requiresTarget)
            {
                state.PendingSkillValue = playedCardValue;
            }
            else
            {
                ExecuteAutoSkill(state, playedCardValue);

                if (playedCardValue != 5 && playedCardValue != 8)
                {
                    var opponent = state.Players.First(p => p.UserId != userId);
                    state.CurrentTurnUserId = opponent.UserId;
                }
            }

            return await CheckAndResolveRoundAsync(roomCode, state);
        }

        private void ExecuteAutoSkill(GameRoomState state, int skillValue)
        {
            switch (skillValue)
            {
                case 2:
                    state.IsTopDeckCardRevealed = true;
                    break;
                case 8:
                    state.ForceEndRound = true;
                    break;
            }
        }

        public async Task<GameRoomState?> SubmitSkillActionAsync(Guid userId, string roomCode, SkillPayload payload)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Playing) return null;
            if (state.CurrentTurnUserId != userId || state.PendingSkillValue == null) return null;

            int skillValue = state.PendingSkillValue.Value;
            var currentPlayer = state.Players.First(p => p.UserId == userId);
            var opponent = state.Players.First(p => p.UserId != userId);

            switch (skillValue)
            {
                case 1:
                    if (payload.TargetCardId.HasValue)
                    {
                        var myCard = currentPlayer.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                        // Bổ sung Anti-cheat: Chỉ cho phép chọn lá bài chưa lật
                        if (myCard != null && !myCard.IsRevealed) myCard.IsRevealed = true;
                        else return null;
                    }
                    break;

                case 3:
                    if (payload.TargetCardId.HasValue)
                    {
                        var targetCard = opponent.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                        // Bổ sung Anti-cheat: Chỉ cho phép chọn lá bài chưa lật
                        if (targetCard != null && !targetCard.IsRevealed) targetCard.IsRevealed = true;
                        else return null;
                    }
                    break;

                case 4:
                    if (payload.TargetCardId.HasValue && payload.TargetCardId2.HasValue)
                    {
                        var card1 = currentPlayer.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                        var card2 = currentPlayer.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId2.Value);

                        if (card1 != null && card2 != null)
                        {
                            int idx1 = currentPlayer.Hand.IndexOf(card1);
                            int idx2 = currentPlayer.Hand.IndexOf(card2);

                            currentPlayer.Hand[idx1] = card2;
                            currentPlayer.Hand[idx2] = card1;
                        }
                    }
                    break;

                case 6:
                    if (payload.TargetCardId.HasValue)
                    {
                        var opponentCard = opponent.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                        if (opponentCard != null)
                        {
                            int districtIndex = opponent.Hand.IndexOf(opponentCard);
                            var myCard = currentPlayer.Hand[districtIndex];

                            opponent.Hand[districtIndex] = myCard;
                            currentPlayer.Hand[districtIndex] = opponentCard;
                        }
                    }
                    break;

                case 7:
                    if (payload.TargetColor1.HasValue && payload.TargetColor2.HasValue)
                    {
                        var idx1 = state.ColorRanking.IndexOf(payload.TargetColor1.Value);
                        var idx2 = state.ColorRanking.IndexOf(payload.TargetColor2.Value);

                        // Bổ sung luật: 1 trong 2 màu phải là màu Trump (Index 0)
                        if (idx1 != -1 && idx2 != -1 && (idx1 == 0 || idx2 == 0))
                        {
                            state.ColorRanking[idx1] = payload.TargetColor2.Value;
                            state.ColorRanking[idx2] = payload.TargetColor1.Value;
                        }
                        else return null;
                    }
                    break;
            }

            state.PendingSkillValue = null;
            state.CurrentTurnUserId = opponent.UserId;

            return await CheckAndResolveRoundAsync(roomCode, state);
        }

        public async Task<GameRoomState?> CallEndRoundAsync(Guid userId, string roomCode)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Playing) return null;
            if (state.CurrentTurnUserId != userId || state.PendingSkillValue != null) return null;

            if (state.DiscardPile.Count < 6) return null;

            state.IsLastTurn = true;
            state.CalledEndRoundUserId = userId;

            var opponent = state.Players.First(p => p.UserId != userId);
            state.CurrentTurnUserId = opponent.UserId;

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        private async Task<GameRoomState> CheckAndResolveRoundAsync(string roomCode, GameRoomState state)
        {
            bool isDeckEmpty = state.DrawPile.Count == 0;
            bool isLastTurnFinished = state.IsLastTurn && state.CurrentTurnUserId == state.CalledEndRoundUserId;

            if (isDeckEmpty || state.ForceEndRound || isLastTurnFinished)
            {
                return await ResolveRoundAsync(roomCode, state);
            }

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        private async Task<GameRoomState> ResolveRoundAsync(string roomCode, GameRoomState state)
        {
            var allCards = GameHelper.GenerateStandardDeck();
            var dracula = state.Players.First(p => p.Faction == FactionType.Dracula);
            var vanHelsing = state.Players.First(p => p.Faction == FactionType.VanHelsing);

            for (int i = 0; i < 5; i++)
            {
                var draculaCard = allCards.First(c => c.CardId == dracula.Hand[i].CardId);
                var vhCard = allCards.First(c => c.CardId == vanHelsing.Hand[i].CardId);

                int winner = CompareCards(draculaCard, vhCard, state.ColorRanking);
                var zone = state.Zones[i];

                if (winner > 0)
                {
                    if (zone.HumanTokens > 0)
                    {
                        zone.HumanTokens--;
                        zone.VampireTokens++;
                    }

                    if (zone.VampireTokens >= 4)
                    {
                        return await EndGameAsync(state, dracula.UserId, "Dracula turned 4 humans in a district.");
                    }
                }
                else
                {
                    dracula.Health--;

                    if (dracula.Health <= 0)
                    {
                        return await EndGameAsync(state, vanHelsing.UserId, "Van Helsing defeated Dracula.");
                    }
                }
            }

            state.RoundNumber++;
            if (state.RoundNumber > 5)
            {
                return await EndGameAsync(state, dracula.UserId, "Dracula survived 5 rounds.");
            }

            SetupNextRound(state);
            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        private int CompareCards(CardData c1, CardData c2, List<CardColor> ranking)
        {
            CardColor trumpColor = ranking[0];
            bool c1IsTrump = c1.Color == trumpColor;
            bool c2IsTrump = c2.Color == trumpColor;

            if (c1IsTrump && !c2IsTrump) return 1;
            if (!c1IsTrump && c2IsTrump) return -1;

            if (c1.Value > c2.Value) return 1;
            if (c1.Value < c2.Value) return -1;

            int c1Rank = ranking.IndexOf(c1.Color);
            int c2Rank = ranking.IndexOf(c2.Color);
            return c1Rank < c2Rank ? 1 : -1;
        }

        private void SetupNextRound(GameRoomState state)
        {
            state.IsLastTurn = false;
            state.CalledEndRoundUserId = null;
            state.ForceEndRound = false;
            state.PendingSkillValue = null;
            state.IsTopDeckCardRevealed = false;

            var allCards = GameHelper.GenerateStandardDeck();
            var deckIds = allCards.Select(c => c.CardId).ToList();
            state.DrawPile = GameHelper.Shuffle(deckIds);
            state.DiscardPile = new List<int>();

            foreach (var p in state.Players)
            {
                p.DrawnCard = null;
                p.Hand.Clear();

                for (int i = 0; i < 5; i++)
                {
                    var cardId = state.DrawPile[0];
                    state.DrawPile.RemoveAt(0);
                    p.Hand.Add(new CardInHand { CardId = cardId, IsRevealed = false });
                }
            }

            var draculaPlayer = state.Players.First(p => p.Faction == FactionType.Dracula);
            state.CurrentTurnUserId = draculaPlayer.UserId;
        }

        public async Task LeaveRoomAsync(Guid userId)
        {
            var roomCode = await _gameStateService.GetUserRoomAsync(userId);
            if (string.IsNullOrEmpty(roomCode)) return;

            var state = await _gameStateService.GetGameStateAsync(roomCode);
            if (state != null)
            {
                var player = state.Players.FirstOrDefault(p => p.UserId == userId);
                if (player != null)
                {
                    state.Players.Remove(player);
                    if (state.Players.Count == 0)
                    {
                        await _gameStateService.DeleteGameStateAsync(roomCode);
                    }
                    else
                    {
                        await _gameStateService.SaveGameStateAsync(roomCode, state);
                    }
                }
            }
            await _gameStateService.RemoveUserRoomAsync(userId);
        }

        private async Task<GameRoomState> EndGameAsync(GameRoomState state, Guid winnerId, string reason)
        {
            state.Status = RoomStatus.Finished;
            state.WinnerId = winnerId;
            state.EndReason = reason;

            await _gameStateService.SaveGameStateAsync(state.RoomCode, state);

            foreach (var player in state.Players)
            {
                await _gameStateService.RemoveUserRoomAsync(player.UserId);
            }

            return state;
        }

        public async Task<GameRoomState?> SurrenderAsync(Guid userId, string roomCode)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Playing) return null;

            var loser = state.Players.FirstOrDefault(p => p.UserId == userId);
            var winner = state.Players.FirstOrDefault(p => p.UserId != userId);

            if (loser == null || winner == null) return null;

            return await EndGameAsync(state, winner.UserId, "Surrender");
        }

        public async Task<GameRoomState?> HandleDisconnectAsync(Guid userId, string connectionId)
        {
            var roomCode = await _gameStateService.GetUserRoomAsync(userId);
            if (string.IsNullOrEmpty(roomCode)) return null;

            var state = await _gameStateService.GetGameStateAsync(roomCode);
            if (state == null) return null;

            if (state.Status == RoomStatus.Waiting)
            {
                await LeaveRoomAsync(userId);
                return state;
            }

            return state;
        }

        private FactionType GetOppositeFaction(FactionType faction)
        {
            return faction == FactionType.Dracula ? FactionType.VanHelsing : FactionType.Dracula;
        }
    }
}