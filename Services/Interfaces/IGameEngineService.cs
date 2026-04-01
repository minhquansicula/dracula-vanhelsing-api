using DraculaVanHelsing.Api.Models.Enums;
using DraculaVanHelsing.Api.Models.GameState;

namespace DraculaVanHelsing.Api.Services.Interfaces
{
    public interface IGameEngineService
    {
        Task<GameRoomState> CreateRoomAsync(Guid userId, string connectionId);
        Task<GameRoomState?> JoinRoomAsync(Guid userId, string connectionId, string roomCode);
        Task<GameRoomState?> SelectRoleAsync(Guid userId, string roomCode, FactionType requestedFaction);
        Task<GameRoomState?> SurrenderAsync(Guid userId, string roomCode);
        Task<GameRoomState?> DrawCardAsync(Guid userId, string roomCode);
        Task<GameRoomState?> PlayCardAsync(Guid userId, string roomCode, int discardedCardId);
    }
}