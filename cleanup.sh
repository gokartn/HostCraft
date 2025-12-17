#!/bin/bash
set -e

echo "🧹 HostCraft Complete Cleanup Script"
echo "====================================="
echo ""
echo "⚠️  WARNING: This will DELETE EVERYTHING including:"
echo "  - All HostCraft containers"
echo "  - All volumes (database, backups, etc.)"
echo "  - All networks"
echo "  - All application data folders"
echo "  - All configuration files"
echo "  - The HostCraft installation directory"
echo ""
echo "❌ THIS CANNOT BE UNDONE - ALL DATA WILL BE PERMANENTLY LOST!"
echo ""
while true; do
    read -p "Type 'DELETE EVERYTHING' to confirm: " confirm
    if [ "$confirm" = "DELETE EVERYTHING" ]; then
        break
    else
        echo "❌ Cancelled. You must type 'DELETE EVERYTHING' exactly to continue."
        exit 0
    fi
done

echo ""
echo "🗑️  Step 1: Stopping and removing Docker stack..."
if docker stack ls | grep -q hostcraft; then
    docker stack rm hostcraft
    echo "Waiting for stack removal to complete..."
    sleep 10
fi

echo ""
echo "🗑️  Step 2: Removing Docker Compose resources..."
if [ -f docker-compose.yml ]; then
    docker compose down -v --remove-orphans 2>/dev/null || true
fi

echo ""
echo "🗑️  Step 3: Removing all HostCraft containers..."
docker ps -a --filter "name=hostcraft" --format "{{.ID}}" | xargs -r docker rm -f 2>/dev/null || true

echo ""
echo "🗑️  Step 4: Removing all HostCraft volumes..."
docker volume ls --filter "name=hostcraft" --format "{{.Name}}" | xargs -r docker volume rm 2>/dev/null || true

echo ""
echo "🗑️  Step 5: Removing all HostCraft networks..."
docker network ls --filter "name=hostcraft" --format "{{.Name}}" | xargs -r docker network rm 2>/dev/null || true

echo ""
echo "🗑️  Step 6: Removing application data directories..."
# Remove common data directories
sudo rm -rf /var/lib/hostcraft 2>/dev/null || true
sudo rm -rf /opt/hostcraft 2>/dev/null || true
sudo rm -rf /var/hostcraft 2>/dev/null || true
sudo rm -rf ~/hostcraft-data 2>/dev/null || true

echo ""
echo "🗑️  Step 7: Removing configuration files..."
sudo rm -rf /etc/hostcraft 2>/dev/null || true
sudo rm -f /etc/systemd/system/hostcraft.service 2>/dev/null || true
sudo systemctl daemon-reload 2>/dev/null || true

echo ""
echo "🗑️  Step 8: Removing log files..."
sudo rm -rf /var/log/hostcraft 2>/dev/null || true

echo ""
echo "🗑️  Step 9: Cleaning up images (optional)..."
read -p "Do you want to remove HostCraft Docker images? (yes/no): " remove_images
case $remove_images in
    yes|y|Y|YES)
        docker images --filter "reference=hostcraft*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        echo "✅ Images removed"
        ;;
    *)
        echo "⏭️  Skipping image removal"
        ;;
esac

echo ""
echo "🗑️  Step 10: Removing installation directory..."
read -p "Remove the HostCraft installation directory ($(pwd))? (yes/no): " remove_dir
case $remove_dir in
    yes|y|Y|YES)
        INSTALL_DIR=$(pwd)
        cd ..
        sudo rm -rf "$INSTALL_DIR"
        echo "✅ Installation directory removed"
        ;;
    *)
        echo "⏭️  Keeping installation directory"
        ;;
esac

echo ""
echo "✅ HostCraft has been completely removed from the system!"
echo ""
echo "All containers, volumes, networks, and data have been deleted."
echo "To reinstall HostCraft, clone the repository again and run ./install.sh"
