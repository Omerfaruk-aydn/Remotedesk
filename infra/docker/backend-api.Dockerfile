FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore SecureRemoteDesk.Backend.sln
RUN dotnet publish src/SecureRemoteDesk.Api/SecureRemoteDesk.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SecureRemoteDesk.Api.dll"]
