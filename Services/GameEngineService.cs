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
            if (state.CurrentTurnUserId != userId) return null; // Không phải lượt của người này

            // BẢO MẬT: Chặn thao tác nếu đang chờ xử lý kỹ năng (Lá 3, 4, 6, 7)
            if (state.PendingSkillValue != null) return null;

            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null || player.DrawnCard != null) return null; // Đã rút bài rồi không được rút thêm

            if (state.DrawPile.Count == 0)
            {
                // TODO: Xử lý hết bài -> Gọi hàm kết thúc vòng
                return state;
            }

            // Rút lá đầu tiên từ DrawPile
            var cardId = state.DrawPile[0];
            state.DrawPile.RemoveAt(0);

            // Đưa vào khu vực chờ (DrawnCard) của người chơi
            player.DrawnCard = new CardInHand { CardId = cardId, IsRevealed = false };

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        public async Task<GameRoomState?> PlayCardAsync(Guid userId, string roomCode, int discardedCardId)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Playing) return null;
            if (state.CurrentTurnUserId != userId) return null;

            // BẢO MẬT: Chặn thao tác nếu đang chờ xử lý kỹ năng (Ngăn chặn spam API)
            if (state.PendingSkillValue != null) return null;

            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null || player.DrawnCard == null) return null;

            int playedCardValue = ((discardedCardId - 1) % 8) + 1;

            // Luật lá số 8: Không được vứt trừ khi Mộ bài có ít nhất 6 lá (bao gồm cả nó, tức là trước đó có >= 5 lá)
            if (playedCardValue == 8 && state.DiscardPile.Count < 5)
            {
                return null; // Không cho phép vứt
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

            bool requiresTarget = playedCardValue ==1 || playedCardValue == 3 || playedCardValue == 4 || playedCardValue == 6 || playedCardValue == 7;

            if (requiresTarget)
            {
                state.PendingSkillValue = playedCardValue;
            }
            else
            {
                ExecuteAutoSkill(state, playedCardValue);

                // Nếu đánh lá 5 (Thêm lượt) hoặc 8 (Kết thúc vòng), không chuyển lượt cho đối thủ
                if (playedCardValue != 5 && playedCardValue != 8)
                {
                    var opponent = state.Players.First(p => p.UserId != userId);
                    state.CurrentTurnUserId = opponent.UserId;
                }
            }

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        private void ExecuteAutoSkill(GameRoomState state, int skillValue)
        {
            var currentPlayer = state.Players.First(p => p.UserId == state.CurrentTurnUserId);

            switch (skillValue)
            {
                case 2: // Reveal the top card of the deck (Lật lá trên cùng bộ bài)
                        // Chúng ta không lật trong DrawPile (vì nó là mảng int), 
                        // mà ta lật lá đó khi người chơi tiếp theo rút nó, hoặc lưu vào một biến 'TopCardRevealed' trong state.
                        // Cách đơn giản nhất: Đánh dấu để FE hiển thị lá bài tiếp theo trong Deck là "nhìn thấy được".
                    state.IsTopDeckCardRevealed = true;
                    break;

                case 8:
                    // TODO: ResolveRoundAsync(state);
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
                case 1: // Lật 1 lá bài của bản thân
                    if (payload.TargetCardId.HasValue)
                    {
                        var myCard = currentPlayer.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                        if (myCard != null)
                        {
                            myCard.IsRevealed = true;
                        }
                    }
                    break;

                case 3: // Lật bài đối thủ
                    if (payload.TargetCardId.HasValue)
                    {
                        var targetCard = opponent.Hand.FirstOrDefault(c => c.CardId == payload.TargetCardId.Value);
                        if (targetCard != null)
                        {
                            targetCard.IsRevealed = true;
                        }
                    }
                    break;

                case 4: // Tráo 2 lá bài của bản thân
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

                case 6: // Tráo bài cùng Quận với đối thủ
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

                case 7: // Đổi thứ hạng màu
                    if (payload.TargetColor1.HasValue && payload.TargetColor2.HasValue)
                    {
                        var idx1 = state.ColorRanking.IndexOf(payload.TargetColor1.Value);
                        var idx2 = state.ColorRanking.IndexOf(payload.TargetColor2.Value);

                        if (idx1 != -1 && idx2 != -1)
                        {
                            state.ColorRanking[idx1] = payload.TargetColor2.Value;
                            state.ColorRanking[idx2] = payload.TargetColor1.Value;
                        }
                    }
                    break;
            }

            state.PendingSkillValue = null;
            state.CurrentTurnUserId = opponent.UserId;

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            return state;
        }

        public async Task<GameRoomState?> SurrenderAsync(Guid userId, string roomCode)
        {
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            // Chỉ cho phép đầu hàng khi game đang diễn ra
            if (state == null || state.Status != RoomStatus.Playing) return null;

            var loser = state.Players.FirstOrDefault(p => p.UserId == userId);
            var winner = state.Players.FirstOrDefault(p => p.UserId != userId);

            if (loser == null || winner == null) return null;

            // Cập nhật trạng thái game
            state.Status = RoomStatus.Finished;
            state.WinnerId = winner.UserId;
            state.EndReason = "Surrender";

            // Lưu lại trạng thái mới vào Redis
            await _gameStateService.SaveGameStateAsync(roomCode, state);

            // TODO: (Sau này) Lưu lịch sử trận đấu (MatchHistory) vào Database SQL tại đây

            return state;
        }
        private FactionType GetOppositeFaction(FactionType faction)
        {
            return faction == FactionType.Dracula ? FactionType.VanHelsing : FactionType.Dracula;
        }
    }
}