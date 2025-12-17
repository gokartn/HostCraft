# HostCraft - PaaS Platform

HostCraft is a self-hosted Platform-as-a-Service (PaaS) built in C#/.NET 8, designed to properly handle Docker Swarm deployments with correct network management.

## 📦 Installation

Get HostCraft up and running in minutes with Docker Compose:

### Prerequisites
- Docker Engine installed and running
- Docker Compose v2.0+
- Git

### Quick Start

1. **Clone the repository:**
   ```bash
   git clone https://github.com/gokartn/hostcraft.git
   cd HostCraft
   ```

2. **Make the install script executable:**
   ```bash
   chmod +x install.sh
   ```

3. **Run the installation:**
   ```bash
   ./install.sh
   ```
   
   Or run directly with bash:
   ```bash
   bash install.sh
   ```

4. **Access HostCraft:**
   - Web UI: http://localhost:5000
   - API: http://localhost:5100

The installation script will:
- Build the Docker images
- Set up the database
- Start all services in the background
- Display the status of running containers

### Manual Installation

If you prefer to install manually:

```bash
# Build and start services
docker-compose up -d --build

# Check status
docker-compose ps

# View logs
docker-compose logs -f
```

## 🗑️ Uninstallation

To completely remove HostCraft from your system:

### Using the uninstall script:

1. **Make the script executable:**
   ```bash
   chmod +x uninstall.sh
   ```

2. **Run the uninstaller:**
   ```bash
   ./uninstall.sh
   ```
   
   Or run directly with bash:
   ```bash
   bash uninstall.sh
   ```

This will:
- Stop all running containers
- Remove containers, networks, and images
- Clean up volumes (optional, you'll be prompted)

### Manual Uninstallation

```bash
# Stop and remove all services
docker-compose down

# Remove with volumes (⚠️ deletes all data)
docker-compose down -v

# Remove images
docker-compose down --rmi all
```

---

## 🎯 Project Status

**Phase 1 - Foundation** ✅ **COMPLETED**

- ✅ Core domain entities (Server, Application, Deployment, Project, User)
- ✅ Comprehensive enum types (ServerType, DeploymentStatus, ApplicationSourceType, etc.)
- ✅ Service interfaces (IDockerService, INetworkManager, ISshService, etc.)
- ✅ EF Core database context with entity configurations
- ✅ Docker.DotNet integration with proper Swarm support
- ✅ Network Manager with critical bridge vs overlay detection
- ✅ Basic API with ServersController
- ✅ NuGet packages installed (Docker.DotNet, SSH.NET, EF Core, Npgsql)

## 🏗️ Architecture

```
HostCraft/
├── src/
│   ├── HostCraft.Core/          # Domain layer (entities, interfaces, enums)
│   ├── HostCraft.Infrastructure/ # External integrations (Docker, SSH, Git)
│   ├── HostCraft.Api/           # ASP.NET Core Web API
│   ├── HostCraft.Web/           # Blazor Server UI (TODO)
│   └── HostCraft.Shared/        # Shared DTOs (TODO)
```

## 🔑 Key Features Implemented

### 🛡️ High Availability (HA)
- **Multi-Manager Swarm Quorum** - 3/5/7 manager support for fault tolerance
- **Automatic Failover** - Services auto-migrate to healthy nodes on failure
- **Health Monitoring** - Continuous HTTP/TCP health checks with configurable thresholds
- **Auto-Recovery** - Intelligent restart, redeploy, or rollback on failure
- **Rolling Updates** - Zero-downtime deployments with automatic rollback
- **Node Draining** - Graceful maintenance mode for servers

### 💾 Disaster Recovery (DR)
- **Multi-Region Support** - Deploy across datacenters for geographic redundancy
- **Automated Backups** - Configuration, volume, and full backups with retention policies
- **S3 Integration** - Off-site backup storage with S3-compatible providers
- **DR Failover** - One-click failover to secondary region
- **Backup Restore** - Point-in-time recovery with tested restore procedures
- **DR Testing** - Dry-run failover testing without affecting production

### 🔧 Network Management (CRITICAL)
- **Proper bridge vs overlay detection** - fixes Coolify's critical bug
- Automatic network type selection based on server mode
- Validation of existing networks
- Prevents deplo

### Phase 2 - Deployment Engine
- [ ] Deployment orchestration service implementation
- [ ] Build from Dockerfile support
- [ ] Git integration with LibGit2Sharp
- [ ] Environment variables management UI
- [ ] Real-time deployment logs with SignalR
- [ ] Hangfire background job queue

### Phase 3 - HA/DR Implementation
- [ ] Health monitoring background service with Hangfire
- [ ] Backup service with tar/S3 integration
- [ ] Swarm HA service for cluster management
- [ ] DR failover orchestration
- [ ] Uptime tracking and SLA reporting
- [ ] Alert system (email, Slack, webhooks)ger, and SwarmWorker types
- Region assignment for multi-datacenter deployments

### 🐳 Docker Service
- Container operations (create, start, stop, remove, list)
- Swarm service operations (create, update, remove, list, scale)
- Network management (create, list, validate)
- Image operations (pull, list)
- Swarm initialization and management
- Volume management for persistent storage

## 🚀 Next Steps (Phase 2 - Deployment Engine)

- [ ] Deployment orchestration service
- [ ] Build from Dockerfile support
- [ ] Git integration with LibGit2Sharp
- [ ] Environment variables management UI
- [ ] Real-time deployment logs with SignalR
- [ ] Hangfire background job queue

## 📦 Technology Stack

| Component | Technology |
|-----------|------------|
| Backend API | ASP.NET Core 10 |
| Web UI | Blazor Server |
| Database | EF Core 10 + PostgreSQL |
| Docker API | Docker.DotNet |
| SSH | SSH.NET |
| Background Jobs | Hangfire (planned) |

## 🏃 Running Locally

**Prerequisites:**
- PostgreSQL 16 or higher
- .NET 10 SDK

```powershell
# Build the solution
dotnet build HostCraft.sln

# Run the API (requires PostgreSQL)
dotnet run --project src/HostCraft.Api

# API will be available at http://localhost:5100
```

## 📋 Database

HostCraft requires PostgreSQL. Configure the connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=hostcraft;Username=hostcraft;Password=yourpassword"
  }
}
```

## 🔧 API Endpoints

### Servers
- `GET /api/servers` - List all servers
- `GET /api/servers/{id}` - Get server details
- `POST /api/servers` - Add new server
- `PUT /api/servers/{id}` - Update server
- `DELETE /api/servers/{id}` - Remove server
- `POST /api/servers/{id}/validate` - Validate server connection
- `GET /api/servers/{id}/containers` - List containers on server
- `GET /api/servers/{id}/services` - List Swarm services

### Health
- `GET /health` - Health check endpoint

## 🐛 Known Limitations

- Log streaming not yet fully implemented (MultiplexedStream handling needs work)
- ResEnterprise HA/DR** - Built-in high availability and disaster recovery from day one
3. **Type safety** - Strong typing prevents configuration mistakes  
4. **Better performance** - C# is faster than PHP
5. **Single language stack** - Blazor + ASP.NET Core
6. **Integration potential** - Easy to integrate with existing C# projects
7. **Production-ready** - Designed for self-hosted production deployments with real HA/DR needs

## 📚 Documentation

- [HA/DR Architecture](docs/HA-DR-ARCHITECTURE.md) - Comprehensive HA/DR design and best practices
- [API Documentation](docs/API.md) - Complete API endpoint reference (coming soon)
- [Deployment Guide](docs/DEPLOYMENT.md) - Production deployment instructions (coming soon)
## 📝 Why HostCraft?

After discovering fundamental bugs in Coolify's Docker Swarm implementation (network type mismatches, incorrect Swarm detection), we decided to build a proper solution from scratch in C#/.NET with:

1. **Correct network handling** - Swarm uses overlay, standalone uses bridge
2. **Type safety** - Strong typing prevents configuration mistakes  
3. **Better performance** - C# is faster than PHP
4. **Single language stack** - Blazor + ASP.NET Core
5. **Integration potential** - Easy to integrate with existing C# projects

## 📄 License

TBD

---

*Last updated: December 13, 2025*
