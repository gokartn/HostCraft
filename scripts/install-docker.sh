#!/bin/bash
set -e

# Make installation fully non-interactive
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=a
export NEEDRESTART_SUSPEND=1

echo "================================================"
echo "  HostCraft - Docker Installation Script"
echo "  Non-Interactive Mode - No User Input Required"
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

# Disable interactive prompts for service restarts
echo "📝 Configuring non-interactive mode..."
echo "\$nrconf{restart} = 'a';" > /etc/needrestart/conf.d/50-hostcraft.conf 2>/dev/null || true
echo "\$nrconf{kernelhints} = 0;" >> /etc/needrestart/conf.d/50-hostcraft.conf 2>/dev/null || true

# Install Docker
echo "📦 Installing Docker..."
if ! command -v docker &> /dev/null; then
    case $OS in
        ubuntu|debian)
            # Update package index non-interactively
            apt-get update -qq
            
            # Install prerequisites without prompts
            apt-get install -y -qq \
                --no-install-recommends \
                -o Dpkg::Options::="--force-confdef" \
                -o Dpkg::Options::="--force-confold" \
                ca-certificates curl gnupg lsb-release
            # Add Docker's official GPG key
            install -m 0755 -d /etc/apt/keyrings
            curl -fsSL https://download.docker.com/linux/$OS/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
            chmod a+r /etc/apt/keyrings/docker.gpg
            
            # Add Docker repository
            echo \
              "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/$OS \
              $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
              tee /etc/apt/sources.list.d/docker.list > /dev/null
            
            # Install Docker without any prompts
            apt-get update -qq
            # Install Docker on RHEL-based systems non-interactively
            yum install -y -q yum-utils
            yum-config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
            yum install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
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
echo "🚀 Starting Docker service..."
systemctl enable docker --now 2>/dev/null || systemctl enable docker && systemctl start docker

# Wait for Docker to be ready
echo "⏳ Waiting for Docker to be ready..."
for i in {1..30}; do
    if docker info >/dev/null 2>&1; then
        break
    fi
    sleep 1
done

# Verify installation
echo ""
echo "🔍 Verifying installation..."
docker --version
docker compose version
docker info >/dev/null 2>&1 && echo "✅ Docker daemon is running"

# Clean up needrestart config if it was created
rm -f /etc/needrestart/conf.d/50-hostcraft.conf 2>/dev/null || true

echo ""
echo "✅ Docker installed successfully!"
echo "✅ Installation completed without requiring restart"
echo ""
echo "📝 HostCraft can now manage this server"
echo "   - Ready for standalone container deployments"
echo "   - Ready to join Docker Swarm
echo ""
echo "✅ Docker installed successfully!"
echo ""
echo "📝 Next steps:"
echo "   - For standalone: docker run hello-world"
echo "   - For Swarm manager: docker swarm init --advertise-addr <your-ip>"
echo "   - For Swarm worker: docker swarm join --token <token> <manager-ip>:2377"
echo ""
