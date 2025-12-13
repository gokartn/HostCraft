#!/bin/bash
set -e

echo "================================================"
echo "  HostCraft - Swarm Worker Setup"
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

# Check if already in a swarm
if docker info --format '{{.Swarm.LocalNodeState}}' | grep -q active; then
    echo "⚠️  This node is already part of a swarm"
    docker info --format '{{.Swarm.RemoteManagers}}'
    exit 0
fi

# Prompt for join token and manager address
echo "You need the join token and manager address from the manager node"
echo ""
read -p "Enter the WORKER join token: " JOIN_TOKEN
read -p "Enter the manager address (e.g., 192.168.1.100:2377): " MANAGER_ADDRESS

if [ -z "$JOIN_TOKEN" ] || [ -z "$MANAGER_ADDRESS" ]; then
    echo "❌ Both token and manager address are required"
    exit 1
fi

# Open firewall if ufw is active
if command -v ufw &> /dev/null && ufw status | grep -q "Status: active"; then
    echo "🔓 Opening firewall ports..."
    ufw allow 7946/tcp comment "Docker Swarm node communication"
    ufw allow 7946/udp comment "Docker Swarm node communication"
    ufw allow 4789/udp comment "Docker Swarm overlay network"
    echo "✅ Firewall rules added"
fi

# Join swarm
echo ""
echo "🚀 Joining Docker Swarm..."
docker swarm join --token $JOIN_TOKEN $MANAGER_ADDRESS

echo ""
echo "✅ Successfully joined swarm as worker!"
echo ""
echo "📝 Note: Run 'docker node ls' on the manager to see this node"
echo ""
