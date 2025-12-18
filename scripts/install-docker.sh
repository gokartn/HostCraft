#!/bin/bash

# Make installation fully non-interactive
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=a
export NEEDRESTART_SUSPEND=1
export UCF_FORCE_CONFFOLD=1
export DEBIAN_PRIORITY=critical

# Don't exit on error - we'll handle errors gracefully
set +e

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
mkdir -p /etc/needrestart/conf.d 2>/dev/null || true
cat > /etc/needrestart/conf.d/50-hostcraft.conf <<'EOF' 2>/dev/null || true
$nrconf{restart} = 'a';
$nrconf{kernelhints} = 0;
EOF

# Also disable service restarts during package installation
mkdir -p /etc/apt/apt.conf.d 2>/dev/null || true
echo 'DPkg::Pre-Install-Pkgs {"/bin/true";};' > /etc/apt/apt.conf.d/99hostcraft 2>/dev/null || true

# Check if Docker is already installed
if command -v docker &> /dev/null; then
    echo "✅ Docker already installed"
    docker --version
else
    echo "📦 Installing Docker..."
    
    case $OS in
        ubuntu|debian)
            # Update package index non-interactively
            echo "🔄 Updating package index..."
            apt-get update -qq -o Dpkg::Options::="--force-confdef" -o Dpkg::Options::="--force-confold" 2>&1 | grep -v "^\(Ign\|Get\|Hit\|Reading\|Building\)" || true
            
            # Install prerequisites without prompts
            echo "📦 Installing prerequisites..."
            DEBIAN_FRONTEND=noninteractive apt-get install -y -qq \
                --no-install-recommends \
                --allow-downgrades \
                --allow-remove-essential \
                --allow-change-held-packages \
                -o Dpkg::Options::="--force-confdef" \
                -o Dpkg::Options::="--force-confold" \
                -o Dpkg::Use-Pty=0 \
                ca-certificates curl gnupg lsb-release apt-transport-https 2>&1 | grep -v "^\(Selecting\|Preparing\|Unpacking\|Setting\)" || true
            
            # Add Docker's official GPG key (batch mode for non-interactive)
            echo "🔑 Adding Docker GPG key..."
            mkdir -p /etc/apt/keyrings
            curl -fsSL https://download.docker.com/linux/$OS/gpg | gpg --batch --yes --dearmor -o /etc/apt/keyrings/docker.gpg 2>/dev/null || curl -fsSL https://download.docker.com/linux/$OS/gpg > /etc/apt/keyrings/docker.gpg
            chmod a+r /etc/apt/keyrings/docker.gpg
            
            # Add Docker repository
            echo "📝 Adding Docker repository..."
            echo \
              "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/$OS \
              $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
              tee /etc/apt/sources.list.d/docker.list > /dev/null
            
            # Install Docker without any prompts
            echo "🐳 Installing Docker CE..."
            apt-get update -qq -o Dpkg::Options::="--force-confdef" -o Dpkg::Options::="--force-confold" 2>&1 | grep -v "^\(Ign\|Get\|Hit\|Reading\|Building\)" || true
            DEBIAN_FRONTEND=noninteractive apt-get install -y -qq \
                --no-install-recommends \
                --allow-downgrades \
                --allow-remove-essential \
                --allow-change-held-packages \
                -o Dpkg::Options::="--force-confdef" \
                -o Dpkg::Options::="--force-confold" \
                -o Dpkg::Use-Pty=0 \
                docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin 2>&1 | grep -v "^\(Selecting\|Preparing\|Unpacking\|Setting\)" || true
            ;;
        centos|rhel|fedora)
            # Install Docker on RHEL-based systems non-interactively
            yum install -y -q yum-utils
            yum-config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
            yum install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
            ;;
        *)
            echo "❌ Unsupported OS: $OS"
            exit 1
            ;;
    esac
    echo "✅ Docker installed"
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

# Verify Docker is working
docker info >/dev/null 2>&1 && echo "✅ Docker daemon is running"

# Clean up temporary configs
rm -f /etc/needrestart/conf.d/50-hostcraft.conf 2>/dev/null || true
rm -f /etc/apt/apt.conf.d/99hostcraft 2>/dev/null || true

echo ""
echo "============================================"
echo "✅ Docker installed successfully!"
echo "✅ Installation completed without requiring restart"
echo "============================================"
echo ""
echo "📝 HostCraft can now manage this server"
echo "   - Ready for standalone container deployments"
echo "   - Ready to join Docker Swarm"
echo ""
exit 0
