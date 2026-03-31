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