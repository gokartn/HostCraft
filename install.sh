#!/bin/bash
set -euo pipefail

# HostCraft Installation Script - Security Hardened
# Version: 0.0.1-alpha
# This script implements production-grade security measures by default

echo "================================================"
echo "  HostCraft Installation (Security Hardened)"
echo "  Version: 0.0.1-alpha"
echo "================================================"
echo ""
echo "Press Enter to accept default options in [brackets]"
echo ""

# Configuration
CONFIG_DIR="/var/lib/hostcraft"
CONFIG_FILE="$CONFIG_DIR/install.conf"
SECRETS_DIR="$CONFIG_DIR/secrets"
TRAEFIK_DIR="$CONFIG_DIR/traefik/dynamic"
KEY_FILE="$SECRETS_DIR/encryption.key"
REGISTRY_DIR="$CONFIG_DIR/registry"
REGISTRY_PASSWORD_FILE="$SECRETS_DIR/registry-password"
SWARM_UNLOCK_KEY_FILE="$SECRETS_DIR/swarm-unlock-key"

# Initialize variables
init_swarm=""
custom_password=""
TRAEFIK_EMAIL=""
HOSTCRAFT_DOMAIN=""

# Load saved configuration if exists
load_config() {
    if [ -f "$CONFIG_FILE" ]; then
        echo "[Info] Loading saved configuration..."
        source "$CONFIG_FILE"
        echo "   [OK] Configuration loaded"
        echo ""
    fi
}

# Save configuration for future installs
save_config() {
    mkdir -p "$CONFIG_DIR"
    cat > "$CONFIG_FILE" << EOF
# HostCraft Install Configuration
# Generated: $(date)
SAVED_CUSTOM_PASSWORD="${custom_password:-}"
SAVED_INIT_SWARM="${init_swarm:-}"
SAVED_TRAEFIK_EMAIL="${TRAEFIK_EMAIL:-}"
SAVED_HOSTCRAFT_DOMAIN="${HOSTCRAFT_DOMAIN:-}"
EOF
    chmod 600 "$CONFIG_FILE"
    echo "[Save] Configuration saved to $CONFIG_FILE"
}

load_config

# =============================================================================
# SECURITY FUNCTIONS (P0 - Critical)
# =============================================================================

# Configure UFW firewall BEFORE Docker installation
setup_firewall() {
    # Check if running as root
    if [ "$EUID" -ne 0 ]; then
        return
    fi

    echo "[Firewall] Configuring UFW..."

    # Install UFW if not present
    if ! command -v ufw &> /dev/null; then
        apt-get update -qq
        apt-get install -y ufw > /dev/null 2>&1
    fi

    # Default policies: deny all incoming, allow outgoing
    ufw --force default deny incoming > /dev/null 2>&1
    ufw --force default allow outgoing > /dev/null 2>&1

    # Allow SSH, HTTP, HTTPS only
    ufw allow 22/tcp comment 'SSH' > /dev/null 2>&1
    ufw allow 80/tcp comment 'HTTP - Traefik' > /dev/null 2>&1
    ufw allow 443/tcp comment 'HTTPS - Traefik' > /dev/null 2>&1

    # CRITICAL: Docker Swarm ports (2377, 7946, 4789) are BLOCKED from internet
    # They will only be accessible via localhost or private networks

    # Enable firewall
    ufw --force enable > /dev/null 2>&1
    echo "   [OK] Firewall configured"
}

# Secure Docker daemon configuration
configure_docker_security() {
    local enable_swarm="${1:-false}"

    # Check if running as root
    if [ "$EUID" -ne 0 ]; then
        return
    fi

    echo "[Docker] Securing daemon..."

    # Create Docker daemon config directory
    mkdir -p /etc/docker

    # Backup existing config if present
    if [ -f /etc/docker/daemon.json ]; then
        cp /etc/docker/daemon.json /etc/docker/daemon.json.backup.$(date +%s)
    fi

    # Create secure daemon configuration
    # CRITICAL: live-restore MUST be false (or omitted) for Swarm mode
    if [ "$enable_swarm" = "true" ]; then
        echo "   [Info] Configuring Docker for Swarm mode (live-restore disabled)"
        cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  },
  "userland-proxy": false,
  "no-new-privileges": true
}
EOF
    else
        echo "   [Info] Configuring Docker for standalone mode (live-restore enabled)"
        cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  },
  "live-restore": true,
  "userland-proxy": false,
  "no-new-privileges": true
}
EOF
    fi

    systemctl restart docker
    sleep 3
    echo "   [OK] Docker daemon configured"
}

# Initialize Docker Swarm with secure settings
init_secure_swarm() {
    local advertise_ip="$1"
    local enable_autolock="${2:-false}"

    # Detect private network interface (common patterns)
    PRIVATE_IP=""
    for interface in ens10 eth1 ens3; do
        PRIVATE_IP=$(ip addr show dev "$interface" 2>/dev/null | grep "inet " | awk '{print $2}' | cut -d/ -f1 || true)
        if [ -n "$PRIVATE_IP" ]; then
            break
        fi
    done

    # Determine listen address (CRITICAL SECURITY SETTING)
    if [ -n "$PRIVATE_IP" ]; then
        # Multi-node setup: Use private network
        LISTEN_ADDR="$PRIVATE_IP:2377"
        ADVERTISE_ADDR="$PRIVATE_IP:2377"
    else
        # Single-node setup: Bind to localhost ONLY
        LISTEN_ADDR="127.0.0.1:2377"
        ADVERTISE_ADDR="${advertise_ip:-127.0.0.1}:2377"
    fi

    # Initialize Swarm with secure settings
    if docker swarm init \
        --advertise-addr "$ADVERTISE_ADDR" \
        --listen-addr "$LISTEN_ADDR" \
        --cert-expiry 2160h 2>&1; then

        # Rotate join tokens immediately (security best practice)
        docker swarm join-token --rotate worker -q > /dev/null 2>&1
        docker swarm join-token --rotate manager -q > /dev/null 2>&1

        # Optionally lock the Swarm (requires unlock key after restart)
        if [ "$enable_autolock" = "true" ]; then
            echo "   [Security] Enabling Swarm autolock..."
            UNLOCK_KEY=$(docker swarm update --autolock=true 2>&1 | grep -A2 "SWMKEY" | grep "SWMKEY" || true)

            if [ -n "$UNLOCK_KEY" ]; then
                # Save unlock key securely
                mkdir -p "$SECRETS_DIR"
                echo "$UNLOCK_KEY" > "$SWARM_UNLOCK_KEY_FILE"
                chmod 600 "$SWARM_UNLOCK_KEY_FILE"
                echo "   [OK] Autolock enabled - unlock key saved to $SWARM_UNLOCK_KEY_FILE"
                echo "   [IMPORTANT] Backup this key! Swarm will require manual unlock after Docker restarts"
            fi
        else
            echo "   [Info] Swarm autolock disabled - automatic recovery enabled"
        fi

        return 0
    else
        return 1
    fi
}

# Deploy Docker Registry with authentication
deploy_secure_registry() {
    # Check if running as root
    if [ "$EUID" -ne 0 ]; then
        return
    fi

    echo "[Registry] Configuring authentication..."

    # Generate registry credentials
    REGISTRY_USER="hostcraft"
    REGISTRY_PASS=$(openssl rand -base64 32)

    # Store password securely
    mkdir -p "$SECRETS_DIR"
    echo "$REGISTRY_PASS" > "$REGISTRY_PASSWORD_FILE"
    chmod 600 "$REGISTRY_PASSWORD_FILE"

    # Create htpasswd file for registry authentication
    mkdir -p "$REGISTRY_DIR"
    docker run --rm --entrypoint htpasswd httpd:2 -Bbn "$REGISTRY_USER" "$REGISTRY_PASS" \
        > "$REGISTRY_DIR/htpasswd" 2>/dev/null
    chmod 600 "$REGISTRY_DIR/htpasswd"

    echo "   [OK] Registry authentication configured"
}

# =============================================================================
# 1. Check Root Privileges & Configure Security
# =============================================================================
if [ "$EUID" -ne 0 ]; then
    echo "[Warn] Not running as root - security features will be skipped"
    echo "        For production, run: sudo ./install.sh"
    echo ""
fi

# Configure firewall (BEFORE Docker)
setup_firewall

# =============================================================================
# 2. Check Docker Installation (Auto-install if missing)
# =============================================================================
echo "[Docker] Checking Docker installation..."
if ! command -v docker &> /dev/null; then
    echo "   [Info] Docker is not installed"
    echo ""

    if [ "$EUID" -ne 0 ]; then
        echo "[Error] Docker is not installed and this script needs root privileges to install it."
        echo "Please either:"
        echo "  1. Run this script with sudo: sudo ./install.sh"
        echo "  2. Install Docker manually: https://docs.docker.com/engine/install/"
        exit 1
    fi

    echo "[Docker] Docker Installation Required"
    echo "---------------------------------------"
    echo "HostCraft requires Docker to be installed."
    echo "This script can automatically install Docker for you."
    echo ""
    read -p "Install Docker automatically? [yes]: " install_docker
    install_docker=${install_docker:-yes}

    case $install_docker in
        yes|y|Y|YES)
            echo ""
            echo "[Docker] Installing Docker..."

            # Check if install-docker.sh exists
            if [ -f "scripts/install-docker.sh" ]; then
                chmod +x scripts/install-docker.sh
                if bash scripts/install-docker.sh; then
                    echo "   [OK] Docker installed successfully!"
                else
                    echo "   [Error] Docker installation failed"
                    echo "   Please install Docker manually: https://docs.docker.com/engine/install/"
                    exit 1
                fi
            else
                # Fall back to inline installation
                echo "   [Info] Using inline Docker installation..."

                # Detect OS
                if [ -f /etc/os-release ]; then
                    . /etc/os-release
                    OS_ID=$ID
                else
                    echo "   [Error] Cannot detect OS"
                    exit 1
                fi

                case $OS_ID in
                    ubuntu|debian)
                        apt-get update -qq
                        apt-get install -y ca-certificates curl gnupg
                        mkdir -p /etc/apt/keyrings
                        curl -fsSL https://download.docker.com/linux/$OS_ID/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
                        chmod a+r /etc/apt/keyrings/docker.gpg
                        echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/$OS_ID $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null
                        apt-get update -qq
                        apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
                        ;;
                    centos|rhel|fedora)
                        yum install -y yum-utils
                        yum-config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
                        yum install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
                        ;;
                    *)
                        echo "   [Error] Unsupported OS: $OS_ID"
                        echo "   Please install Docker manually: https://docs.docker.com/engine/install/"
                        exit 1
                        ;;
                esac

                # Start Docker
                systemctl enable docker --now
                sleep 3
                echo "   [OK] Docker installed successfully!"
            fi
            ;;
        *)
            echo "   [Info] Skipping Docker installation"
            echo "   Please install Docker manually: https://docs.docker.com/engine/install/"
            exit 1
            ;;
    esac
    echo ""
fi

# Verify Docker is running
if ! docker info &> /dev/null; then
    echo "[Error] Docker is installed but not running."
    if [ "$EUID" -eq 0 ]; then
        echo "   [Info] Attempting to start Docker..."
        systemctl start docker
        sleep 3
        if ! docker info &> /dev/null; then
            echo "   [Error] Failed to start Docker. Please check: systemctl status docker"
            exit 1
        fi
        echo "   [OK] Docker started successfully"
    else
        echo "   Please start Docker: sudo systemctl start docker"
        exit 1
    fi
fi

# Check Docker Compose
if ! docker compose version &> /dev/null; then
    echo "[Error] Docker Compose plugin is not available."
    echo "This should have been installed with Docker."
    echo "Please reinstall Docker or install the compose plugin manually."
    exit 1
fi

echo "   [OK] Docker and Docker Compose are installed and running"
echo ""

# =============================================================================
# 3. Detect Swarm Mode & Ask User Preference (BEFORE Docker config!)
# =============================================================================
echo "[Swarm] Checking Docker Swarm status..."
SWARM_STATUS=$(docker info 2>/dev/null | grep "Swarm:" | awk '{print $2}' || echo "inactive")
IS_MANAGER=$(docker info 2>/dev/null | grep "Is Manager:" | awk '{print $3}' || echo "false")

# Initialize variables
SWARM_ACTIVE="false"
ENABLE_SWARM="false"

# Check if this node is a Swarm manager (required for stack deployment)
if [ "$SWARM_STATUS" = "active" ] && [ "$IS_MANAGER" = "true" ]; then
    SWARM_ACTIVE="true"
    ENABLE_SWARM="true"
    echo "   [OK] Docker Swarm is active (this node is a manager)"
elif [ "$SWARM_STATUS" = "active" ] && [ "$IS_MANAGER" != "true" ]; then
    # Node is in Swarm but not a manager (worker or corrupted state)
    echo "   [Warn] Docker Swarm is active but this node is NOT a manager"
    echo "   [Info] Stack deployment requires a manager node"
    echo ""
    read -p "Leave current Swarm and reinitialize as manager? [yes]: " leave_swarm
    leave_swarm=${leave_swarm:-yes}

    case $leave_swarm in
        yes|y|Y|YES)
            echo "   [Action] Leaving current Swarm..."
            docker swarm leave --force 2>/dev/null || true
            SWARM_ACTIVE="false"
            echo "   [OK] Left Swarm, will reinitialize as manager"
            ;;
        *)
            echo "   [Error] Cannot deploy to Swarm without being a manager"
            echo "   [Info] Continuing in standalone mode"
            SWARM_ACTIVE="false"
            ENABLE_SWARM="false"
            ;;
    esac
    echo ""
fi

if [ "$SWARM_ACTIVE" != "true" ]; then
    SWARM_ACTIVE="false"
    echo "   [Info] Docker Swarm is not initialized"
    echo ""

    # Ask about Swarm BEFORE configuring Docker daemon
    echo "[Swarm] Docker Swarm Initialization"
    echo "======================================"
    echo "HostCraft is designed for HIGH AVAILABILITY and DISASTER RECOVERY."
    echo ""
    echo "Swarm Mode Benefits:"
    echo "  ✓ Multi-node clustering with automatic failover"
    echo "  ✓ Zero-downtime deployments (rolling updates)"
    echo "  ✓ Service replication across nodes"
    echo "  ✓ Built-in load balancing and service discovery"
    echo "  ✓ Automatic replica rescheduling on node failure"
    echo ""
    echo "Single-Node Limitations:"
    echo "  ⚠ No fault tolerance (node failure = downtime)"
    echo "  ⚠ Cannot survive hardware failures"
    echo "  ⚠ Maintenance requires downtime"
    echo ""
    echo "For TRUE HA/DR, plan to add 2-3 manager nodes after installation."
    echo ""
    DEFAULT_INIT_SWARM="${SAVED_INIT_SWARM:-yes}"
    read -p "Initialize Docker Swarm? [$DEFAULT_INIT_SWARM]: " init_swarm
    init_swarm=${init_swarm:-$DEFAULT_INIT_SWARM}

    case $init_swarm in
        yes|y|Y|YES)
            ENABLE_SWARM="true"
            echo "[Info] Will configure Docker for Swarm mode"
            echo ""

            # Ask about autolock (security vs availability trade-off)
            echo "[Swarm] Autolock Configuration"
            echo "-------------------------------"
            echo "Swarm autolock encrypts Raft logs at rest for security."
            echo ""
            echo "IMPORTANT: Autolock requires MANUAL UNLOCK after Docker daemon restarts!"
            echo "   - Single-node: Manual unlock = DOWNTIME (not HA/DR compliant)"
            echo "   - Multi-node: Other nodes continue, but locked node is unavailable"
            echo ""
            echo "Recommendation:"
            echo "   - Single-node setup: DISABLE autolock (for automatic recovery)"
            echo "   - Multi-node setup: ENABLE autolock (security + HA maintained)"
            echo ""
            read -p "Enable Swarm autolock? [no]: " enable_autolock
            enable_autolock=${enable_autolock:-no}

            case $enable_autolock in
                yes|y|Y|YES)
                    ENABLE_AUTOLOCK="true"
                    echo "[Info] Autolock will be enabled"
                    ;;
                *)
                    ENABLE_AUTOLOCK="false"
                    echo "[Info] Autolock will be disabled (automatic recovery enabled)"
                    ;;
            esac
            ;;
        *)
            ENABLE_SWARM="false"
            ENABLE_AUTOLOCK="false"
            echo "[Info] Will configure Docker for standalone mode"
            ;;
    esac
fi
echo ""

# =============================================================================
# 4. Secure Docker Daemon (with Swarm-aware configuration)
# =============================================================================
# Skip Docker reconfiguration if Swarm is already active (avoid breaking Swarm)
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "[Docker] Skipping daemon reconfiguration (Swarm already active)"
else
    configure_docker_security "$ENABLE_SWARM"
fi

# =============================================================================
# 5. Initialize Swarm (if requested and not already active)
# =============================================================================
if [ "$SWARM_ACTIVE" = "false" ] && [ "$ENABLE_SWARM" = "true" ]; then
    echo "[Swarm] Initializing Docker Swarm..."
    echo ""

    # Try to detect public IP for advertise address
    PUBLIC_IP=$(curl -4 -s --connect-timeout 3 https://ifconfig.me 2>/dev/null || true)

    # Validate IPv4
    if [ -n "$PUBLIC_IP" ] && ! echo "$PUBLIC_IP" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
        PUBLIC_IP=""
    fi

    if [ -n "$PUBLIC_IP" ]; then
        echo "   [Info] Detected public IP: $PUBLIC_IP"
    fi

    # Initialize Swarm with SECURE settings
    if init_secure_swarm "$PUBLIC_IP" "$ENABLE_AUTOLOCK"; then
        SWARM_ACTIVE="true"
        echo "   [OK] Docker Swarm initialized successfully!"
        echo ""
    else
        echo ""
        echo "   [Error] Swarm initialization failed"
        echo "   This should not happen since live-restore is disabled."
        echo ""
        echo "   Available IP addresses:"
        ip -4 addr show 2>/dev/null | grep -oP '(?<=inet\s)\d+(\.\d+){3}' | grep -v '^127\.' | while read ip; do
            echo "      $ip"
        done
        echo ""
        read -p "   Enter advertise IP (or Enter to continue without Swarm): " ADVERTISE_ADDR
        if [ -n "$ADVERTISE_ADDR" ]; then
            if init_secure_swarm "$ADVERTISE_ADDR" "$ENABLE_AUTOLOCK"; then
                SWARM_ACTIVE="true"
                echo "   [OK] Docker Swarm initialized successfully!"
            else
                echo "   [Error] Swarm initialization failed again"
                echo "   Continuing installation in standalone mode..."
                SWARM_ACTIVE="false"
            fi
        else
            echo "   [Info] Continuing installation in standalone mode..."
            SWARM_ACTIVE="false"
        fi
        echo ""
    fi
fi

# =============================================================================
# 5.5. Verify Swarm is Unlocked (if active)
# =============================================================================
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "[Swarm] Verifying Swarm is unlocked and functional..."

    # Try to run a Swarm command to check if it's locked
    if ! docker node ls &>/dev/null; then
        echo "   [Warn] Swarm appears to be locked"

        if [ -f "$SWARM_UNLOCK_KEY_FILE" ]; then
            echo "   [Action] Attempting to unlock Swarm with saved key..."
            cat "$SWARM_UNLOCK_KEY_FILE" | docker swarm unlock 2>/dev/null || true

            if docker node ls &>/dev/null; then
                echo "   [OK] Swarm unlocked successfully"
            else
                echo "   [Error] Failed to unlock Swarm"
                echo "   [Info] You may need to manually unlock with: docker swarm unlock"
                echo "   [Info] Continuing anyway..."
            fi
        else
            echo "   [Error] Swarm is locked but no unlock key found"
            echo "   [Info] You may need to manually unlock with: docker swarm unlock"
            echo "   [Info] Continuing anyway..."
        fi
    else
        echo "   [OK] Swarm is functional"
    fi
    echo ""
fi

# =============================================================================
# 6. Create Required Directories
# =============================================================================
echo "[Setup] Creating directories..."
mkdir -p "$SECRETS_DIR"
mkdir -p "$TRAEFIK_DIR"
mkdir -p "$REGISTRY_DIR"
if [ "$EUID" -eq 0 ]; then
    chmod 700 "$SECRETS_DIR"
    chmod 755 "$TRAEFIK_DIR"
    chmod 755 "$REGISTRY_DIR"
fi
echo "   [OK] Directories created"
echo ""

# =============================================================================
# 7. Deploy Secure Registry
# =============================================================================
deploy_secure_registry

# =============================================================================
# 8. Collect Configuration Variables
# =============================================================================

# Database Password
echo "[Database] Password Configuration"
echo "-----------------------------------"
DEFAULT_CUSTOM_PASSWORD="${SAVED_CUSTOM_PASSWORD:-no}"
read -p "Set a custom database password? [$DEFAULT_CUSTOM_PASSWORD]: " custom_password
custom_password=${custom_password:-$DEFAULT_CUSTOM_PASSWORD}

case $custom_password in
    yes|y|Y|YES)
        while true; do
            echo ""
            read -s -p "Enter database password: " POSTGRES_PASSWORD
            echo ""
            read -s -p "Confirm database password: " POSTGRES_PASSWORD_CONFIRM
            echo ""
            if [ "$POSTGRES_PASSWORD" != "$POSTGRES_PASSWORD_CONFIRM" ]; then
                echo "[Error] Passwords do not match. Try again."
                continue
            fi
            if [ -z "$POSTGRES_PASSWORD" ]; then
                echo "[Error] Password cannot be empty. Try again."
                continue
            fi
            echo "[OK] Custom database password set"
            break
        done
        ;;
    *)
        POSTGRES_PASSWORD="HostCraft2024!SecureDefault"
        echo "[Info] Using default database password"
        ;;
esac
echo ""

# Encryption Key
echo "[Security] Encryption Key Management"
echo "-------------------------------------"
if [ -f "$KEY_FILE" ]; then
    ENCRYPTION_KEY=$(cat "$KEY_FILE")
    echo "   [OK] Using existing encryption key"
else
    echo "[Action] Generating new encryption key..."
    ENCRYPTION_KEY=$(openssl rand -base64 32)
    echo "$ENCRYPTION_KEY" > "$KEY_FILE"
    chmod 600 "$KEY_FILE"
    echo "   [OK] New encryption key generated and saved"
fi
echo ""

# Registry HTTP Secret
echo "[Registry] Generating registry secret..."
REGISTRY_HTTP_SECRET=$(openssl rand -hex 32)
echo "   [OK] Registry secret generated"
echo ""

# Domain Configuration
echo "[Domain] Domain Configuration"
echo "------------------------------"
echo "IMPORTANT: Let's Encrypt SSL certificates require a real domain name."
echo "If you're setting up for production, enter your domain now."
echo "If you're testing locally, press Enter to use 'hostcraft.localhost' (no SSL)."
echo ""

DEFAULT_DOMAIN="${SAVED_HOSTCRAFT_DOMAIN:-}"
if [ -n "$DEFAULT_DOMAIN" ] && [ "$DEFAULT_DOMAIN" != "hostcraft.localhost" ]; then
    read -p "Enter your domain [$DEFAULT_DOMAIN]: " HOSTCRAFT_DOMAIN
    HOSTCRAFT_DOMAIN=${HOSTCRAFT_DOMAIN:-$DEFAULT_DOMAIN}
else
    read -p "Enter your domain (e.g., hostcraft.yourdomain.com) or Enter for local: " HOSTCRAFT_DOMAIN
fi

if [ -z "$HOSTCRAFT_DOMAIN" ] || [ "$HOSTCRAFT_DOMAIN" = "hostcraft.localhost" ]; then
    HOSTCRAFT_DOMAIN="hostcraft.localhost"
    HOSTCRAFT_API_DOMAIN="hostcraft.localhost"
    ENABLE_HTTPS="false"
    echo "   [Info] Using 'hostcraft.localhost' for local development (no SSL)"
    echo "   [Note] You can configure a real domain later via Settings → Domain & SSL"
else
    HOSTCRAFT_API_DOMAIN="$HOSTCRAFT_DOMAIN"
    ENABLE_HTTPS="true"
    echo "   [OK] Domain: $HOSTCRAFT_DOMAIN"
    echo "   [Info] Make sure DNS A record points to this server's IP"
fi
echo ""

# Traefik Email (only if real domain configured)
if [ "$ENABLE_HTTPS" = "true" ]; then
    echo "[SSL] Let's Encrypt Configuration"
    echo "----------------------------------"
    DEFAULT_TRAEFIK_EMAIL="${SAVED_TRAEFIK_EMAIL:-}"
    if [ -n "$DEFAULT_TRAEFIK_EMAIL" ]; then
        read -p "Email for Let's Encrypt notifications [$DEFAULT_TRAEFIK_EMAIL]: " TRAEFIK_EMAIL
        TRAEFIK_EMAIL=${TRAEFIK_EMAIL:-$DEFAULT_TRAEFIK_EMAIL}
    else
        read -p "Email for Let's Encrypt notifications: " TRAEFIK_EMAIL
    fi

    if [ -z "$TRAEFIK_EMAIL" ]; then
        TRAEFIK_EMAIL="admin@example.com"
        echo "   [Warn] No email provided. SSL certificate requests may fail."
        echo "   [Info] You can update this later via Settings → Domain & SSL"
    else
        echo "   [OK] Let's Encrypt email: $TRAEFIK_EMAIL"
    fi
    echo ""
else
    # For local development, use placeholder email
    TRAEFIK_EMAIL="admin@example.com"
    echo "[SSL] Skipping Let's Encrypt configuration (local development mode)"
    echo ""
fi

# =============================================================================
# 9. Detect Build Mode and Set Image Names
# =============================================================================
echo "[Build] Detecting source code..."
if [ -d "src" ] && [ -f "src/HostCraft.Api/Dockerfile" ]; then
    BUILD_FROM_SOURCE="true"
    WEB_IMAGE="hostcraft-web:latest"
    API_IMAGE="hostcraft-api:latest"
    echo "   [OK] Source code detected - will build from source"
    echo "   [Image] Web: $WEB_IMAGE (will build)"
    echo "   [Image] API: $API_IMAGE (will build)"
else
    BUILD_FROM_SOURCE="false"
    WEB_IMAGE="ghcr.io/gokartn/hostcraft-web:latest"
    API_IMAGE="ghcr.io/gokartn/hostcraft-api:latest"
    echo "   [Info] No source code - will use pre-built images"
    echo "   [Image] Web: $WEB_IMAGE (will pull)"
    echo "   [Image] API: $API_IMAGE (will pull)"

    # Download required files if not present
    RELEASE_BASE_URL="https://github.com/gokartn/HostCraft/releases/download/v0.0.1-alpha"

    if [ ! -f "docker-compose.yml" ]; then
        echo "   [Download] docker-compose.yml..."
        # Download release-specific compose file (no build sections)
        if curl -fsSL -o docker-compose.yml "$RELEASE_BASE_URL/docker-compose.release.yml"; then
            echo "   [OK] docker-compose.yml downloaded"
        else
            echo "   [Error] Failed to download docker-compose.yml"
            echo "   [Error] Please download manually from: $RELEASE_BASE_URL/docker-compose.release.yml"
            exit 1
        fi
    fi

    # Download management scripts for user convenience
    echo "   [Download] Management scripts..."

    if [ ! -f "uninstall.sh" ]; then
        curl -fsSL -o uninstall.sh "$RELEASE_BASE_URL/uninstall.sh" 2>/dev/null && chmod +x uninstall.sh && echo "   [OK] uninstall.sh downloaded" || echo "   [Skip] uninstall.sh download failed"
    fi

    if [ ! -f "cleanup.sh" ]; then
        curl -fsSL -o cleanup.sh "$RELEASE_BASE_URL/cleanup.sh" 2>/dev/null && chmod +x cleanup.sh && echo "   [OK] cleanup.sh downloaded" || echo "   [Skip] cleanup.sh download failed"
    fi
fi
echo ""

# =============================================================================
# 10. Set Network Driver and Localhost Configuration
# =============================================================================
if [ "$SWARM_ACTIVE" = "true" ]; then
    NETWORK_DRIVER="overlay"
    LOCALHOST_IS_SWARM_MANAGER="true"
    echo "[Network] Using overlay network (Swarm mode)"
else
    NETWORK_DRIVER="bridge"
    LOCALHOST_IS_SWARM_MANAGER="false"
    echo "[Network] Using bridge network (standalone mode)"
fi
echo ""

# =============================================================================
# 11. Write .env File
# =============================================================================
echo "[Config] Writing .env file..."
cat > .env << EOF
# HostCraft Environment Configuration
# Generated: $(date)

# Build Configuration
BUILD_FROM_SOURCE=${BUILD_FROM_SOURCE}
WEB_IMAGE=${WEB_IMAGE}
API_IMAGE=${API_IMAGE}

# Network Configuration
NETWORK_DRIVER=${NETWORK_DRIVER}
COMPOSE_PROJECT_NAME=hostcraft

# Database Configuration
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}

# Security
ENCRYPTION_KEY=${ENCRYPTION_KEY}
REGISTRY_HTTP_SECRET=${REGISTRY_HTTP_SECRET}

# Traefik Configuration
ENABLE_TRAEFIK=true
TRAEFIK_EMAIL=${TRAEFIK_EMAIL}
LETSENCRYPT_EMAIL=${TRAEFIK_EMAIL}

# Registry Configuration
ENABLE_REGISTRY=true

# Ports
WEB_PORT=5050
API_PORT=5100

# Localhost Server Configuration
LOCALHOST_IS_SWARM_MANAGER=${LOCALHOST_IS_SWARM_MANAGER}
SKIP_LOCALHOST_SEED=false

# Domain Configuration
HOSTCRAFT_DOMAIN=${HOSTCRAFT_DOMAIN}
HOSTCRAFT_API_DOMAIN=${HOSTCRAFT_API_DOMAIN}
TRAEFIK_DOMAIN=traefik.${HOSTCRAFT_DOMAIN}

# Environment
ASPNETCORE_ENVIRONMENT=Production
EOF

chmod 600 .env
echo "   [OK] .env file created"
echo ""

# =============================================================================
# 12. Stop Existing Deployment
# =============================================================================
echo "[Cleanup] Removing existing deployment..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    docker stack rm hostcraft 2>/dev/null || true
    echo "   [Wait] Waiting for services to stop..."
    sleep 10
else
    docker compose down 2>/dev/null || true
fi
echo "   [OK] Cleanup complete"
echo ""

# =============================================================================
# 13. Validate and Fix Network Configuration
# =============================================================================
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "[Network] Validating overlay network..."
    NETWORK_NAME="hostcraft_hostcraft-network"
    NETWORK_SUBNET="10.0.100.0/24"

    # Check if network exists and is healthy
    if docker network inspect "$NETWORK_NAME" &>/dev/null; then
        # Validate network driver
        CURRENT_DRIVER=$(docker network inspect "$NETWORK_NAME" --format '{{.Driver}}')
        CURRENT_SCOPE=$(docker network inspect "$NETWORK_NAME" --format '{{.Scope}}')

        if [ "$CURRENT_DRIVER" != "overlay" ] || [ "$CURRENT_SCOPE" != "swarm" ]; then
            echo "   [Warn] Network has incorrect driver or scope"
            echo "   [Action] Removing and recreating network..."

            # Wait for network to be free
            for i in {1..30}; do
                if docker network rm "$NETWORK_NAME" 2>/dev/null; then
                    echo "   [OK] Old network removed"
                    break
                fi
                sleep 2
            done

            # Recreate with correct settings
            docker network create \
                --driver overlay \
                --subnet "$NETWORK_SUBNET" \
                --attachable \
                "$NETWORK_NAME"
            echo "   [OK] Network recreated"
        else
            echo "   [OK] Network is valid"
        fi
    else
        echo "   [Info] Network does not exist, will be created by stack deployment"
    fi
    echo ""
fi

# =============================================================================
# 14. Build or Pull Images
# =============================================================================
if [ "$BUILD_FROM_SOURCE" = "true" ]; then
    echo "[Build] Building Docker images from source..."
    docker compose build --no-cache
else
    echo "[Pull] Pulling pre-built images..."
    docker compose pull
fi
echo ""

# =============================================================================
# 14b. Free Ports 80/443 for Traefik
# =============================================================================
echo "[Ports] Checking if ports 80/443 are available for Traefik..."
for port in 80 443; do
    # Use exact port match (space or colon before, nothing or space after) to avoid :8080 matching :80
    BLOCKING_PID=$(ss -tlnp 2>/dev/null | awk -v p=":${port}" '$4 ~ p"$" || $4 ~ p" " {print}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1 || true)
    if [ -n "$BLOCKING_PID" ]; then
        BLOCKING_SERVICE=$(ps -p "$BLOCKING_PID" -o comm= 2>/dev/null || echo "unknown")
        echo "   [Conflict] Port ${port} is in use by ${BLOCKING_SERVICE} (PID ${BLOCKING_PID})"

        # Try to stop and disable the service if it's a known web server
        case "$BLOCKING_SERVICE" in
            apache2|httpd|nginx)
                echo "   [Fix] Stopping and disabling ${BLOCKING_SERVICE}..."
                systemctl stop "$BLOCKING_SERVICE" 2>/dev/null || true
                systemctl disable "$BLOCKING_SERVICE" 2>/dev/null || true
                echo "   [OK] ${BLOCKING_SERVICE} stopped and disabled"
                ;;
            *)
                echo "   [Warning] Unknown service on port ${port}. Traefik may fail to start."
                echo "   [Warning] Run: kill ${BLOCKING_PID}  or  systemctl stop <service>"
                ;;
        esac
    else
        echo "   [OK] Port ${port} is available"
    fi
done
echo ""

# =============================================================================
# 15. Deploy
# =============================================================================
echo "[Deploy] Starting HostCraft..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "   [Mode] Deploying as Docker Swarm stack..."

    # Deploy with registry and traefik profiles
    docker stack deploy -c docker-compose.yml --with-registry-auth hostcraft

    echo "   [OK] Stack deployed successfully"
    echo "   [Info] Check status: docker stack ps hostcraft"
else
    echo "   [Mode] Deploying with Docker Compose..."

    # Deploy with registry and traefik profiles
    docker compose --profile traefik --profile registry up -d

    echo "   [OK] Containers started successfully"
    echo "   [Info] Check status: docker compose ps"
fi
echo ""

# =============================================================================
# 16. Wait for Services
# =============================================================================
echo "[Wait] Waiting for PostgreSQL to be ready..."
sleep 10

if [ "$SWARM_ACTIVE" = "true" ]; then
    # Find postgres container in swarm
    for i in {1..30}; do
        POSTGRES_CONTAINER=$(docker ps --filter "label=com.docker.swarm.service.name=hostcraft_postgres" --format "{{.ID}}" | head -n 1)
        if [ -n "$POSTGRES_CONTAINER" ]; then
            break
        fi
        sleep 2
    done

    if [ -n "$POSTGRES_CONTAINER" ]; then
        until docker exec "$POSTGRES_CONTAINER" pg_isready -U hostcraft &>/dev/null; do
            sleep 2
        done
    fi
else
    # Standalone mode
    until docker exec hostcraft-postgres-1 pg_isready -U hostcraft &>/dev/null; do
        sleep 2
    done
fi

echo "   [OK] PostgreSQL is ready"
echo ""

# =============================================================================
# 17. Success Message
# =============================================================================
echo "================================================"
echo "  HostCraft Installation Complete!"
echo "================================================"
echo ""

# Get host IP
HOST_IP=$(hostname -I | awk '{print $1}')

echo "[Access] Your HostCraft instance:"
echo ""
echo "  Web UI: http://${HOST_IP}:5050"
echo "  API:    http://${HOST_IP}:5100"

if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "  Traefik Dashboard: http://${HOST_IP}:8080"
    echo ""

    # Count manager nodes
    MANAGER_COUNT=$(docker node ls --filter "role=manager" --format "{{.ID}}" | wc -l)

    if [ "$MANAGER_COUNT" -eq 1 ]; then
        echo "[HA/DR] SINGLE-NODE DETECTED - For Production HA, Add More Nodes!"
        echo "---------------------------------------------------------------"
        echo "Current Setup: Single-node Swarm (NOT fault-tolerant)"
        echo ""
        echo "Recommended HA Setup: 3-Manager Quorum"
        echo "  • Survives 1 node failure (maintains quorum)"
        echo "  • Zero-downtime deployments and automatic failover"
        echo "  • Geographic distribution (on-prem + cloud)"
        echo ""
        echo "To add a manager node (RECOMMENDED):"
        echo "  1. On new server, install Docker"
        echo "  2. Get join token: docker swarm join-token manager"
        echo "  3. Run join command on new server"
        echo "  4. Services automatically spread across nodes"
        echo ""
        echo "To add a worker node:"
        echo "  1. On new server, install Docker"
        echo "  2. Get join token: docker swarm join-token worker"
        echo "  3. Run join command on new server"
        echo ""
        echo "Service Scaling (after adding nodes):"
        echo "  docker service scale hostcraft_api=3"
        echo "  docker service scale hostcraft_web=3"
        echo ""
    else
        echo "[HA/DR] Multi-Node Swarm Active ($MANAGER_COUNT managers)"
        echo "---------------------------------------"
        echo "Join tokens (add more nodes):"
        echo "  Worker:  docker swarm join-token worker"
        echo "  Manager: docker swarm join-token manager"
        echo ""
    fi
fi

echo ""

# Show SSL/domain-specific instructions
if [ "$HOSTCRAFT_DOMAIN" = "hostcraft.localhost" ]; then
    echo "[Domain] Currently configured for LOCAL DEVELOPMENT:"
    echo "  • Domain: $HOSTCRAFT_DOMAIN (no SSL certificates)"
    echo "  • Access via IP addresses above or configure a real domain"
    echo ""
    echo "[SSL] To enable HTTPS with a real domain:"
    echo "  1. Get a domain name and point DNS A record to: $HOST_IP"
    echo "  2. Ensure ports 80 and 443 are open in firewall"
    echo "  3. Complete setup wizard (create admin account)"
    echo "  4. Go to Settings → Domain & SSL"
    echo "  5. Enter your domain, enable HTTPS, and provide your email"
    echo "  6. SSL certificate will be automatically provisioned"
    echo ""
else
    echo "[Domain] Configured for PRODUCTION:"
    echo "  • Domain: $HOSTCRAFT_DOMAIN"
    echo "  • Let's Encrypt Email: $TRAEFIK_EMAIL"
    echo ""
    echo "[SSL] SSL certificate will be automatically requested:"
    echo "  ✓ Ensure DNS A record '$HOSTCRAFT_DOMAIN' points to: $HOST_IP"
    echo "  ✓ Ensure ports 80 and 443 are open in firewall"
    echo "  ✓ Certificate issuance takes 1-2 minutes after services start"
    echo "  ✓ Access HostCraft at: https://$HOSTCRAFT_DOMAIN (once certificate is ready)"
    echo ""
    echo "[Next Steps]:"
    echo "  1. Wait 1-2 minutes for SSL certificate provisioning"
    echo "  2. Visit https://$HOSTCRAFT_DOMAIN to complete setup wizard"
    echo "  3. Create admin account and start deploying applications"
    echo ""
fi
echo "[Monitoring] Service status:"
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "  docker service ls"
    echo "  docker stack ps hostcraft"
else
    echo "  docker compose ps"
    echo "  docker compose logs -f"
fi
echo ""
echo "[Management] Available commands:"
echo "  ./uninstall.sh  - Remove HostCraft (keeps data for reinstall)"
echo "  ./cleanup.sh    - Complete removal (deletes all data)"
echo ""
echo "================================================"
echo ""

# Save config for next install
save_config

echo "[Done] Installation complete!"
echo ""
