# HostCraft Installation Guide

## Quick Start (Recommended)

### Prerequisites
- Docker and Docker Compose installed
- Linux server with Docker socket mounted (`/var/run/docker.sock`)

### One-Command Installation

```bash
./install.sh
```

That's it! The script will:
1. ✅ Check for Docker and Docker Compose
2. 🧹 Clean up any existing containers
3. 🐳 Start all containers
4. ⏳ Wait for PostgreSQL to be ready
5. 🔧 Apply database schema fixes
6. 🔄 Restart services
7. ✅ Verify everything is running

After installation completes, access:
- **Web UI**: `http://your-server-ip:5000`
- **API**: `http://your-server-ip:5001`

---

## Manual Installation

If you prefer to install manually or need to troubleshoot:

### Step 1: Start Containers

```bash
docker compose up -d
```

### Step 2: Wait for Services

```bash
# Wait ~10 seconds for PostgreSQL and migrations to complete
sleep 10
```

### Step 3: Apply Database Fixes

Due to Entity Framework Core migrations being generated for SQLite, we need to convert column types for PostgreSQL:

```bash
cat > /tmp/fix_postgres.sql << 'EOF'
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
EOF

docker exec -i hostcraft-postgres-1 psql -U hostcraft -d hostcraft < /tmp/fix_postgres.sql
rm /tmp/fix_postgres.sql
```

### Step 4: Restart API

```bash
docker restart hostcraft-hostcraft-api-1
```

### Step 5: Verify

```bash
docker compose ps
```

All containers should show as "Up" status.

---

## Why These Fixes Are Needed

Entity Framework Core generates migrations based on the development database (SQLite), which creates column types incompatible with PostgreSQL:

- **Boolean columns**: Created as `INTEGER` instead of `boolean`
- **UUID columns**: Created as `TEXT` instead of `uuid`
- **DateTime columns**: Created as `TEXT` instead of `timestamp`
- **Auto-increment**: Missing sequences for ID columns

The installation script automatically converts all these types to their proper PostgreSQL equivalents.

---

## Troubleshooting

### Containers Won't Start
```bash
# Check logs
docker compose logs

# Check specific service
docker logs hostcraft-hostcraft-api-1
```

### Database Connection Issues
```bash
# Verify PostgreSQL is running
docker exec hostcraft-postgres-1 pg_isready -U hostcraft

# Check database exists
docker exec hostcraft-postgres-1 psql -U hostcraft -l
```

### API Not Responding
```bash
# Check API logs
docker logs hostcraft-hostcraft-api-1 --tail 50

# Restart API
docker restart hostcraft-hostcraft-api-1
```

### Complete Reset
```bash
# Stop and remove everything including volumes
docker compose down -v

# Run install script again
./install.sh
```

---

## Configuration

### Environment Variables

Edit `docker-compose.yml` to customize:

- **Database**:
  - `POSTGRES_USER`: PostgreSQL username (default: `hostcraft`)
  - `POSTGRES_PASSWORD`: Database password
  - `POSTGRES_DB`: Database name (default: `hostcraft`)

- **API**:
  - `ConnectionStrings__DefaultConnection`: Database connection string
  - `ASPNETCORE_URLS`: API listening address (default: `http://+:8080`)

- **Web**:
  - `ApiUrl`: API endpoint URL (default: `http://hostcraft-api:8080`)

### Ports

Default ports (can be changed in `docker-compose.yml`):
- Web UI: `5000`
- API: `5001`
- PostgreSQL: `5432` (internal only)

---

## Next Steps

After installation:

1. **Access Web UI**: Navigate to `http://your-server-ip:5000`
2. **Localhost Server**: Automatically configured and ready to use
3. **Create Projects**: Start deploying applications
4. **Add Remote Servers**: Configure additional Docker hosts

Enjoy using HostCraft! 🎉
