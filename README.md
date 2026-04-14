# Dracula vs Van Helsing - Backend API
Welcome to the Dracula vs Van Helsing backend repository. This project provides the robust, real-time server infrastructure required to power the asymmetric strategic battles. It handles matchmaking, state synchronization, secure authentication, and persistent data storage. Explore the live version at https://boardgame-dracula-vanhelsing.vercel.app/.
Frontend Repository: https://github.com/minhquansicula/dracula-frontend

## Key Features
Real-Time Engine: Bidirectional WebSocket communication using SignalR for instant game state updates, lobby management, and matchmaking.
Secure Authentication: JWT (JSON Web Token) implementation for robust user registration, login, and protected API endpoints.
Relational Data Management: Entity Framework Core (Code-First approach) managing complex relationships, composite keys, and match histories.
RESTful Architecture: Clean and scalable API endpoints for user management, leaderboards, and data retrieval.
Cloud-Native: Configured and optimized for deployment on Azure App Service with Azure SQL Database integration.

##Prerequisites
Ensure the following are installed on your system before starting:

- .NET 8.0 SDK
- SQL Server (LocalDB, SQL Server Management Studio, or Azure SQL)
- Git (required for cloning the repository)

## Tech Stack
- Core Framework: C# .NET 8 (ASP.NET Core Web API)
- Real-time Communication: SignalR
- ORM & Database: Entity Framework Core, SQL Server / Azure SQL Database
- Security: JWT Authentication & CORS Policies

## Project Structure
The project is organized into a modular structure for optimal scalability and maintenance:

- src/Controllers/: REST API endpoints (Auth, Users)
- src/Hubs/: SignalR hubs handling real-time WebSocket connections
-src/Data/: Entity Framework AppDbContext and Migrations
- src/Models/: Database Entities (Users, MatchHistory) and DTOs
- src/Services/: Core business logic and game state management
- src/Program.cs: Application entry point, dependency injection, and middleware

## Installation and Running


1. **Clone the repository:**
   ```bash
   git clone https://github.com/minhquansicula/dracula-api-backend.git
   cd dracula-api-backend

2. Configure Environment Variables:
Open appsettings.Development.json (or create it if it doesn't exist) and configure your local SQL Server connection and a secure JWT Key:
```
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=DraculaDbLocal;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Key": "YOUR_SUPER_SECRET_KEY_AT_LEAST_32_CHARS_LONG",
    "Issuer": "DraculaGame",
    "Audience": "DraculaPlayers"
  }
}
```

3. Apply Database Migrations:


```
dotnet ef database update
```
4. Start the development server:

```Bash

dotnet run
The API will launch on https://localhost:xxxx. You can access the Swagger UI documentation at https://localhost:xxxx/swagger.
```
## Deployment
This project is optimized for deployment on Azure App Service (Linux).
Important Deployment Note: When deploying to Azure, do not commit production passwords to GitHub. You must declare the following in the Environment variables section of your Azure App Service dashboard: Add DefaultConnection in the Connection strings tab (Type: SQLAzure) pointing to your Azure SQL Database, and add Jwt__Key (with double underscores) in the App settings tab for token encryption.
