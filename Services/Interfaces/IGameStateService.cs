using DraculaVanHelsing.Api.Models.GameState;

namespace DraculaVanHelsing.Api.Services.Interfaces
{
    public interface IGameStateService
    {
        Task<GameRoomState?> GetGameStateAsync(string roomId);
        Task SaveGameStateAsync(string roomId, GameRoomState state);
        Task DeleteGameStateAsync(string roomId);

        Task SetUserRoomAsync(Guid userId, string roomCode);
        Task<string?> GetUserRoomAsync(Guid userId);
        Task RemoveUserRoomAsync(Guid userId);
    }
}