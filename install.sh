#!/bin/bash
set -e

echo "🚀 HostCraft Installation Script"
echo "================================="
echo ""
echo "Press Enter to accept the default option shown in [brackets]"
echo ""

# Default values
DEFAULT_POSTGRES_PASSWORD="HostCraft2024!SecureDefault"

# Database password configuration
echo "🗄️  Database Password Configuration"
echo "------------------------------------"
read -p "Set a custom database password? [no]: " custom_password
custom_password=${custom_password:-no}

case $custom_password in
    yes|y|Y|YES)
        while true; do
            echo ""
            read -s -p "Enter your database password: " POSTGRES_PASSWORD
            echo ""
            read -s -p "Confirm your database password: " POSTGRES_PASSWORD_CONFIRM
            echo ""
            if [ "$POSTGRES_PASSWORD" != "$POSTGRES_PASSWORD_CONFIRM" ]; then
                echo "❌ Passwords do not match. Please try again."
                continue
            fi
            if [ -z "$POSTGRES_PASSWORD" ]; then
                echo "❌ Password cannot be empty. Please try again."
                continue
            fi
            echo "✅ Custom database password set"
            break
        done
        ;;
    *)
        POSTGRES_PASSWORD="$DEFAULT_POSTGRES_PASSWORD"
        echo "✅ Using default database password"
        ;;
esac
echo ""

# Generate secure encryption key
echo "🔐 Generating encryption key..."
ENCRYPTION_KEY=$(openssl rand -base64 32)
echo "   ✅ Encryption key generated"
echo ""

# Export variables for docker commands
export ENCRYPTION_KEY
export POSTGRES_PASSWORD

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "⚠️  Docker is not installed on this system."
    echo ""
    read -p "Install Docker now? [yes]: " install_docker
    install_docker=${install_docker:-yes}

    case $install_docker in
        yes|y|Y|YES|"")
            echo "📦 Installing Docker..."

            # Detect OS and install Docker
            if [ -f /etc/os-release ]; then
                . /etc/os-release
                OS=$ID
            else
                echo "❌ Cannot detect OS. Please install Docker manually."
                exit 1
            fi

            case $OS in
                ubuntu|debian)
                    echo "Installing Docker on Ubuntu/Debian..."
                    curl -fsSL https://get.docker.com -o get-docker.sh
                    sh get-docker.sh
                    rm get-docker.sh
                    systemctl enable docker
                    systemctl start docker
                    ;;
                centos|rhel|fedora)
                    echo "Installing Docker on CentOS/RHEL/Fedora..."
                    curl -fsSL https://get.docker.com -o get-docker.sh
                    sh get-docker.sh
                    rm get-docker.sh
                    systemctl enable docker
                    systemctl start docker
                    ;;
                *)
                    echo "❌ Unsupported OS: $OS"
                    echo "Please install Docker manually from: https://docs.docker.com/engine/install/"
                    exit 1
                    ;;
            esac

            echo "✅ Docker installed successfully"
            echo ""
            ;;
        *)
            echo "❌ Docker is required to run HostCraft."
            echo "Please install Docker from: https://docs.docker.com/engine/install/"
            exit 1
            ;;
    esac
fi

echo "✅ Docker is installed"
echo ""

# Ask about Docker Swarm initialization
echo "🐝 Docker Swarm Configuration"
echo "-----------------------------"
read -p "Initialize Docker Swarm? [yes]: " init_swarm
init_swarm=${init_swarm:-yes}

SWARM_MANAGER="false"
case $init_swarm in
    yes|y|Y|YES|"")
        init_swarm="yes"
        # Check if already part of a swarm
        if docker info 2>/dev/null | grep -q "Swarm: active"; then
            echo "✅ Docker Swarm is already initialized"
            SWARM_MANAGER="true"
        else
            echo "🔧 Initializing Docker Swarm..."
            if docker swarm init 2>/dev/null; then
                echo "✅ Docker Swarm initialized successfully"
                SWARM_MANAGER="true"
            else
                echo "⚠️  Failed to initialize swarm (may need to specify --advertise-addr)"
                echo "You can initialize it manually later with: docker swarm init"
            fi
        fi
        ;;
    *)
        init_swarm="no"
        echo "⏭️  Skipping Docker Swarm initialization"
        ;;
esac
echo ""

# Ask about Traefik setup if swarm is enabled
setup_traefik="no"
if [ "$init_swarm" = "yes" ]; then
    echo "🌐 Traefik Reverse Proxy Configuration"
    echo "--------------------------------------"
    echo "Traefik provides automatic SSL certificates (Let's Encrypt) and domain routing."
    read -p "Set up Traefik reverse proxy? [yes]: " setup_traefik
    setup_traefik=${setup_traefik:-yes}

    case $setup_traefik in
        yes|y|Y|YES|"")
            setup_traefik="yes"
            echo ""
            read -p "📧 Enter your email for Let's Encrypt notifications (required): " TRAEFIK_EMAIL

            if [ -z "$TRAEFIK_EMAIL" ]; then
                echo "⚠️  No email provided. Skipping Traefik setup."
                setup_traefik="no"
            else
                TRAEFIK_DASHBOARD_PORT="8080"
                echo "✅ Traefik will be configured with email: $TRAEFIK_EMAIL"
            fi
            ;;
        *)
            setup_traefik="no"
            echo "⏭️  Skipping Traefik setup"
            ;;
    esac
    echo ""
fi

# Ask about localhost server configuration
echo "🖥️  Server Configuration"
echo "------------------------"
echo "1) Configure localhost as a Docker host (recommended)"
echo "2) UI only (manage remote servers only)"
read -p "Select option [1]: " server_option
server_option=${server_option:-1}

case $server_option in
    1|"")
        CONFIGURE_LOCALHOST="true"
        echo "✅ Will configure localhost server"

        # If swarm is initialized, ask if localhost should be swarm manager
        if [ "$SWARM_MANAGER" = "true" ]; then
            echo ""
            read -p "Configure localhost as Swarm Manager? [yes]: " localhost_swarm
            localhost_swarm=${localhost_swarm:-yes}

            case $localhost_swarm in
                yes|y|Y|YES|"")
                    LOCALHOST_SWARM_MANAGER="true"
                    echo "✅ Localhost will be configured as Swarm Manager"
                    ;;
                *)
                    LOCALHOST_SWARM_MANAGER="false"
                    echo "✅ Localhost will be configured as standalone Docker host"
                    ;;
            esac
        else
            LOCALHOST_SWARM_MANAGER="false"
        fi
        ;;
    2)
        CONFIGURE_LOCALHOST="false"
        LOCALHOST_SWARM_MANAGER="false"
        echo "✅ UI only mode - no localhost auto-configuration"
        ;;
    *)
        # Default to option 1
        CONFIGURE_LOCALHOST="true"
        LOCALHOST_SWARM_MANAGER="false"
        echo "✅ Will configure localhost server (default)"
        ;;
esac
echo ""

# Check if Docker Compose is available
if ! docker compose version &> /dev/null; then
    echo "❌ Docker Compose is not available. Please install Docker Compose first."
    exit 1
fi

echo "✅ Docker and Docker Compose are installed"
echo ""

# Check if Swarm is active for deployment decision
SWARM_ACTIVE="false"
if docker info 2>/dev/null | grep -q "Swarm: active"; then
    SWARM_ACTIVE="true"
    echo "✅ Docker Swarm detected - will deploy as stack"
else
    echo "✅ Standalone Docker detected - will use compose"
fi
echo ""

# Stop and remove existing deployment (keeps data volumes)
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "🧹 Cleaning up existing stack..."
    docker stack rm hostcraft 2>/dev/null || true
    echo "   Waiting for services to stop..."
    sleep 10
else
    echo "🧹 Cleaning up existing containers..."
    docker compose down 2>/dev/null || true
fi
echo ""

# Rebuild and start the deployment
echo "🔨 Building Docker images..."
docker compose build --no-cache
echo ""

echo "🐳 Starting HostCraft..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "   Deploying as Docker Swarm stack..."

    # Create temporary compose file with substituted variables
    export POSTGRES_PASSWORD ENCRYPTION_KEY
    envsubst < docker-compose.yml > /tmp/docker-compose-substituted.yml

    if [ "$CONFIGURE_LOCALHOST" = "true" ]; then
        if [ "$LOCALHOST_SWARM_MANAGER" = "true" ]; then
            LOCALHOST_IS_SWARM_MANAGER=true docker stack deploy -c /tmp/docker-compose-substituted.yml hostcraft
        else
            docker stack deploy -c /tmp/docker-compose-substituted.yml hostcraft
        fi
    else
        SKIP_LOCALHOST_SEED=true docker stack deploy -c /tmp/docker-compose-substituted.yml hostcraft
    fi

    rm -f /tmp/docker-compose-substituted.yml

    echo "   ✅ Stack deployed successfully"
    echo "   📊 Check status: docker stack ps hostcraft"
    echo "   📋 View services: docker service ls"
else
    echo "   Deploying with Docker Compose..."

    export POSTGRES_PASSWORD ENCRYPTION_KEY
    envsubst < docker-compose.yml > /tmp/docker-compose-substituted.yml

    if [ "$CONFIGURE_LOCALHOST" = "true" ]; then
        if [ "$LOCALHOST_SWARM_MANAGER" = "true" ]; then
            LOCALHOST_IS_SWARM_MANAGER=true docker compose -f /tmp/docker-compose-substituted.yml up -d
        else
            docker compose -f /tmp/docker-compose-substituted.yml up -d
        fi
    else
        SKIP_LOCALHOST_SEED=true docker compose -f /tmp/docker-compose-substituted.yml up -d
    fi

    rm -f /tmp/docker-compose-substituted.yml

    echo "   ✅ Containers started successfully"
fi
echo ""

# Wait for PostgreSQL to be ready
echo "⏳ Waiting for PostgreSQL to be ready..."
sleep 10

if [ "$SWARM_ACTIVE" = "true" ]; then
    POSTGRES_CONTAINER=""
    for i in {1..30}; do
        POSTGRES_CONTAINER=$(docker ps --filter "label=com.docker.swarm.service.name=hostcraft_postgres" --format "{{.ID}}" | head -n 1)
        if [ -n "$POSTGRES_CONTAINER" ]; then
            break
        fi
        echo "   Waiting for postgres service to start... (attempt $i/30)"
        sleep 2
    done

    if [ -z "$POSTGRES_CONTAINER" ]; then
        echo "⚠️  Warning: Could not find postgres container"
    else
        until docker exec "$POSTGRES_CONTAINER" pg_isready -U hostcraft &>/dev/null; do
            echo "   PostgreSQL is not ready yet, waiting..."
            sleep 2
        done
        echo "✅ PostgreSQL is ready"
    fi
else
    until docker exec hostcraft-postgres-1 pg_isready -U hostcraft &>/dev/null; do
        echo "   PostgreSQL is not ready yet, waiting..."
        sleep 2
    done
    echo "✅ PostgreSQL is ready"
fi
echo ""

# Wait for migrations to complete
echo "⏳ Waiting for database migrations..."
sleep 5
echo "✅ Migrations completed"
echo ""

# Check if initial setup is needed
echo "👤 Checking initial setup status..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    POSTGRES_TARGET=$(docker ps --filter "label=com.docker.swarm.service.name=hostcraft_postgres" --format "{{.ID}}" | head -n 1)
else
    POSTGRES_TARGET="hostcraft-postgres-1"
fi

USER_COUNT=$(docker exec -i "$POSTGRES_TARGET" psql -U hostcraft -d hostcraft -t -c "SELECT COUNT(*) FROM \"Users\"" 2>/dev/null | tr -d ' ' || echo "0")

if [ "$USER_COUNT" = "0" ] || [ -z "$USER_COUNT" ]; then
    echo "   ℹ️  No admin users found - initial setup required"
    SETUP_REQUIRED="true"
else
    echo "   ✅ Admin user already exists - setup complete"
    SETUP_REQUIRED="false"
fi
echo ""

# Determine the postgres container name/id for both modes
if [ "$SWARM_ACTIVE" = "true" ]; then
    POSTGRES_TARGET=$(docker ps --filter "label=com.docker.swarm.service.name=hostcraft_postgres" --format "{{.ID}}" | head -n 1)
else
    POSTGRES_TARGET="hostcraft-postgres-1"
fi

# Apply PostgreSQL type fixes
echo "🔧 Applying PostgreSQL type fixes..."
cat > /tmp/fix_postgres_types.sql << 'SQL'
-- Fix boolean columns (convert integer to boolean)
ALTER TABLE "Regions" ALTER COLUMN "IsPrimary" TYPE boolean USING "IsPrimary"::integer::boolean;
ALTER TABLE "Users" ALTER COLUMN "IsAdmin" TYPE boolean USING "IsAdmin"::integer::boolean;
ALTER TABLE "Servers" ALTER COLUMN "IsSwarmManager" TYPE boolean USING "IsSwarmManager"::integer::boolean;
ALTER TABLE "Applications" ALTER COLUMN "AutoDeploy" TYPE boolean USING "AutoDeploy"::integer::boolean;
ALTER TABLE "Applications" ALTER COLUMN "AutoRestart" TYPE boolean USING "AutoRestart"::integer::boolean;
ALTER TABLE "Applications" ALTER COLUMN "AutoRollback" TYPE boolean USING "AutoRollback"::integer::boolean;
ALTER TABLE "EnvironmentVariables" ALTER COLUMN "IsSecret" TYPE boolean USING "IsSecret"::integer::boolean;
ALTER TABLE "Volumes" ALTER COLUMN "IsBackedUp" TYPE boolean USING "IsBackedUp"::integer::boolean;

-- Fix UUID columns (convert text to uuid)
ALTER TABLE "Applications" ALTER COLUMN "Uuid" TYPE uuid USING "Uuid"::uuid;
ALTER TABLE "Backups" ALTER COLUMN "Uuid" TYPE uuid USING "Uuid"::uuid;
ALTER TABLE "Deployments" ALTER COLUMN "Uuid" TYPE uuid USING "Uuid"::uuid;
ALTER TABLE "Projects" ALTER COLUMN "Uuid" TYPE uuid USING "Uuid"::uuid;
ALTER TABLE "Users" ALTER COLUMN "Uuid" TYPE uuid USING "Uuid"::uuid;
ALTER TABLE "Volumes" ALTER COLUMN "Uuid" TYPE uuid USING "Uuid"::uuid;

-- Fix DateTime columns (convert text to timestamp)
ALTER TABLE "PrivateKeys" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
ALTER TABLE "Projects" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
ALTER TABLE "Regions" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
ALTER TABLE "Users" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
ALTER TABLE "Users" ALTER COLUMN "LastLoginAt" TYPE timestamp USING CASE WHEN "LastLoginAt" IS NULL OR "LastLoginAt" = '' THEN NULL ELSE "LastLoginAt"::timestamp END;
ALTER TABLE "Servers" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
ALTER TABLE "Servers" ALTER COLUMN "LastHealthCheck" TYPE timestamp USING CASE WHEN "LastHealthCheck" IS NULL OR "LastHealthCheck" = '' THEN NULL ELSE "LastHealthCheck"::timestamp END;
ALTER TABLE "Servers" ALTER COLUMN "LastFailureAt" TYPE timestamp USING CASE WHEN "LastFailureAt" IS NULL OR "LastFailureAt" = '' THEN NULL ELSE "LastFailureAt"::timestamp END;
ALTER TABLE "Applications" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
ALTER TABLE "Applications" ALTER COLUMN "LastDeployedAt" TYPE timestamp USING CASE WHEN "LastDeployedAt" IS NULL OR "LastDeployedAt" = '' THEN NULL ELSE "LastDeployedAt"::timestamp END;
ALTER TABLE "Applications" ALTER COLUMN "LastHealthCheckAt" TYPE timestamp USING CASE WHEN "LastHealthCheckAt" IS NULL OR "LastHealthCheckAt" = '' THEN NULL ELSE "LastHealthCheckAt"::timestamp END;
ALTER TABLE "Deployments" ALTER COLUMN "StartedAt" TYPE timestamp USING "StartedAt"::timestamp;
ALTER TABLE "Deployments" ALTER COLUMN "FinishedAt" TYPE timestamp USING CASE WHEN "FinishedAt" IS NULL OR "FinishedAt" = '' THEN NULL ELSE "FinishedAt"::timestamp END;
ALTER TABLE "DeploymentLogs" ALTER COLUMN "Timestamp" TYPE timestamp USING "Timestamp"::timestamp;
ALTER TABLE "EnvironmentVariables" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
ALTER TABLE "Backups" ALTER COLUMN "StartedAt" TYPE timestamp USING "StartedAt"::timestamp;
ALTER TABLE "Backups" ALTER COLUMN "CompletedAt" TYPE timestamp USING CASE WHEN "CompletedAt" IS NULL OR "CompletedAt" = '' THEN NULL ELSE "CompletedAt"::timestamp END;
ALTER TABLE "Backups" ALTER COLUMN "ExpiresAt" TYPE timestamp USING CASE WHEN "ExpiresAt" IS NULL OR "ExpiresAt" = '' THEN NULL ELSE "ExpiresAt"::timestamp END;
ALTER TABLE "HealthChecks" ALTER COLUMN "CheckedAt" TYPE timestamp USING "CheckedAt"::timestamp;
ALTER TABLE "Volumes" ALTER COLUMN "CreatedAt" TYPE timestamp USING "CreatedAt"::timestamp;
SQL

docker exec -i "$POSTGRES_TARGET" psql -U hostcraft -d hostcraft < /tmp/fix_postgres_types.sql > /dev/null 2>&1
rm /tmp/fix_postgres_types.sql
echo "✅ PostgreSQL type fixes applied"
echo ""

# Restart API to ensure everything is picked up
echo "🔄 Restarting API..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    docker service update --detach --force hostcraft_api > /dev/null 2>&1
    echo "   ✅ API service restarted"
    echo "   ⏳ Waiting for API to become healthy..."
    sleep 5
else
    docker restart hostcraft-api-1 > /dev/null 2>&1 || docker restart api-1 > /dev/null
    echo "   ✅ API container restarted"
    sleep 3
fi
echo ""

# Setup Traefik if requested
domain_configured=false
if [ "$setup_traefik" = "yes" ] && [ -n "$TRAEFIK_EMAIL" ]; then
    echo "🌐 Setting up Traefik reverse proxy..."
    echo ""

    # Create Traefik network
    echo "📦 Creating traefik-public network..."
    docker network create --driver=overlay traefik-public 2>/dev/null || echo "   Network already exists"

    # Create Traefik compose file
    TRAEFIK_COMPOSE="/tmp/traefik-compose.yml"

    cat > "$TRAEFIK_COMPOSE" << 'EOF'
version: '3.8'

services:
  traefik:
    image: traefik:v2.11
    command:
      - --providers.docker=true
      - --providers.docker.swarmMode=true
      - --providers.docker.exposedByDefault=false
      - --providers.docker.network=traefik-public
      - --entrypoints.web.address=:80
      - --entrypoints.websecure.address=:443
      - --entrypoints.web.http.redirections.entrypoint.to=websecure
      - --entrypoints.web.http.redirections.entrypoint.scheme=https
      - --api.dashboard=true
      - --certificatesresolvers.letsencrypt.acme.email=TRAEFIK_EMAIL_PLACEHOLDER
      - --certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json
      - --certificatesresolvers.letsencrypt.acme.httpchallenge=true
      - --certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web
      - --log.level=INFO
      - --accesslog=true
    ports:
      - "80:80"
      - "443:443"
      - "8080:8080"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - traefik-certificates:/letsencrypt
    networks:
      - traefik-public
    deploy:
      mode: replicated
      replicas: 1
      placement:
        constraints:
          - node.role == manager
      restart_policy:
        condition: any
        delay: 5s
        max_attempts: 3
      labels:
        - "traefik.enable=true"

volumes:
  traefik-certificates:
    driver: local

networks:
  traefik-public:
    external: true
EOF

    sed -i "s/TRAEFIK_EMAIL_PLACEHOLDER/${TRAEFIK_EMAIL}/g" "$TRAEFIK_COMPOSE"

    echo "Deploying Traefik..."
    docker stack deploy -c "$TRAEFIK_COMPOSE" traefik

    echo ""
    echo "⏳ Waiting for Traefik to start..."
    sleep 5

    if docker service ls | grep -q "traefik_traefik"; then
        echo "✅ Traefik deployed successfully!"

        echo "🔗 Connecting HostCraft services to Traefik network..."
        docker service update --network-add traefik-public hostcraft_web 2>/dev/null || true
        echo "✅ HostCraft web service connected to Traefik"
        echo ""

        # Ask if user wants to configure domain now
        echo "🌐 Domain Configuration"
        echo "-----------------------"
        echo "You can configure a domain now or later in the Web UI."
        read -p "Configure domain now? [yes]: " configure_domain
        configure_domain=${configure_domain:-yes}

        case $configure_domain in
            yes|y|Y|YES|"")
                echo ""
                read -p "Enter your domain (e.g., hostcraft.example.com): " hostcraft_domain

                if [ -n "$hostcraft_domain" ]; then
                    read -p "Enable HTTPS with Let's Encrypt? [yes]: " enable_https
                    enable_https=${enable_https:-yes}

                    echo ""
                    echo "🔧 Applying Traefik configuration to HostCraft..."

                    HOST_RULE=$(printf "Host(\`%s\`)" "$hostcraft_domain")

                    case $enable_https in
                        yes|y|Y|YES|"")
                            docker service update \
                                --label-add "traefik.enable=true" \
                                --label-add "traefik.docker.network=traefik-public" \
                                --label-add "traefik.http.routers.hostcraft-web.rule=$HOST_RULE" \
                                --label-add "traefik.http.routers.hostcraft-web.entrypoints=websecure" \
                                --label-add "traefik.http.routers.hostcraft-web.tls=true" \
                                --label-add "traefik.http.routers.hostcraft-web.tls.certresolver=letsencrypt" \
                                --label-add "traefik.http.routers.hostcraft-web.service=hostcraft-web" \
                                --label-add "traefik.http.services.hostcraft-web.loadbalancer.server.port=8080" \
                                --label-add "traefik.http.routers.hostcraft-web-http.rule=$HOST_RULE" \
                                --label-add "traefik.http.routers.hostcraft-web-http.entrypoints=web" \
                                --label-add "traefik.http.routers.hostcraft-web-http.middlewares=redirect-to-https" \
                                --label-add "traefik.http.middlewares.redirect-to-https.redirectscheme.scheme=https" \
                                --label-add "traefik.http.middlewares.redirect-to-https.redirectscheme.permanent=true" \
                                --force hostcraft_web
                            ;;
                        *)
                            docker service update \
                                --label-add "traefik.enable=true" \
                                --label-add "traefik.docker.network=traefik-public" \
                                --label-add "traefik.http.routers.hostcraft-web.rule=$HOST_RULE" \
                                --label-add "traefik.http.routers.hostcraft-web.entrypoints=web" \
                                --label-add "traefik.http.routers.hostcraft-web.service=hostcraft-web" \
                                --label-add "traefik.http.services.hostcraft-web.loadbalancer.server.port=8080" \
                                --force hostcraft_web
                            ;;
                    esac

                    if [ $? -eq 0 ]; then
                        echo "✅ Domain configured: $hostcraft_domain"
                        domain_configured=true
                    else
                        echo "⚠️  Failed to apply domain configuration. Configure it later in the Web UI."
                    fi
                else
                    echo "⚠️  No domain entered. Configure it later in the Web UI."
                fi
                ;;
            *)
                echo "⏭️  Skipping domain configuration. Configure it later in Settings -> Domain & SSL"
                ;;
        esac
        echo ""
    else
        echo "⚠️  Traefik deployment may have issues. Check logs: docker service logs traefik_traefik"
    fi

    rm -f "$TRAEFIK_COMPOSE"
fi

# Check if everything is running
echo "📊 Deployment Status:"
if [ "$SWARM_ACTIVE" = "true" ]; then
    echo ""
    echo "Services:"
    docker service ls --filter label=hostcraft.managed=true
    echo ""
    echo "Tasks:"
    docker stack ps hostcraft --no-trunc 2>/dev/null || docker stack ps hostcraft
else
    docker compose ps
fi
echo ""

echo "✅ Installation completed successfully!"
echo ""
echo "📍 Access your HostCraft instance:"
if [ "$setup_traefik" = "yes" ] && [ "$domain_configured" = "true" ]; then
    echo "   🌐 Web UI: https://$hostcraft_domain (via Traefik)"
    echo "   📊 Direct access: http://$(hostname -I | awk '{print $1}'):5000"
    echo "   🔧 API: http://$(hostname -I | awk '{print $1}'):5100"
    echo "   📈 Traefik Dashboard: http://$(hostname -I | awk '{print $1}'):8080"
else
    echo "   🌐 Web UI: http://$(hostname -I | awk '{print $1}'):5000"
    echo "   🔧 API:    http://$(hostname -I | awk '{print $1}'):5100"
    if [ "$setup_traefik" = "yes" ]; then
        echo "   📈 Traefik Dashboard: http://$(hostname -I | awk '{print $1}'):8080"
        echo ""
        echo "   💡 To enable domain access with HTTPS:"
        echo "      Go to Settings -> HostCraft Domain & SSL"
    fi
fi
echo ""

# Show setup instructions if needed
if [ "$SETUP_REQUIRED" = "true" ]; then
    echo "============================================================="
    echo "🔐 FIRST TIME SETUP REQUIRED"
    echo "============================================================="
    echo ""
    echo "   1. Open the Web UI URL above"
    echo "   2. You will be redirected to the Setup page"
    echo "   3. Create your admin account with:"
    echo "      - Your name"
    echo "      - Your email address"
    echo "      - A secure password (8+ chars, upper, lower, number)"
    echo ""
    echo "============================================================="
    echo ""
fi

if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "🐝 Deployment Mode: Docker Swarm Stack"
    echo "   📊 Monitor: docker service ls"
    echo "   📋 Tasks: docker stack ps hostcraft"
    echo "   📝 Logs: docker service logs hostcraft_web"
    if [ "$CONFIGURE_LOCALHOST" = "true" ] && [ "$LOCALHOST_SWARM_MANAGER" = "true" ]; then
        echo ""
        echo "   📋 Worker join token: docker swarm join-token worker"
        echo "   📋 Manager join token: docker swarm join-token manager"
    fi
else
    echo "🐳 Deployment Mode: Docker Compose (Standalone)"
    echo "   📊 Monitor: docker compose ps"
    echo "   📝 Logs: docker compose logs -f"
    echo "   🔄 Restart: docker compose restart"
fi
echo ""
echo "🎉 HostCraft is ready to use!"
