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
echo "🐝 Checking Docker Swarm status..."
SWARM_ACTIVE="false"
IS_MANAGER="false"

if docker info 2>/dev/null | grep -q "Swarm: active"; then
    SWARM_ACTIVE="true"
    # Check if this node is a manager
    if docker info 2>/dev/null | grep -q "Is Manager: true"; then
        IS_MANAGER="true"
        echo "   ✅ This node is a Docker Swarm Manager"

        # Count managers and workers
        MANAGER_COUNT=$(docker node ls --filter "role=manager" -q 2>/dev/null | wc -l)
        WORKER_COUNT=$(docker node ls --filter "role=worker" -q 2>/dev/null | wc -l)
        echo "   📊 Swarm has $MANAGER_COUNT manager(s) and $WORKER_COUNT worker(s)"
    else
        echo "   ✅ This node is a Docker Swarm Worker"
    fi
else
    echo "   ℹ️  Docker Swarm is not active on this node"
fi
echo ""

echo "🗑️  Step 1: Stopping and removing Docker stacks..."

# Function to wait for a stack's services and containers to fully drain
wait_for_stack_removal() {
    local stack_name="$1"
    local max_wait=60
    local elapsed=0

    echo "   Waiting for ${stack_name} stack services to fully stop..."
    while [ $elapsed -lt $max_wait ]; do
        # Check if any services from this stack still exist
        remaining=$(docker service ls --filter "label=com.docker.stack.namespace=${stack_name}" -q 2>/dev/null | wc -l)
        if [ "$remaining" -eq 0 ]; then
            echo "   ✅ All ${stack_name} services removed"
            # Extra wait for container cleanup and network detach
            sleep 5
            return 0
        fi
        echo "   ... ${remaining} service(s) still draining (${elapsed}s/${max_wait}s)"
        sleep 3
        elapsed=$((elapsed + 3))
    done
    echo "   ⚠️  Timed out waiting for ${stack_name} services to stop. Continuing anyway."
}

if docker stack ls 2>/dev/null | grep -q hostcraft; then
    docker stack rm hostcraft
    wait_for_stack_removal "hostcraft"
fi

if docker stack ls 2>/dev/null | grep -q traefik; then
    docker stack rm traefik
    wait_for_stack_removal "traefik"
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
echo "🗑️  Step 5: Removing Traefik resources..."
docker ps -a --filter "name=traefik" --format "{{.ID}}" | xargs -r docker rm -f 2>/dev/null || true
docker volume ls --filter "name=traefik" --format "{{.Name}}" | xargs -r docker volume rm 2>/dev/null || true

echo ""
echo "🗑️  Step 6: Removing all HostCraft networks..."
# Retry network removal - Docker may need a moment after services fully detach
for attempt in 1 2 3; do
    remaining_nets=$(docker network ls --filter "name=hostcraft" --format "{{.Name}}" 2>/dev/null | wc -l)
    remaining_traefik=$(docker network ls --filter "name=traefik-public" --format "{{.Name}}" 2>/dev/null | wc -l)
    if [ "$remaining_nets" -eq 0 ] && [ "$remaining_traefik" -eq 0 ]; then
        break
    fi
    docker network ls --filter "name=hostcraft" --format "{{.Name}}" | xargs -r docker network rm 2>/dev/null || true
    docker network ls --filter "name=traefik-public" --format "{{.Name}}" | xargs -r docker network rm 2>/dev/null || true
    if [ $attempt -lt 3 ]; then
        sleep 5
    fi
done

echo ""
echo "🐝 Step 7: Docker Swarm Cleanup..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo ""
    echo "⚠️  Docker Swarm is active on this system."
    echo "   This swarm may have been initialized by HostCraft during installation."
    echo ""
    read -p "Do you want to leave/destroy the Docker Swarm? (yes/no) [no]: " destroy_swarm
    destroy_swarm=${destroy_swarm:-no}

    case $destroy_swarm in
        yes|y|Y|YES)
            if [ "$IS_MANAGER" = "true" ]; then
                echo ""
                echo "   🔍 Checking for worker nodes..."

                # Get list of worker nodes (excluding this manager)
                WORKER_NODES=$(docker node ls --filter "role=worker" --format "{{.ID}}" 2>/dev/null)

                if [ -n "$WORKER_NODES" ]; then
                    echo "   ⚠️  Found worker nodes in the swarm."
                    echo "   These workers should leave the swarm before the manager is removed."
                    echo ""
                    read -p "   Force remove all worker nodes from swarm? (yes/no) [yes]: " remove_workers
                    remove_workers=${remove_workers:-yes}

                    case $remove_workers in
                        yes|y|Y|YES|"")
                            echo "   🗑️  Removing worker nodes from swarm..."
                            for NODE_ID in $WORKER_NODES; do
                                NODE_NAME=$(docker node inspect "$NODE_ID" --format "{{.Description.Hostname}}" 2>/dev/null || echo "$NODE_ID")
                                echo "      Removing node: $NODE_NAME ($NODE_ID)"
                                # First try to drain the node
                                docker node update --availability drain "$NODE_ID" 2>/dev/null || true
                                sleep 2
                                # Then remove it (force in case it's down)
                                docker node rm --force "$NODE_ID" 2>/dev/null || true
                            done
                            echo "   ✅ Worker nodes removed from swarm"
                            ;;
                        *)
                            echo "   ⏭️  Skipping worker node removal"
                            echo "   ⚠️  Warning: Workers may become orphaned when manager leaves"
                            ;;
                    esac
                fi

                # Check for other managers
                OTHER_MANAGERS=$(docker node ls --filter "role=manager" --format "{{.ID}}" 2>/dev/null | grep -v "$(docker info --format '{{.Swarm.NodeID}}')" || true)

                if [ -n "$OTHER_MANAGERS" ]; then
                    echo ""
                    echo "   ⚠️  There are other manager nodes in the swarm."
                    echo "   This node will leave the swarm, but the swarm will continue with other managers."
                fi

                echo ""
                echo "   🗑️  Leaving Docker Swarm (as manager)..."
                # Force leave since we're the manager
                docker swarm leave --force 2>/dev/null || true
                echo "   ✅ Left Docker Swarm"
            else
                # This node is a worker
                echo ""
                echo "   🗑️  Leaving Docker Swarm (as worker)..."
                docker swarm leave 2>/dev/null || docker swarm leave --force 2>/dev/null || true
                echo "   ✅ Left Docker Swarm"
            fi
            ;;
        *)
            echo "   ⏭️  Keeping Docker Swarm intact"
            echo "   ℹ️  You can manually leave the swarm later with: docker swarm leave --force"
            ;;
    esac
else
    echo "   ℹ️  Docker Swarm is not active - skipping"
fi

echo ""
echo "🗑️  Step 8: Removing application data directories..."
# Remove common data directories
sudo rm -rf /var/lib/hostcraft 2>/dev/null || true
sudo rm -rf /opt/hostcraft 2>/dev/null || true
sudo rm -rf /var/hostcraft 2>/dev/null || true
sudo rm -rf ~/hostcraft-data 2>/dev/null || true

echo ""
echo "🗑️  Step 9: Removing configuration files..."
sudo rm -rf /etc/hostcraft 2>/dev/null || true
sudo rm -f /etc/systemd/system/hostcraft.service 2>/dev/null || true
sudo systemctl daemon-reload 2>/dev/null || true

echo ""
echo "🗑️  Step 10: Removing log files..."
sudo rm -rf /var/log/hostcraft 2>/dev/null || true

echo ""
echo "🗑️  Step 11: Cleaning up images (optional)..."
read -p "Do you want to remove HostCraft Docker images? (yes/no) [no]: " remove_images
remove_images=${remove_images:-no}
case $remove_images in
    yes|y|Y|YES)
        echo "   🗑️  Removing locally built images..."
        docker images --filter "reference=hostcraft*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        echo "   🗑️  Removing GitHub Container Registry images..."
        docker images --filter "reference=ghcr.io/*/hostcraft-api*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        docker images --filter "reference=ghcr.io/*/hostcraft-web*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        echo "   🗑️  Removing Traefik images..."
        docker images --filter "reference=traefik*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        echo "   🗑️  Removing PostgreSQL images..."
        docker images --filter "reference=postgres*" --format "{{.ID}}" | xargs -r docker rmi -f 2>/dev/null || true
        echo "   ✅ Images removed"
        ;;
    *)
        echo "   ⏭️  Skipping image removal"
        ;;
esac

echo ""
echo "🗑️  Step 12: Removing installation directory..."
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
echo "=============================================="
echo "✅ HostCraft has been completely removed!"
echo "=============================================="
echo ""
echo "Cleaned up:"
echo "  • Docker stacks and containers"
echo "  • Volumes and networks"
echo "  • Application data and config files"
if [ "$destroy_swarm" = "yes" ] || [ "$destroy_swarm" = "y" ] || [ "$destroy_swarm" = "Y" ] || [ "$destroy_swarm" = "YES" ]; then
    echo "  • Docker Swarm (left/destroyed)"
fi
echo ""
echo "To reinstall HostCraft, clone the repository again and run ./install.sh"
