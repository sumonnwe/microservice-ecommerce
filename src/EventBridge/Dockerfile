# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file (path is relative to build context ./src/EventBridge)
COPY ["EventBridge.csproj", "./"]

# Restore using correct path
RUN dotnet restore "EventBridge.csproj"

# Copy everything and publish
COPY . .
WORKDIR /src
RUN dotnet publish "EventBridge.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

# copy published app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "EventBridge.dll"]