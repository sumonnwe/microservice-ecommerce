# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything from build context (src/OrderService)
COPY ["OrderService/OrderService.csproj", "OrderService/"]
COPY ["Shared/Shared.csproj", "Shared/"] 

# Restore using correct path
RUN dotnet restore OrderService/OrderService.csproj

# Copy everything and publish
COPY . .
WORKDIR /src/OrderService
RUN dotnet publish "OrderService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# copy published app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "OrderService.dll"]