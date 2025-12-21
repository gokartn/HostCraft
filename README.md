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
   cd hostcraft
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

## � Security & Encryption

HostCraft implements enterprise-grade security features to protect your data and infrastructure.

### Data Encryption at Rest

HostCraft automatically encrypts sensitive data stored in the database using AES-256-GCM encryption:

**Encrypted Fields:**
- User 2FA secrets and recovery codes
- Email confirmation and password reset tokens
- Security stamps and session tokens
- Git provider OAuth client secrets
- Let's Encrypt email addresses
- SSH private keys and passphrases
- Environment variable secrets

**Encryption Key Management:**
1. **Generate a secure encryption key:**
   ```bash
   # On Linux/Mac or WSL:
   chmod +x scripts/generate-encryption-key.sh
   ./scripts/generate-encryption-key.sh
   
   # Or run directly with bash:
   bash scripts/generate-encryption-key.sh
   ```

2. **Set the encryption key in your environment:**
   ```bash
   export ENCRYPTION_KEY="your-generated-key-here"
   ```

3. **For Docker Compose, create a `.env` file:**
   ```bash
   ENCRYPTION_KEY=your-generated-key-here
   ```

**⚠️ CRITICAL SECURITY WARNING:**
- **Backup your encryption key securely** - without it, encrypted data cannot be recovered
- Store the key in a secure secret manager (AWS Secrets Manager, Azure Key Vault, etc.)
- Never commit the key to version control
- Rotate keys periodically using the built-in key rotation feature

### Authentication & Authorization

- **JWT-based authentication** with refresh tokens
- **Two-factor authentication (2FA)** support
- **Role-based access control (RBAC)**
- **Secure password hashing** with PBKDF2
- **Session management** with security stamps
- **Audit logging** for all security events

### Network Security

- **HTTPS enforcement** with automatic Let's Encrypt certificates
- **Reverse proxy protection** with Traefik
- **Docker network isolation** between services
- **SSH key-based server authentication**
- **API rate limiting** and request validation

## �🗑️ Uninstallation

### Quick Uninstall (keeps data)

To remove HostCraft while keeping data for potential reinstall:

```bash
chmod +x uninstall.sh
./uninstall.sh
```

This removes containers, networks, and images but preserves volumes.

### Complete Cleanup (⚠️ REMOVES EVERYTHING)

To remove **EVERY TRACE** of HostCraft including all data, folders, and configuration:

```bash
chmod +x cleanup.sh
./cleanup.sh
```

**This will delete:**
- ✓ All Docker containers, volumes, and networks
- ✓ All application data directories (`/var/lib/hostcraft`, `/opt/hostcraft`, etc.)
- ✓ All configuration files (`/etc/hostcraft`)
- ✓ All log files (`/var/log/hostcraft`)
- ✓ Docker images (optional)
- ✓ Installation directory (optional)

**⚠️ WARNING: This action cannot be undone! All data will be permanently lost.**

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
│   ├── HostCraft.Web/           # Blazor Server UI
│   └── HostCraft.Shared/        # Shared DTOs
├── tests/
│   ├── HostCraft.Infrastructure.Tests/
│   └── HostCraft.Api.Tests/
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
- **Proper bridge vs overlay detection** - ensures correct network types for Swarm vs standalone
- Automatic network type selection based on server mode
- Validation of existing networks
- Prevents deployment with wrong network type

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
- Real-time log streaming via WebSocket/SignalR in progress

## 📝 Why HostCraft?

HostCraft was built from scratch in C#/.NET to provide a modern, type-safe PaaS solution:

1. **Correct network handling** - Swarm uses overlay networks, standalone uses bridge networks
2. **Type safety** - Strong typing in C# prevents configuration mistakes
3. **High performance** - Native .NET performance with efficient Docker.DotNet integration
4. **Single language stack** - Blazor Server + ASP.NET Core for full-stack C#
5. **Enterprise ready** - Built-in HA/DR architecture for production deployments
6. **Integration friendly** - Easy to integrate with existing .NET ecosystems

## 📚 Documentation

Full documentation is available in the codebase. See the `CLAUDE.md` file for technical details and architecture overview.

## 📄 License

TBD

---

*HostCraft - Self-hosted PaaS for Docker Swarm deployments*
