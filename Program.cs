using DraculaVanHelsing.Api.Data;
using DraculaVanHelsing.Api.Services;
using DraculaVanHelsing.Api.Services.Interfaces;
using DraculaVanhelsing.Api.Hubs; 
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình SQL Server (Entity Framework Core)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Cấu hình Redis
var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection");
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString!));

// 3. Cấu hình JWT Authentication & CORS
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

// 4. Allow CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // BẮT BUỘC PHẢI CÓ DÒNG NÀY CHO SIGNALR
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };

    // BẮT BUỘC THÊM SỰ KIỆN NÀY ĐỂ SIGNALR NHẬN ĐƯỢC JWT TOKEN
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            // Nếu request gửi đến Hub và có chứa token trong query string
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/gamehub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// 5. Đăng ký các Services (Dependency Injection)
builder.Services.AddScoped<IGameStateService, GameStateService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IGameEngineService, GameEngineService>();

// 6. Cấu hình SignalR và Controllers
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Thứ tự Middleware rất quan trọng: CORS -> AuthN -> AuthZ -> Map
app.UseCors("AllowReactApp");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 7. Map SignalR Hub
app.MapHub<GameHub>("/gamehub");

app.Run();