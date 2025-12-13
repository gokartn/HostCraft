#!/bin/bash
set -e

echo "================================================"
echo "  HostCraft - Docker Installation Script"
echo "================================================"
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo "❌ Please run as root (use sudo)"
    exit 1
fi

# Detect OS
if [ -f /etc/os-release ]; then
    . /etc/os-release
    OS=$ID
    VERSION=$VERSION_ID
else
    echo "❌ Cannot detect OS"
    exit 1
fi

echo "✅ Detected OS: $OS $VERSION"
echo ""

# Install Docker
echo "📦 Installing Docker..."
if ! command -v docker &> /dev/null; then
    case $OS in
        ubuntu|debian)
            apt-get update
            apt-get install -y ca-certificates curl gnupg
            install -m 0755 -d /etc/apt/keyrings
            curl -fsSL https://download.docker.com/linux/$OS/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
            chmod a+r /etc/apt/keyrings/docker.gpg
            
            echo \
              "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/$OS \
              $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
              tee /etc/apt/sources.list.d/docker.list > /dev/null
            
            apt-get update
            apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
            ;;
        centos|rhel|fedora)
            yum install -y yum-utils
            yum-config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
            yum install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
            systemctl start docker
            ;;
        *)
            echo "❌ Unsupported OS: $OS"
            exit 1
            ;;
    esac
    echo "✅ Docker installed"
else
    echo "✅ Docker already installed"
fi

# Start and enable Docker
systemctl enable docker
systemctl start docker

# Verify installation
echo ""
echo "🔍 Verifying installation..."
docker --version
docker compose version

echo ""
echo "✅ Docker installed successfully!"
echo ""
echo "📝 Next steps:"
echo "   - For standalone: docker run hello-world"
echo "   - For Swarm manager: docker swarm init --advertise-addr <your-ip>"
echo "   - For Swarm worker: docker swarm join --token <token> <manager-ip>:2377"
echo ""
