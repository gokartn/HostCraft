#!/bin/bash
set -e

echo "⚠️  HostCraft Complete Removal Script"
echo "======================================"
echo ""
echo "This will DELETE ALL DATA including:"
echo "  - All containers"
echo "  - All volumes (database, backups, etc.)"
echo "  - All networks"
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
echo "🗑️  Removing all containers, volumes, and networks..."
docker compose down -v

echo ""
echo "✅ HostCraft has been completely removed."
echo ""
echo "To reinstall, run: ./install.sh"
