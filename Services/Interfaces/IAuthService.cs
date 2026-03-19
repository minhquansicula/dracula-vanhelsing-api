using DraculaVanHelsing.Api.DTOs.Requests;
using DraculaVanHelsing.Api.DTOs.Responses;

namespace DraculaVanHelsing.Api.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse?> RegisterAsync(RegisterRequest request);
        Task<AuthResponse?> LoginAsync(LoginRequest request);
    }
}