using DraculaVanHelsing.Api.Models.Enums;
using DraculaVanHelsing.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DraculaVanhelsing.Api.Hubs
{
    [Authorize]
    public class GameHub : Hub
    {
        private readonly IGameEngineService _gameEngineService;

        public GameHub(IGameEngineService gameEngineService)
        {
            _gameEngineService = gameEngineService;
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
            var state = await _gameEngineService.CreateRoomAsync(userId, Context.ConnectionId);

            await Groups.AddToGroupAsync(Context.ConnectionId, state.RoomCode);
            await Clients.Caller.SendAsync("RoomCreated", state.RoomCode);
        }

        public async Task JoinRoom(string roomCode)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameEngineService.JoinRoomAsync(userId, Context.ConnectionId, roomCode);

            if (state == null)
            {
                await Clients.Caller.SendAsync("Error", "Phòng không tồn tại hoặc đã đầy.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
            await Clients.Group(roomCode).SendAsync("RoomReadyToSelectRole", state);
        }

        public async Task SelectRole(string roomCode, FactionType requestedFaction)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameEngineService.SelectRoleAsync(userId, roomCode, requestedFaction);

            if (state == null) return;

            if (state.Status == RoomStatus.Playing)
            {
                // Gửi trạng thái game đã bắt đầu (kèm Role chính thức) cho cả 2
                await Clients.Group(roomCode).SendAsync("GameStarted", state);
            }
            else
            {
                // Thông báo cho người kia biết "Đối thủ đã chọn xong, đến lượt bạn"
                await Clients.OthersInGroup(roomCode).SendAsync("OpponentSelectedRole", userId);

                await Clients.Caller.SendAsync("GameStateUpdated", state);
            }
        }

        public async Task DrawCard(string roomCode)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameEngineService.DrawCardAsync(userId, roomCode);

            if (state != null)
            {
                // Gửi state mới cho cả 2 người chơi để cập nhật UI
                await Clients.Group(roomCode).SendAsync("GameStateUpdated", state);
            }
        }

        public async Task PlayCard(string roomCode, int discardedCardId)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameEngineService.PlayCardAsync(userId, roomCode, discardedCardId);

            if (state != null)
            {
                // Gửi state mới cho cả 2 người chơi để cập nhật UI
                await Clients.Group(roomCode).SendAsync("GameStateUpdated", state);
            }
        }

        public async Task Surrender(string roomCode)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameEngineService.SurrenderAsync(userId, roomCode);

            if (state != null)
            {
                // Gửi trạng thái kết thúc game cho cả 2 người chơi trong phòng
                await Clients.Group(roomCode).SendAsync("GameEnded", state);
            }
        }
    }
}