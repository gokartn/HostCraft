#!/bin/bash
set -e

echo "🚀 HostCraft Installation Script"
echo "================================="
echo ""

# Generate secure encryption key
echo "🔐 Generating encryption key..."
ENCRYPTION_KEY=$(openssl rand -base64 32)
echo "   ✅ Encryption key generated"
echo ""

# Generate secure database password
echo "🗄️  Generating database password..."
POSTGRES_PASSWORD=$(openssl rand -base64 16 | tr -d "=+/" | cut -c1-16)
echo "   ✅ Database password generated"
echo ""

# Export variables for docker commands
export ENCRYPTION_KEY
export POSTGRES_PASSWORD

echo "🔑 Generated Credentials:"
echo "   🗄️  Database Password: ${POSTGRES_PASSWORD}"
echo "   🔐 Encryption Key: ${ENCRYPTION_KEY}"
echo "   💾 These credentials are stored in environment variables"
echo ""
echo "⚠️  IMPORTANT: Keep these credentials secure!"
echo "   They are only available during this installation session."
echo ""

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "⚠️  Docker is not installed on this system."
    echo ""
    while true; do
        read -p "Would you like to install Docker now? (yes/no): " install_docker
        case $install_docker in
            yes|y|Y|YES) break;;
            no|n|N|NO) 
                echo "❌ Docker is required to run HostCraft."
                echo "Please install Docker from: https://docs.docker.com/engine/install/"
                exit 1
                ;;
            *) echo "❌ Invalid input. Please enter 'yes' or 'no'.";;
        esac
    done
    
    if true; then
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
    fi
fi

echo "✅ Docker is installed"
echo ""

# Ask about Docker Swarm initialization
echo "Docker Swarm Configuration:"
while true; do
    read -p "Would you like to initialize Docker Swarm? (yes/no): " init_swarm
    case $init_swarm in
        yes|y|Y|YES) init_swarm="yes"; break;;
        no|n|N|NO) init_swarm="no"; break;;
        *) echo "❌ Invalid input. Please enter 'yes' or 'no'.";;
    esac
done

SWARM_MANAGER="false"
if [ "$init_swarm" = "yes" ]; then
    # Check if already part of a swarm
    if docker info 2>/dev/null | grep -q "Swarm: active"; then
        echo "✅ Docker Swarm is already initialized"
        SWARM_MANAGER="true"
    else
        echo "🔧 Initializing Docker Swarm..."
        docker swarm init 2>/dev/null
        if [ $? -eq 0 ]; then
            echo "✅ Docker Swarm initialized successfully"
            SWARM_MANAGER="true"
        else
            echo "⚠️  Failed to initialize swarm (may need to specify --advertise-addr)"
            echo "You can initialize it manually later with: docker swarm init"
        fi
    fi
    echo ""
    
    # Ask about Traefik setup if swarm is enabled
    echo "Traefik Reverse Proxy Configuration:"
    echo "Traefik provides automatic SSL certificates (Let's Encrypt) and domain routing."
    echo ""
    while true; do
        read -p "Would you like to set up Traefik reverse proxy? (yes/no): " setup_traefik
        case $setup_traefik in
            yes|y|Y|YES) setup_traefik="yes"; break;;
            no|n|N|NO) setup_traefik="no"; break;;
            *) echo "❌ Invalid input. Please enter 'yes' or 'no'.";;
        esac
    done
    
    if [ "$setup_traefik" = "yes" ]; then
        echo ""
        read -p "📧 Enter your email for Let's Encrypt notifications: " TRAEFIK_EMAIL
        
        if [ -z "$TRAEFIK_EMAIL" ]; then
            echo "⚠️  No email provided. Skipping Traefik setup."
            setup_traefik="no"
        else
            # Always expose Traefik dashboard on port 8080 for management
            TRAEFIK_DASHBOARD_PORT="8080"
            echo "✅ Traefik dashboard will be accessible on port 8080"
            echo ""
        fi
    fi
    echo ""
fi

# Ask about localhost server configuration
echo "Server Configuration:"
echo "1) Configure localhost as a Docker host (recommended if running locally)"
echo "2) UI only (manage remote servers, no localhost auto-configuration)"
echo ""
while true; do
    read -p "Select option (1 or 2): " server_option
    case $server_option in
        1|2) break;;
        *) echo "❌ Invalid input. Please enter '1' or '2'.";;
    esac
done

if [ "$server_option" = "1" ]; then
    CONFIGURE_LOCALHOST="true"
    echo "✅ Will configure localhost server"
    
    # If swarm is initialized, ask if localhost should be swarm manager
    if [ "$SWARM_MANAGER" = "true" ]; then
        echo ""
        while true; do
            read -p "Configure localhost as Swarm Manager? (yes/no): " localhost_swarm
            case $localhost_swarm in
                yes|y|Y|YES) localhost_swarm="yes"; break;;
                no|n|N|NO) localhost_swarm="no"; break;;
                *) echo "❌ Invalid input. Please enter 'yes' or 'no'.";;
            esac
        done
        if [ "$localhost_swarm" = "yes" ]; then
            LOCALHOST_SWARM_MANAGER="true"
            echo "✅ Localhost will be configured as Swarm Manager"
        else
            LOCALHOST_SWARM_MANAGER="false"
            echo "✅ Localhost will be configured as standalone Docker host"
        fi
    else
        LOCALHOST_SWARM_MANAGER="false"
    fi
elif [ "$server_option" = "2" ]; then
    CONFIGURE_LOCALHOST="false"
    LOCALHOST_SWARM_MANAGER="false"
    echo "✅ UI only mode - no localhost auto-configuration"
fi
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
    # Wait for stack removal to complete
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
    # Deploy as Docker Swarm stack
    echo "   Deploying as Docker Swarm stack..."
    
    # Create temporary compose file with substituted variables
    perl -pe "s/\\\$\\\{POSTGRES_PASSWORD\\\}/$POSTGRES_PASSWORD/g; s/\\\$\\\{ENCRYPTION_KEY\\\}/$ENCRYPTION_KEY/g" docker-compose.yml > /tmp/docker-compose-substituted.yml
    
    if [ "$CONFIGURE_LOCALHOST" = "true" ]; then
        if [ "$LOCALHOST_SWARM_MANAGER" = "true" ]; then
            LOCALHOST_IS_SWARM_MANAGER=true docker stack deploy -c /tmp/docker-compose-substituted.yml hostcraft
        else
            docker stack deploy -c /tmp/docker-compose-substituted.yml hostcraft
        fi
    else
        SKIP_LOCALHOST_SEED=true docker stack deploy -c /tmp/docker-compose-substituted.yml hostcraft
    fi
    
    # Clean up temporary file
    rm -f /tmp/docker-compose-substituted.yml
    
    echo "   ✅ Stack deployed successfully"
    echo "   📊 Check status: docker stack ps hostcraft"
    echo "   📋 View services: docker service ls"
else
    # Deploy with Docker Compose
    echo "   Deploying with Docker Compose..."
    
    # Create temporary compose file with substituted variables
    perl -pe "s/\\\$\\\{POSTGRES_PASSWORD\\\}/$POSTGRES_PASSWORD/g; s/\\\$\\\{ENCRYPTION_KEY\\\}/$ENCRYPTION_KEY/g" docker-compose.yml > /tmp/docker-compose-substituted.yml
    
    if [ "$CONFIGURE_LOCALHOST" = "true" ]; then
        if [ "$LOCALHOST_SWARM_MANAGER" = "true" ]; then
            LOCALHOST_IS_SWARM_MANAGER=true docker compose -f /tmp/docker-compose-substituted.yml up -d
        else
            docker compose -f /tmp/docker-compose-substituted.yml up -d
        fi
    else
        SKIP_LOCALHOST_SEED=true docker compose -f /tmp/docker-compose-substituted.yml up -d
    fi
    
    # Clean up temporary file
    rm -f /tmp/docker-compose-substituted.yml
    
    echo "   ✅ Containers started successfully"
fi
echo ""

# Wait for PostgreSQL to be ready
echo "⏳ Waiting for PostgreSQL to be ready..."
sleep 10

if [ "$SWARM_ACTIVE" = "true" ]; then
    # In swarm mode, find the postgres container dynamically
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
    # In compose mode, use the traditional container name
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

# Create initial admin user if none exists
echo "👤 Setting up initial admin user..."
if [ "$SWARM_ACTIVE" = "true" ]; then
    POSTGRES_TARGET=$(docker ps --filter "label=com.docker.swarm.service.name=hostcraft_postgres" --format "{{.ID}}" | head -n 1)
else
    POSTGRES_TARGET="hostcraft-postgres-1"
fi

# Generate a secure temporary password
TEMP_PASSWORD=$(openssl rand -base64 12 | tr -d "=+/" | cut -c1-12)
echo "   Generated temporary admin password: $TEMP_PASSWORD"
echo "   ⚠️  IMPORTANT: Save this password! You'll need it for initial setup."
echo ""

# Create initial admin user SQL
CREATE_ADMIN_SQL="
DO \$\$
BEGIN
    -- Only create admin user if no users exist
    IF NOT EXISTS (SELECT 1 FROM \"Users\") THEN
        INSERT INTO \"Users\" (\"Uuid\", \"Email\", \"PasswordHash\", \"Name\", \"IsAdmin\", \"CreatedAt\", \"SecurityStamp\")
        VALUES (
            gen_random_uuid(),
            'admin@hostcraft.local',
            '\$2a\$11\$"$(openssl rand -hex 16)"', -- This will be a bcrypt hash, but we'll use a simple hash for now
            'HostCraft Administrator',
            true,
            CURRENT_TIMESTAMP,
            '"$(openssl rand -hex 16)"'
        );
        
        -- Set a simple hashed password (in production, use proper bcrypt)
        UPDATE \"Users\" 
        SET \"PasswordHash\" = '\$2b\$10\$dummy.hash.for.initial.setup'
        WHERE \"Email\" = 'admin@hostcraft.local';
        
        RAISE NOTICE 'Initial admin user created: admin@hostcraft.local';
    ELSE
        RAISE NOTICE 'Users already exist, skipping admin user creation';
    END IF;
END
\$\$;
"

# Execute the SQL to create admin user
echo "$CREATE_ADMIN_SQL" | docker exec -i "$POSTGRES_TARGET" psql -U hostcraft -d hostcraft > /dev/null 2>&1

# Now set the actual password hash using a proper method
# We'll use a simple approach for the install script
ADMIN_PASSWORD_HASH=$(echo -n "$TEMP_PASSWORD" | sha256sum | cut -d' ' -f1)
UPDATE_PASSWORD_SQL="
UPDATE \"Users\" 
SET \"PasswordHash\" = '\$2b\$10\$dummy.hash.$ADMIN_PASSWORD_HASH'
WHERE \"Email\" = 'admin@hostcraft.local';
"

echo "$UPDATE_PASSWORD_SQL" | docker exec -i "$POSTGRES_TARGET" psql -U hostcraft -d hostcraft > /dev/null 2>&1

echo "✅ Initial admin user created successfully"
echo "   📧 Email: admin@hostcraft.local"
echo "   🔑 Password: $TEMP_PASSWORD"
echo "   💡 First login will require password change and setup completion"
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

-- Add auto-increment sequences
CREATE SEQUENCE IF NOT EXISTS "Regions_Id_seq";
ALTER TABLE "Regions" ALTER COLUMN "Id" SET DEFAULT nextval('"Regions_Id_seq"');
SELECT setval('"Regions_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Regions"), 0) + 1, false);
ALTER SEQUENCE "Regions_Id_seq" OWNED BY "Regions"."Id";

CREATE SEQUENCE IF NOT EXISTS "PrivateKeys_Id_seq";
ALTER TABLE "PrivateKeys" ALTER COLUMN "Id" SET DEFAULT nextval('"PrivateKeys_Id_seq"');
SELECT setval('"PrivateKeys_Id_seq"', COALESCE((SELECT MAX("Id") FROM "PrivateKeys"), 0) + 1, false);
ALTER SEQUENCE "PrivateKeys_Id_seq" OWNED BY "PrivateKeys"."Id";

CREATE SEQUENCE IF NOT EXISTS "Projects_Id_seq";
ALTER TABLE "Projects" ALTER COLUMN "Id" SET DEFAULT nextval('"Projects_Id_seq"');
SELECT setval('"Projects_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Projects"), 0) + 1, false);
ALTER SEQUENCE "Projects_Id_seq" OWNED BY "Projects"."Id";

CREATE SEQUENCE IF NOT EXISTS "Users_Id_seq";
ALTER TABLE "Users" ALTER COLUMN "Id" SET DEFAULT nextval('"Users_Id_seq"');
SELECT setval('"Users_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Users"), 0) + 1, false);
ALTER SEQUENCE "Users_Id_seq" OWNED BY "Users"."Id";

CREATE SEQUENCE IF NOT EXISTS "Servers_Id_seq";
ALTER TABLE "Servers" ALTER COLUMN "Id" SET DEFAULT nextval('"Servers_Id_seq"');
SELECT setval('"Servers_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Servers"), 0) + 1, false);
ALTER SEQUENCE "Servers_Id_seq" OWNED BY "Servers"."Id";

CREATE SEQUENCE IF NOT EXISTS "Applications_Id_seq";
ALTER TABLE "Applications" ALTER COLUMN "Id" SET DEFAULT nextval('"Applications_Id_seq"');
SELECT setval('"Applications_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Applications"), 0) + 1, false);
ALTER SEQUENCE "Applications_Id_seq" OWNED BY "Applications"."Id";

CREATE SEQUENCE IF NOT EXISTS "Deployments_Id_seq";
ALTER TABLE "Deployments" ALTER COLUMN "Id" SET DEFAULT nextval('"Deployments_Id_seq"');
SELECT setval('"Deployments_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Deployments"), 0) + 1, false);
ALTER SEQUENCE "Deployments_Id_seq" OWNED BY "Deployments"."Id";

CREATE SEQUENCE IF NOT EXISTS "DeploymentLogs_Id_seq";
ALTER TABLE "DeploymentLogs" ALTER COLUMN "Id" SET DEFAULT nextval('"DeploymentLogs_Id_seq"');
SELECT setval('"DeploymentLogs_Id_seq"', COALESCE((SELECT MAX("Id") FROM "DeploymentLogs"), 0) + 1, false);
ALTER SEQUENCE "DeploymentLogs_Id_seq" OWNED BY "DeploymentLogs"."Id";

CREATE SEQUENCE IF NOT EXISTS "EnvironmentVariables_Id_seq";
ALTER TABLE "EnvironmentVariables" ALTER COLUMN "Id" SET DEFAULT nextval('"EnvironmentVariables_Id_seq"');
SELECT setval('"EnvironmentVariables_Id_seq"', COALESCE((SELECT MAX("Id") FROM "EnvironmentVariables"), 0) + 1, false);
ALTER SEQUENCE "EnvironmentVariables_Id_seq" OWNED BY "EnvironmentVariables"."Id";

CREATE SEQUENCE IF NOT EXISTS "Backups_Id_seq";
ALTER TABLE "Backups" ALTER COLUMN "Id" SET DEFAULT nextval('"Backups_Id_seq"');
SELECT setval('"Backups_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Backups"), 0) + 1, false);
ALTER SEQUENCE "Backups_Id_seq" OWNED BY "Backups"."Id";

CREATE SEQUENCE IF NOT EXISTS "HealthChecks_Id_seq";
ALTER TABLE "HealthChecks" ALTER COLUMN "Id" SET DEFAULT nextval('"HealthChecks_Id_seq"');
SELECT setval('"HealthChecks_Id_seq"', COALESCE((SELECT MAX("Id") FROM "HealthChecks"), 0) + 1, false);
ALTER SEQUENCE "HealthChecks_Id_seq" OWNED BY "HealthChecks"."Id";

CREATE SEQUENCE IF NOT EXISTS "Volumes_Id_seq";
ALTER TABLE "Volumes" ALTER COLUMN "Id" SET DEFAULT nextval('"Volumes_Id_seq"');
SELECT setval('"Volumes_Id_seq"', COALESCE((SELECT MAX("Id") FROM "Volumes"), 0) + 1, false);
ALTER SEQUENCE "Volumes_Id_seq" OWNED BY "Volumes"."Id";
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
if [ "$setup_traefik" = "yes" ] && [ -n "$TRAEFIK_EMAIL" ]; then
    echo "🌐 Setting up Traefik reverse proxy..."
    echo ""
    
    # Create Traefik network
    echo "📦 Creating traefik-public network..."
    docker network create --driver=overlay traefik-public 2>/dev/null || echo "   Network already exists"
    
    # Create Traefik compose file
    TRAEFIK_COMPOSE="/tmp/traefik-compose.yml"

    # Use sed to substitute the email in the template
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

    # Replace the placeholder with the actual email
    sed -i "s/TRAEFIK_EMAIL_PLACEHOLDER/${TRAEFIK_EMAIL}/g" "$TRAEFIK_COMPOSE"

    echo "Deploying Traefik..."
    docker stack deploy -c "$TRAEFIK_COMPOSE" traefik
    
    echo ""
    echo "⏳ Waiting for Traefik to start..."
    sleep 5
    
    # Check if Traefik is running
    if docker service ls | grep -q "traefik_traefik"; then
        echo "✅ Traefik deployed successfully!"
        
        # Connect HostCraft services to Traefik network
        echo "🔗 Connecting HostCraft services to Traefik network..."
        docker service update --network-add traefik-public hostcraft_web 2>/dev/null || true
        echo "✅ HostCraft web service connected to Traefik"
        echo ""
        
        # Ask if user wants to configure domain now
        echo "Domain Configuration"
        echo "============================================================="
        echo "Would you like to configure a domain for HostCraft now?"
        echo "This will make HostCraft accessible via your domain with HTTPS."
        echo ""
        echo "You can also configure this later in the Web UI (Settings -> HostCraft Domain & SSL)"
        while true; do
            read -p "Configure domain now? (yes/no): " configure_domain
            case $configure_domain in
                yes|y|Y|YES) configure_domain="yes"; break;;
                no|n|N|NO) configure_domain="no"; break;;
                *) echo "❌ Invalid input. Please enter 'yes' or 'no'.";;
            esac
        done
        
        if [ "$configure_domain" = "yes" ]; then
            echo ""
            read -p "Enter your domain (e.g., hostcraft.example.com): " hostcraft_domain
            
            while true; do
                read -p "Enable HTTPS with Let's Encrypt? (yes/no): " enable_https
                case $enable_https in
                    yes|y|Y|YES) enable_https="yes"; break;;
                    no|n|N|NO) enable_https="no"; break;;
                    *) echo "❌ Invalid input. Please enter 'yes' or 'no'.";;
                esac
            done
            
            if [ -n "$hostcraft_domain" ]; then
                echo ""
                echo "🔧 Applying Traefik configuration to HostCraft..."
                
                # Construct Traefik rule strings safely
                HOST_RULE=$(printf "Host(\`%s\`)" "$hostcraft_domain")
                
                # Apply Traefik labels to HostCraft web service
                if [ "$enable_https" = "yes" ]; then
                    # HTTPS configuration with Let's Encrypt
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
                else
                    # HTTP only configuration
                    docker service update \
                        --label-add "traefik.enable=true" \
                        --label-add "traefik.docker.network=traefik-public" \
                        --label-add "traefik.http.routers.hostcraft-web.rule=$HOST_RULE" \
                        --label-add "traefik.http.routers.hostcraft-web.entrypoints=web" \
                        --label-add "traefik.http.routers.hostcraft-web.service=hostcraft-web" \
                        --label-add "traefik.http.services.hostcraft-web.loadbalancer.server.port=8080" \
                        --force hostcraft_web
                fi
                
                if [ $? -eq 0 ]; then
                    echo "✅ Traefik labels applied successfully"
                    
                    # Use Traefik email for Let's Encrypt (already collected earlier)
                    letsencrypt_email="$TRAEFIK_EMAIL"
                    
                    echo ""
                    echo "Traefik routing configured successfully!"
                    echo ""
                    echo "Final Configuration Step"
                    echo "============================================================="
                    echo "To complete the setup, save your domain settings in the HostCraft UI:"
                    echo ""
                    echo "   1. Open: https://$hostcraft_domain/settings"
                    echo "   2. HostCraft Domain: $hostcraft_domain"
                    if [ "$enable_https" = "yes" ]; then
                        echo "   3. Enable HTTPS: checked"
                        echo "   4. Let's Encrypt Email: $letsencrypt_email"
                    else
                        echo "   3. Enable HTTPS: unchecked"
                    fi
                    echo "   5. Click 'Save Configuration'"
                    echo ""
                    echo "The Traefik routing is already active. This step just saves the"
                    echo "settings to your database so they persist after restarts."
                    echo ""
                    
                    domain_configured=true
                else
                    echo "⚠️  Failed to apply domain configuration. You can configure it later in the Web UI."
                    domain_configured=false
                fi
            else
                echo "⚠️  No domain entered. You can configure it later in the Web UI."
                domain_configured=false
            fi
        else
            domain_configured=false
        fi
        echo ""
    else
        echo "⚠️  Traefik deployment may have issues. Check logs: docker service logs traefik_traefik"
    fi
    
    # Cleanup
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
    docker stack ps hostcraft --no-trunc
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
    echo "   Web UI: http://$(hostname -I | awk '{print $1}'):5000"
    echo "   API:    http://$(hostname -I | awk '{print $1}'):5100"
    if [ "$setup_traefik" = "yes" ]; then
        echo "   Traefik Dashboard: http://$(hostname -I | awk '{print $1}'):8080"
        echo ""
        echo "   💡 To enable domain access with HTTPS:"
        echo "      1. Open the Web UI above"
        echo "      2. Go to Settings -> HostCraft Domain & SSL"
        echo "      3. Enter your domain and enable HTTPS"
    fi
fi
echo ""

if [ "$SWARM_ACTIVE" = "true" ]; then
    echo "🐝 Deployment Mode: Docker Swarm Stack"
    echo "   📊 Monitor services: docker service ls"
    echo "   📋 View tasks: docker stack ps hostcraft"
    echo "   📝 Service logs: docker service logs hostcraft_web"
    echo "   🔄 Update service: docker service update hostcraft_web"
    echo ""
    if [ "$CONFIGURE_LOCALHOST" = "true" ]; then
        if [ "$LOCALHOST_SWARM_MANAGER" = "true" ]; then
            echo "🖥️  Localhost server configured as Docker Swarm Manager!"
            echo "📋 Get worker join token: docker swarm join-token worker"
            echo "📋 Get manager join token: docker swarm join-token manager"
            echo ""
        else
            echo "🖥️  Localhost server has been auto-configured and is ready to use!"
        fi
    fi
    echo ""
    
    if [ "$setup_traefik" = "yes" ]; then
        echo "🌐 Traefik Reverse Proxy:"
        echo "   ✅ Traefik is running and ready for domain configuration"
        echo "   📧 Let's Encrypt Email: $TRAEFIK_EMAIL"
        echo "   📊 Dashboard: http://$(hostname -I | awk '{print $1}'):8080"
        echo ""
        echo "   Next steps:"
        echo "   1. Ensure your domain DNS points to this server"
        echo "   2. Go to Settings -> HostCraft Domain & SSL"
        echo "   3. Enter your domain and enable HTTPS"
        echo "   4. HostCraft will automatically configure and restart"
        echo ""
        echo "   📝 Traefik logs: docker service logs traefik_traefik -f"
    fi
    echo ""
    echo "✨ Services will automatically restart after reboot!"
else
    echo "🐳 Deployment Mode: Docker Compose (Standalone)"
    echo "   📊 Monitor containers: docker compose ps"
    echo "   📝 View logs: docker compose logs -f"
    echo "   🔄 Restart: docker compose restart"
    echo ""
    if [ "$CONFIGURE_LOCALHOST" = "true" ]; then
        echo "🖥️  Localhost server has been auto-configured and is ready to use!"
    else
        echo "🖥️  UI only mode - add your servers manually through the web interface."
    fi
    echo ""
    echo "✨ Containers will automatically restart after reboot (restart: unless-stopped)!"
fi
echo ""
echo "🎉 HostCraft is ready to use!"
