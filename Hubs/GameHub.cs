using DraculaVanHelsing.Api.Models.Enums;
using DraculaVanHelsing.Api.Models.GameState;
using DraculaVanHelsing.Api.Services;
using DraculaVanHelsing.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DraculaVanhelsing.Api.Hubs
{
    [Authorize]
    public class GameHub : Hub
    {
        private readonly IGameEngineService _gameEngineService;
        private readonly IGameStateService _gameStateService;

        public GameHub(IGameEngineService gameEngineService, IGameStateService gameStateService)
        {
            _gameEngineService = gameEngineService;
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
            var userId = Guid.Parse(Context.UserIdentifier!);
            Console.WriteLine($"User Disconnected: {userId} - ConnectionId: {Context.ConnectionId}");

            var state = await _gameEngineService.HandleDisconnectAsync(userId, Context.ConnectionId);

            if (state != null)
            {
                if (state.Status == RoomStatus.Waiting)
                {
                    await Clients.Group(state.RoomCode).SendAsync("GameStateUpdated", state);
                }
                else if (state.Status == RoomStatus.Finished)
                {
                    await Clients.Group(state.RoomCode).SendAsync("GameEnded", state);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
        public async Task<string?> CheckCurrentActiveMatch()
        {
            try
            {
                if (string.IsNullOrEmpty(Context.UserIdentifier))
                {
                    Console.WriteLine("[SignalR] Lỗi: Context.UserIdentifier bị NULL.");
                    return null;
                }

                if (!Guid.TryParse(Context.UserIdentifier, out var userId))
                {
                    Console.WriteLine($"[SignalR] Lỗi Parse Guid: {Context.UserIdentifier}");
                    return null;
                }

                // Cần _gameStateService để gọi hàm này
                var roomCode = await _gameStateService.GetUserRoomAsync(userId);

                if (!string.IsNullOrEmpty(roomCode))
                {
                    Console.WriteLine($"[SignalR] Tự động reconnect vào phòng: {roomCode}");
                    await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                    // --- THÊM PHẦN NÀY ĐỂ TRẢ LẠI TRẠNG THÁI CHO CLIENT ---
                    var state = await _gameStateService.GetGameStateAsync(roomCode);
                    if (state != null)
                    {
                        await Clients.Caller.SendAsync("GameStateUpdated", state);
                    }

                    return roomCode;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Exception trong CheckCurrentActiveMatch: {ex.Message}");
                return null;
            }
        }

        public async Task LeaveRoom()
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            await _gameEngineService.LeaveRoomAsync(userId);
            await Clients.Caller.SendAsync("LeftRoom"); // Thông báo cho FE đã thoát xong
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

        public async Task SubmitSkillAction(string roomCode, SkillPayload payload)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameEngineService.SubmitSkillActionAsync(userId, roomCode, payload);

            if (state != null)
            {
                await Clients.Group(roomCode).SendAsync("GameStateUpdated", state);
            }
        }
        public async Task CallEndRound(string roomCode)
        {
            var userId = Guid.Parse(Context.UserIdentifier!);
            var state = await _gameEngineService.CallEndRoundAsync(userId, roomCode);

            if (state != null)
            {
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