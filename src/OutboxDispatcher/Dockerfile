# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files relative to build context ./src
COPY ["OutboxDispatcher/OutboxDispatcher.csproj", "OutboxDispatcher/"]
COPY ["Shared/Shared.csproj", "Shared/"]

# Restore using correct path
RUN dotnet restore OutboxDispatcher/OutboxDispatcher.csproj

# Copy everything and publish
COPY . .
WORKDIR /src/OutboxDispatcher
RUN dotnet publish "OutboxDispatcher.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage (worker)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "OutboxDispatcher.dll"]