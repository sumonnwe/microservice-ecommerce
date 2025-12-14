#!/bin/bash

echo "🛑 Stopping all running containers..."
docker stop $(docker ps -aq) 2>/dev/null

echo "🗑 Removing all containers..."
docker rm -f $(docker ps -aq) 2>/dev/null

echo "🖼 Removing all Docker images..."
docker rmi -f $(docker images -aq) 2>/dev/null

echo "📦 Removing all unused volumes..."
docker volume prune -f

echo "🌐 Removing all unused networks..."
docker network prune -f

echo "🧹 Removing all build cache..."
docker builder prune -a -f

echo "🔧 Building Docker Compose with --no-cache..."
docker compose build --no-cache

echo "🚀 Starting Docker Compose..."
docker compose up -d

echo "✨ Cleanup complete and containers started successfully!"
