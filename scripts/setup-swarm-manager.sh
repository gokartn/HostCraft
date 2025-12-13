#!/bin/bash
set -e

echo "================================================"
echo "  HostCraft - Swarm Manager Setup"
echo "================================================"
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo "❌ Please run as root (use sudo)"
    exit 1
fi

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed. Run ./install-docker.sh first"
    exit 1
fi

# Get IP address
echo "🔍 Detecting IP addresses..."
echo ""
ip -4 addr show | grep -oP '(?<=inet\s)\d+(\.\d+){3}' | grep -v 127.0.0.1

echo ""
read -p "Enter the IP address to advertise (public IP if using NAT): " ADVERTISE_IP

if [ -z "$ADVERTISE_IP" ]; then
    echo "❌ IP address is required"
    exit 1
fi

# Check if already in a swarm
if docker info --format '{{.Swarm.LocalNodeState}}' | grep -q active; then
    echo "⚠️  This node is already part of a swarm"
    echo ""
    docker node ls
    echo ""
    echo "Manager join token:"
    docker swarm join-token manager
    echo ""
    echo "Worker join token:"
    docker swarm join-token worker
    exit 0
fi

# Initialize Swarm
echo ""
echo "🚀 Initializing Docker Swarm..."
docker swarm init --advertise-addr $ADVERTISE_IP

echo ""
echo "✅ Swarm initialized successfully!"
echo ""

# Show tokens
echo "📋 Save these tokens securely:"
echo ""
echo "─────────────────────────────────────────"
echo "MANAGER JOIN TOKEN:"
docker swarm join-token manager -q
echo "─────────────────────────────────────────"
echo ""
echo "─────────────────────────────────────────"
echo "WORKER JOIN TOKEN:"
docker swarm join-token worker -q
echo "─────────────────────────────────────────"
echo ""

# Show join commands
echo "📝 To add nodes, run these commands on other servers:"
echo ""
echo "For manager:"
docker swarm join-token manager | grep "docker swarm join"
echo ""
echo "For worker:"
docker swarm join-token worker | grep "docker swarm join"
echo ""

# Open firewall if ufw is active
if command -v ufw &> /dev/null && ufw status | grep -q "Status: active"; then
    echo "🔓 Opening firewall ports..."
    ufw allow 2377/tcp comment "Docker Swarm management"
    ufw allow 7946/tcp comment "Docker Swarm node communication"
    ufw allow 7946/udp comment "Docker Swarm node communication"
    ufw allow 4789/udp comment "Docker Swarm overlay network"
    echo "✅ Firewall rules added"
fi

echo ""
echo "🎉 Swarm manager setup complete!"
echo ""
echo "📊 Current cluster status:"
docker node ls
echo ""
