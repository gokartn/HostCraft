# Claude Code Instructions for HostCraft

## Project Overview

**HostCraft** is a self-hosted Platform-as-a-Service (PaaS) built in C#/.NET 10, providing production-grade deployments with built-in HA/DR and proper Docker Swarm support.

**Version:** 0.0.1-alpha
**Status:** Active Development
**Core Advantage:** Correct bridge vs overlay network detection for Docker Swarm deployments

---

## Critical Build Verification Rule

**BEFORE stating any code changes are ready:**

1. Run `dotnet build` from solution root (`C:\Users\firefighter\Documents\GitHub\HostCraft`)
2. Verify ALL 7 projects compile with 0 errors:
   - HostCraft.Core
   - HostCraft.Infrastructure
   - HostCraft.Infrastructure.Tests
   - HostCraft.Api
   - HostCraft.Api.Tests
   - HostCraft.Web
   - HostCraft.Shared
3. Report: "Build verified - ALL 7 projects compiled, X warnings, 0 errors"

**Never say:**
- "It will build now"
- "This should compile"
- "Now it's fixed" (without actual build output proof)

---

## Architecture

```
HostCraft/
├── src/
│   ├── HostCraft.Core/           # Domain: Entities, Interfaces, Enums
│   ├── HostCraft.Infrastructure/ # Docker, SSH, Git, Proxy, Database
│   ├── HostCraft.Api/            # ASP.NET Core REST API (port 5100)
│   ├── HostCraft.Web/            # Blazor Server UI (port 5000)
│   └── HostCraft.Shared/         # Shared DTOs
├── tests/
│   ├── HostCraft.Infrastructure.Tests/
│   └── HostCraft.Api.Tests/
├── scripts/                      # install.sh, uninstall.sh, cleanup.sh
├── docs/                         # Architecture documentation
└── docker-compose.yml           # PostgreSQL + API + Web
```

---

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET | 10.0 |
| API | ASP.NET Core | 10.0 |
| UI | Blazor Server | 10.0 |
| ORM | Entity Framework Core | 10.0 |
| Database | PostgreSQL | 16+ |
| Docker Client | Docker.DotNet | 3.125.15 |
| SSH Client | SSH.NET (Renci.SshNet) | 2023.0.0 |
| Logging | Serilog | 8.0.3 |

---

## Core Domain Entities

### Primary Entities
- **Server**: Docker host (Standalone, SwarmManager, SwarmWorker)
- **Application**: Deployed app (Container or Swarm Service)
- **Deployment**: Deployment operation with status tracking
- **Project**: Logical grouping of applications

### Supporting Entities
- **PrivateKey**: SSH keys for server auth
- **EnvironmentVariable**: App configuration/secrets
- **Volume**: Persistent storage
- **GitProvider**: GitHub/GitLab/Gitea connections
- **Region**: Multi-datacenter support
- **Certificate**: SSL certificates
- **Backup**: Backup records
- **HealthCheck**: Health monitoring results

---

## Critical Enums

### ServerType (Determines Network Type)
```csharp
Standalone = 0    // Uses Bridge networks
SwarmManager = 1  // Uses Overlay networks
SwarmWorker = 2   // Uses Overlay networks
```

### NetworkType (THE BUG FIX)
```csharp
Bridge = 0   // Single-host (standalone only)
Overlay = 1  // Multi-host (Swarm required)
Host = 2     // Host stack sharing
None = 3     // No networking
```

**Critical Logic:** NetworkManager ensures correct network selection - Swarm services require overlay networks, standalone containers use bridge networks.

### DeploymentMode
```csharp
Container  // Standalone container
Service    // Docker Swarm service
```

### ApplicationSourceType
```csharp
DockerImage = 0    // Pre-built image
DockerCompose = 1  // docker-compose.yml
Dockerfile = 2     // Build from Dockerfile
Git = 3            // Deploy from Git repo
```

---

## Key Services & Interfaces

### IDockerService (Singleton)
- Container ops: Create, Start, Stop, Remove, List, Inspect, Logs
- Service ops: Create, Update, Remove, List, Inspect, Logs, Scale
- Network ops: Create, Remove, List, EnsureExists
- Swarm ops: Init, Join, Leave, GetJoinToken
- SSH tunneling for remote Docker via `socat`

### INetworkManager (Scoped) - THE FIX
- `GetRequiredNetworkType()`: Returns Overlay for Swarm, Bridge for Standalone
- `ValidateNetworkTypeAsync()`: Validates existing networks
- `EnsureNetworkExistsAsync()`: Creates with correct type

### IDeploymentService (Scoped)
- Routes to Swarm or Standalone handler
- Full pipeline: Clone → Build → Network → Deploy → Health

### IGitService (Scoped)
- Clone, Checkout, GetCommit info
- Token-based auth for GitHub

### IStackService (Scoped)
- Docker Stack deployment (docker-compose for Swarm)

---

## API Endpoints Structure

```
/api/servers          # Server CRUD + Swarm init/join
/api/applications     # App CRUD + deploy/stop/restart/scale
/api/deployments      # Deployment history + logs + rollback
/api/containers       # Container management
/api/services         # Swarm service management
/api/networks         # Network management
/api/images           # Image management
/api/git-providers    # GitHub account management
/api/webhooks/github  # GitHub push/PR webhooks
/api/projects         # Project/workspace management
/api/system-settings  # Global config
/health               # Health check
```

---

## Blazor UI Pages

```
/                     # Home dashboard
/servers              # Server list
/servers/{id}         # Server details
/servers/new          # Add server
/applications         # Application list
/applications/{id}    # Application details
/applications/new     # New application
/deployments          # Deployment history
/containers           # Container management
/services             # Swarm services
/swarm-nodes          # Swarm node management
/images               # Docker images
/networks             # Docker networks
/terminal             # SSH terminal
/settings             # System settings
```

---

## Docker Deployment

```yaml
# docker-compose.yml services:
hostcraft-api:     # Port 5100, runs on Swarm manager
hostcraft-web:     # Port 5000, Blazor UI
hostcraft-postgres: # Port 5432 (internal)

# Overlay network: hostcraft-network (attachable)
```

---

## Development Workflow

### Running Locally
```powershell
# Requires PostgreSQL running
dotnet build HostCraft.sln
dotnet run --project src/HostCraft.Api   # API on :5100
dotnet run --project src/HostCraft.Web   # UI on :5000
```

### Running with Docker
```bash
./install.sh           # Full install
docker-compose up -d   # Start services
docker-compose logs -f # View logs
```

---

## Common Issues & Solutions

### HostCraft.Web Build Errors
- Blazor render mode errors: Remove `@rendermode` from pages
- Missing dependencies: Check `Program.cs` DI registration

### HostCraft.Infrastructure Errors
- Missing Docker.DotNet or SSH.NET references
- Check NuGet package restoration

### All Projects
- Missing using statements
- Wrong namespace references
- DI registration missing in `Program.cs`

---

## Current Development Status

### Completed (Phase 1 & 2)
- Core entities and enums
- Docker.DotNet integration with Swarm
- NetworkManager with correct bridge/overlay detection
- Full API with all controllers
- Blazor Server UI with 15+ pages
- GitHub integration (webhooks, push-to-deploy)
- SSH tunneling for remote Docker

### In Progress (Phase 3)
- Docker Compose deployment testing
- Secrets management
- Pre/post deploy hooks

### Planned (Phase 4+)
- Prometheus/Grafana monitoring
- RBAC/Team management
- 1-click database templates
- HA/DR implementation (architecture done)

---

## Security Standards

**Never include in code or docs:**
- Real IP addresses (use 10.0.0.x or examples)
- Real passwords or tokens
- Production credentials

**Always:**
- Validate HMAC-SHA256 on GitHub webhooks
- Use SSH keys over passwords
- Store secrets as environment variables

---

## Testing

```powershell
# Run all tests
dotnet test

# Specific test project
dotnet test tests/HostCraft.Infrastructure.Tests
dotnet test tests/HostCraft.Api.Tests
```

---

## Key Files to Know

| Purpose | File Path |
|---------|-----------|
| API Entry | `src/HostCraft.Api/Program.cs` |
| Web Entry | `src/HostCraft.Web/Program.cs` |
| Docker Service | `src/HostCraft.Infrastructure/Services/DockerService.cs` |
| Network Manager | `src/HostCraft.Infrastructure/Services/NetworkManager.cs` |
| Deployment Service | `src/HostCraft.Infrastructure/Services/DeploymentService.cs` |
| DB Context | `src/HostCraft.Infrastructure/Data/HostCraftDbContext.cs` |
| Server Entity | `src/HostCraft.Core/Entities/Server.cs` |
| Application Entity | `src/HostCraft.Core/Entities/Application.cs` |
| Enums | `src/HostCraft.Core/Enums/*.cs` |

---

## Reminders

1. **Always build before claiming done** - Run `dotnet build` and verify 0 errors
2. **Check all 7 projects** - A single project failure breaks Docker deployment
3. **Network type matters** - Swarm = Overlay, Standalone = Bridge
4. **SSH tunneling** - Remote Docker connections use `socat` via SSH
5. **PostgreSQL required** - No SQLite, must have PostgreSQL 16+
6. **Blazor Server** - Not WebAssembly, runs server-side

---

*Last updated: December 19, 2025*
