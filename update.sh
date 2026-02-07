#!/bin/bash
set -e

echo "🔄 Updating HostCraft..."
echo "======================"
echo ""

# Pull latest code
echo "📥 Pulling latest code from GitHub..."
git pull
echo ""

# Stop containers
echo "🛑 Stopping containers..."
docker compose down
echo ""

# Rebuild containers
echo "🔨 Rebuilding containers..."
docker compose build --no-cache
echo ""

# Start containers
echo "🚀 Starting containers..."
docker compose up -d
echo ""

# Wait for containers to be ready
echo "⏳ Waiting for services to be ready..."
sleep 10
echo ""

echo "✅ Update completed!"
echo ""
echo "📍 HostCraft is now running with the latest changes"
echo "   Web UI: http://$(hostname -I | awk '{print $1}'):5000"
echo "   API:    http://$(hostname -I | awk '{print $1}'):5100"
echo ""
echo "💡 Clear your browser cache (Ctrl+Shift+R or Cmd+Shift+R) to see CSS changes"
