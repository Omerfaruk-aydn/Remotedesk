# Local Development

## Requirements

- .NET 8 SDK
- Node.js 22+
- Docker Desktop
- PostgreSQL tools optional

## Backend

```powershell
cd apps/backend-api
dotnet restore
dotnet ef migrations add InitialCreate --project src/SecureRemoteDesk.Infrastructure --startup-project src/SecureRemoteDesk.Api
dotnet ef database update --project src/SecureRemoteDesk.Infrastructure --startup-project src/SecureRemoteDesk.Api
dotnet run --project src/SecureRemoteDesk.Api
```

## Admin Dashboard

```powershell
cd apps/admin-dashboard
npm install
npm run dev
```

## Desktop Apps

```powershell
dotnet run --project apps/host-windows/src/SecureRemoteDesk.Host.csproj
dotnet run --project apps/viewer-windows/src/SecureRemoteDesk.Viewer.csproj
```

## Tests

```powershell
dotnet test apps/backend-api/SecureRemoteDesk.Backend.sln
```
