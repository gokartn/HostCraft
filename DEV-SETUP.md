# Local Development Setup

## Quick Start (Recommended)

### 1. Start Development Environment

```bash
# From the HostCraft root directory
docker-compose -f docker-compose.dev.yml up --build
```

This will:
- Build fresh images from your local code
- Start PostgreSQL database
- Run migrations automatically
- Start API on http://localhost:5100
- Start Web UI on http://localhost:5000

### 2. Access HostCraft

Open your browser to: **http://localhost:5000**

You'll see the setup page to create your first admin account.

### 3. Stop Development Environment

```bash
docker-compose -f docker-compose.dev.yml down
```

To also remove volumes (fresh start):

```bash
docker-compose -f docker-compose.dev.yml down -v
```

---

## Development Workflow

### Making Code Changes

1. **Edit code** in your IDE (VS Code, Rider, etc.)
2. **Rebuild and restart:**
   ```bash
   docker-compose -f docker-compose.dev.yml up --build
   ```
3. **Test changes** at http://localhost:5000

### Viewing Logs

```bash
# All services
docker-compose -f docker-compose.dev.yml logs -f

# Specific service
docker-compose -f docker-compose.dev.yml logs -f api
docker-compose -f docker-compose.dev.yml logs -f web
docker-compose -f docker-compose.dev.yml logs -f postgres
```

### Database Migrations

Migrations run automatically on API startup. If you need to add a new migration:

```bash
# From HostCraft root directory
cd src/HostCraft.Infrastructure

# Add a new migration
dotnet ef migrations add YourMigrationName --startup-project ../HostCraft.Api

# Migrations will apply automatically when you restart the API
```

### Schema Change Checklist (Alpha)

HostCraft is still in alpha, so we keep a single canonical migration. Whenever you add or modify persisted entities:

1. Update the entity or DbContext model.
2. Edit `src/HostCraft.Infrastructure/Migrations/20251218130443_InitialCreate.cs` so fresh installs receive the new column/index defaults.
3. Update `src/HostCraft.Infrastructure/Migrations/HostCraftDbContextModelSnapshot.cs` to mirror the schema.
4. Run `dotnet build` from the solution root and confirm all six projects succeed.

Do **not** create incremental migrations until we exit the alpha phase.

### Resetting the Database

```bash
# Stop everything and remove volumes
docker-compose -f docker-compose.dev.yml down -v

# Start fresh (migrations will run automatically)
docker-compose -f docker-compose.dev.yml up --build
```

---

## Running Without Docker (Advanced)

### Prerequisites

- .NET 10 SDK installed
- PostgreSQL 18 running locally
- Docker Desktop running (for deploying apps)

### Steps

1. **Start PostgreSQL** (Docker or local install)
   ```bash
   # Using Docker
   docker run -d --name postgres-dev \
     -e POSTGRES_DB=hostcraft \
     -e POSTGRES_USER=hostcraft \
     -e POSTGRES_PASSWORD=DevPassword123! \
     -p 5432:5432 \
     postgres:18-alpine
   ```

2. **Set environment variables**
   ```bash
   export ConnectionStrings__DefaultConnection="Host=localhost;Database=hostcraft;Username=hostcraft;Password=DevPassword123!"
   mkdir -p "$HOME/.hostcraft"
   export Encryption__KeyPath="$HOME/.hostcraft/encryption.key"
   # Optional: export Encryption__Key="base64-key" to bring your own key
   ```

3. **Run migrations**
   ```bash
   cd src/HostCraft.Infrastructure
   dotnet ef database update --startup-project ../HostCraft.Api
   ```

4. **Start API**
   ```bash
   cd ../HostCraft.Api
   dotnet run
   # Runs on http://localhost:5100
   ```

5. **Start Web (in another terminal)**
   ```bash
   cd ../HostCraft.Web
   dotnet run
   # Runs on http://localhost:5000
   ```

---

## Troubleshooting

### "An error occurred during setup"

Check API logs:
```bash
docker-compose -f docker-compose.dev.yml logs api
```

Common causes:
- Database not ready (wait 10 seconds and try again)
- Migration failed (check logs for SQL errors)
- Encryption key invalid (ensure `/app/data/encryption.key` exists or provide a 32-byte base64 key)

### "Cannot connect to API"

1. Check API is running:
   ```bash
   curl http://localhost:5100/health
   ```

2. Check Web can reach API:
   ```bash
   docker-compose -f docker-compose.dev.yml logs web | grep "ApiUrl"
   ```

### "Database connection failed"

1. Check PostgreSQL is running:
   ```bash
   docker-compose -f docker-compose.dev.yml ps postgres
   ```

2. Check connection string:
   ```bash
   docker-compose -f docker-compose.dev.yml exec api env | grep ConnectionStrings
   ```

### Port already in use

If ports 5000, 5100, or 5432 are already in use, edit `docker-compose.dev.yml` and change the port mappings:

```yaml
ports:
  - "5001:8080"  # Changed from 5000
```

---

## Deploying to Production (VPS)

Once you've tested locally, deploy to production:

### 1. Build Production Images

```bash
# Build images
docker-compose build

# Tag images
docker tag hostcraft-api:latest yourdockerhub/hostcraft-api:latest
docker tag hostcraft-web:latest yourdockerhub/hostcraft-web:latest

# Push to registry
docker push yourdockerhub/hostcraft-api:latest
docker push yourdockerhub/hostcraft-web:latest
```

### 2. Deploy to Swarm

On your VPS:

```bash
cd ~/hostcraft

# Pull latest images
docker pull yourdockerhub/hostcraft-api:latest
docker pull yourdockerhub/hostcraft-web:latest

# Update stack
docker stack deploy -c docker-compose.yml hostcraft

# Check rollout
docker service ls
docker service logs hostcraft_api --tail 50
```

---

## Configuration

### Environment Variables

Create `.env` file in project root for local development:

```env
POSTGRES_PASSWORD=DevPassword123!
# ENCRYPTION_KEY=optional-base64-key-if-you-don't-want-auto-generation
WEB_PORT=5000
API_PORT=5100
```

Then use:
```bash
docker-compose -f docker-compose.dev.yml --env-file .env up
```

---

## Next Steps

- [ ] Test database template deployment locally
- [ ] Verify Swarm service creation works
- [ ] Test GitHub webhook integration
- [ ] Deploy to production VPS

**Happy coding!** 🚀
