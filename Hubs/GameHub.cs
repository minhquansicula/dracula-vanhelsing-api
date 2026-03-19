using DraculaVanHelsing.Api.Models.Enums;
using DraculaVanHelsing.Api.Models.GameState;
using DraculaVanHelsing.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DraculaVanhelsing.Api.Hubs
{
    [Authorize]
    public class GameHub : Hub
    {
        private readonly IGameStateService _gameStateService;

        public GameHub(IGameStateService gameStateService)
        {
            _gameStateService = gameStateService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            Console.WriteLine($"User Connected: {userId} - ConnectionId: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            Console.WriteLine($"User Disconnected: {userId} - ConnectionId: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task CreateRoom()
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var roomCode = GenerateRoomCode();

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
                        ConnectionId = Context.ConnectionId,
                        Health = 0 // Máu sẽ set sau khi chốt Role
                    }
                }
            };

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
            await Clients.Caller.SendAsync("RoomCreated", roomCode);
        }

        public async Task JoinRoom(string roomCode)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Waiting || state.Players.Count >= 2)
            {
                await Clients.Caller.SendAsync("Error", "Phòng không tồn tại hoặc đã đầy.");
                return;
            }

            state.Players.Add(new PlayerInGame
            {
                UserId = userId,
                ConnectionId = Context.ConnectionId,
                Health = 0
            });

            await _gameStateService.SaveGameStateAsync(roomCode, state);
            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

            // Báo cho cả 2 người chơi biết phòng đã đủ 2 người và chuyển sang màn hình chọn Role
            await Clients.Group(roomCode).SendAsync("RoomReadyToSelectRole", state);
        }

        public async Task SelectRole(string roomCode, FactionType requestedFaction)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameStateService.GetGameStateAsync(roomCode);

            if (state == null || state.Status != RoomStatus.Waiting) return;

            var player = state.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return;

            // Lưu lựa chọn của người chơi
            player.RequestedFaction = requestedFaction;

            // Kiểm tra xem cả 2 người đã chọn xong chưa
            if (state.Players.Count == 2 && state.Players.All(p => p.RequestedFaction.HasValue))
            {
                var player1 = state.Players[0];
                var player2 = state.Players[1];

                // Nếu 2 người chọn khác nhau -> Ai chọn gì được nấy
                if (player1.RequestedFaction != player2.RequestedFaction)
                {
                    player1.Faction = player1.RequestedFaction;
                    player2.Faction = player2.RequestedFaction;
                }
                else
                {
                    // Nếu 2 người chọn giống nhau -> Random 50/50
                    var random = new Random();
                    bool player1KeepsRole = random.Next(2) == 0;

                    player1.Faction = player1KeepsRole ? player1.RequestedFaction : GetOppositeFaction(player1.RequestedFaction!.Value);
                    player2.Faction = player1KeepsRole ? GetOppositeFaction(player2.RequestedFaction!.Value) : player2.RequestedFaction;
                }

                // Cập nhật lại Máu theo Role chính thức (Chỉ Dracula có 12 HP)
                foreach (var p in state.Players)
                {
                    p.Health = p.Faction == FactionType.Dracula ? 12 : 0;
                }

                // Chuyển state sang Playing
                state.Status = RoomStatus.Playing;
                await _gameStateService.SaveGameStateAsync(roomCode, state);

                // TODO: Chỗ này sau này sẽ gọi hàm xào bài, chia bài...

                // Báo cho cả 2 người kết quả chọn Role và Bắt đầu game
                await Clients.Group(roomCode).SendAsync("GameStarted", state);
            }
            else
            {
                // Nếu mới có 1 người chọn, lưu lại và đợi người kia
                await _gameStateService.SaveGameStateAsync(roomCode, state);
                await Clients.OthersInGroup(roomCode).SendAsync("OpponentSelectedRole");
            }
        }

        private FactionType GetOppositeFaction(FactionType faction)
        {
            return faction == FactionType.Dracula ? FactionType.VanHelsing : FactionType.Dracula;
        }

        private string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}