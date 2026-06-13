# Backend API

ASP.NET Core 8 Web API, PostgreSQL, EF Core, JWT authentication and SignalR signaling layer.

Run after installing .NET 8 SDK:

```powershell
dotnet restore
dotnet ef database update --project src/SecureRemoteDesk.Infrastructure --startup-project src/SecureRemoteDesk.Api
dotnet run --project src/SecureRemoteDesk.Api
```
