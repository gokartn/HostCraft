#!/bin/bash
set -e

echo "⚠️  HostCraft Uninstall Script"
echo "======================================"
echo ""
echo "This will STOP and REMOVE HostCraft including:"
echo "  - All containers/services"
echo "  - All volumes (database, backups, etc.)"
echo "  - All networks"
echo "  - Application data in /var/lib/hostcraft"
echo ""
echo "⚠️  ALL DATA WILL BE PERMANENTLY LOST!"
echo ""
while true; do
    read -p "Are you sure you want to continue? (yes/no): " confirm
    case $confirm in
        yes|y|Y|YES) break;;
        no|n|N|NO)
            echo "Cancelled."
            exit 0
            ;;
        *) echo "❌ Invalid input. Please enter 'yes' or 'no'.";;
    esac
done

echo ""
echo "🐝 Checking Docker Swarm status..."
SWARM_ACTIVE="false"
if docker info 2>/dev/null | grep -q "Swarm: active"; then
    SWARM_ACTIVE="true"
    echo "   ✅ Docker Swarm is active"
else
    echo "   ℹ️  Docker Swarm is not active"
fi

echo ""
echo "🗑️  Step 1: Stopping and removing HostCraft..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "   Using Docker Stack (Swarm mode)..."
    docker stack rm hostcraft 2>/dev/null || true
    echo "   Waiting for services to stop..."
    sleep 15
else
    echo "   Using Docker Compose (standalone mode)..."
    if [ -f docker-compose.yml ]; then
        docker compose down -v --remove-orphans 2>/dev/null || true
    else
        echo "   ⚠️  No docker-compose.yml found, skipping compose cleanup"
    fi
fi

echo ""
echo "🗑️  Step 2: Removing any remaining containers..."
docker ps -a --filter "name=hostcraft" --format "{{.ID}}" | xargs -r docker rm -f 2>/dev/null || true
docker ps -a --filter "label=hostcraft.managed=true" --format "{{.ID}}" | xargs -r docker rm -f 2>/dev/null || true

echo ""
echo "🗑️  Step 3: Removing volumes..."
docker volume ls --filter "name=hostcraft" --format "{{.Name}}" | xargs -r docker volume rm 2>/dev/null || true
docker volume ls --filter "name=traefik" --format "{{.Name}}" | xargs -r docker volume rm 2>/dev/null || true

echo ""
echo "🗑️  Step 4: Removing networks..."
docker network ls --filter "name=hostcraft" --format "{{.Name}}" | xargs -r docker network rm 2>/dev/null || true

echo ""
echo "🗑️  Step 5: Removing application data..."
sudo rm -rf /var/lib/hostcraft 2>/dev/null || true

echo ""
echo "🗑️  Step 6: Removing images (optional)..."
read -p "Remove HostCraft Docker images? (yes/no) [no]: " remove_images
remove_images=${remove_images:-no}
case $remove_images in
    yes|y|Y|YES)
        echo "   Removing images..."
        docker images --filter "reference=hostcraft*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        docker images --filter "reference=ghcr.io/*/hostcraft*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        echo "   ✅ Images removed"
        ;;
    *)
        echo "   ⏭️  Keeping images"
        ;;
esac

echo ""
echo "============================================"
echo "✅ HostCraft has been uninstalled!"
echo "============================================"
echo ""
echo "Removed:"
echo "  • Containers and services"
echo "  • Volumes and networks"
echo "  • Application data"
echo ""
echo "ℹ️  Docker Swarm is still active (if you want to remove it, use cleanup.sh)"
echo ""
echo "To reinstall HostCraft:"
echo "  curl -fsSL https://github.com/gokartn/HostCraft/releases/latest/download/install.sh | bash"
echo ""
