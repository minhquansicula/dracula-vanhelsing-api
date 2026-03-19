using System.Text.Json;
using DraculaVanHelsing.Api.Models.GameState;
using DraculaVanHelsing.Api.Services.Interfaces;
using StackExchange.Redis;

namespace DraculaVanHelsing.Api.Services
{
    public class GameStateService : IGameStateService
    {
        private readonly IDatabase _redisDb;

        public GameStateService(IConnectionMultiplexer redis)
        {
            _redisDb = redis.GetDatabase();
        }

        public async Task<GameRoomState?> GetGameStateAsync(string roomId)
        {
            var data = await _redisDb.StringGetAsync($"room:{roomId}");
            if (data.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<GameRoomState>(data!);
        }

        public async Task SaveGameStateAsync(string roomId, GameRoomState state)
        {
            var data = JsonSerializer.Serialize(state);
            // Lưu state trên Redis với thời gian hết hạn là 24 giờ
            await _redisDb.StringSetAsync($"room:{roomId}", data, TimeSpan.FromHours(24));
        }

        public async Task DeleteGameStateAsync(string roomId)
        {
            await _redisDb.KeyDeleteAsync($"room:{roomId}");
        }
    }
}