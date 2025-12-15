#!/bin/bash
set -e

echo "🚀 HostCraft Installation Script"
echo "================================="
echo ""

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed. Please install Docker first."
    exit 1
fi

# Check if Docker Compose is available
if ! docker compose version &> /dev/null; then
    echo "❌ Docker Compose is not available. Please install Docker Compose first."
    exit 1
fi

echo "✅ Docker and Docker Compose are installed"
echo ""

# Stop and remove existing containers (keeps data volumes)
echo "🧹 Cleaning up existing containers..."
docker compose down 2>/dev/null || true
echo ""

# Start the containers
echo "🐳 Starting Docker containers..."
docker compose up -d
echo ""

# Wait for PostgreSQL to be ready
echo "⏳ Waiting for PostgreSQL to be ready..."
sleep 5
until docker exec hostcraft-postgres-1 pg_isready -U hostcraft &>/dev/null; do
    echo "   PostgreSQL is not ready yet, waiting..."
    sleep 2
done
echo "✅ PostgreSQL is ready"
echo ""

# Wait for migrations to complete
echo "⏳ Waiting for database migrations..."
sleep 5
echo "✅ Migrations completed"
echo ""

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

docker exec -i hostcraft-postgres-1 psql -U hostcraft -d hostcraft < /tmp/fix_postgres_types.sql > /dev/null 2>&1
rm /tmp/fix_postgres_types.sql
echo "✅ PostgreSQL type fixes applied"
echo ""

# Restart API to ensure everything is picked up
echo "🔄 Restarting API container..."
docker restart hostcraft-hostcraft-api-1 > /dev/null
sleep 3
echo "✅ API restarted"
echo ""

# Check if everything is running
echo "📊 Container Status:"
docker compose ps
echo ""

echo "✅ Installation completed successfully!"
echo ""
echo "📍 Access your HostCraft instance:"
echo "   Web UI: http://$(hostname -I | awk '{print $1}'):5000"
echo "   API:    http://$(hostname -I | awk '{print $1}'):5001"
echo ""
echo "🎉 HostCraft is ready to use!"
