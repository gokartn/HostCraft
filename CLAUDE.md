# Claude Code Instructions for HostCraft

## Project Overview

**HostCraft** is a self-hosted Platform-as-a-Service (PaaS) built in C#/.NET 10, designed for **production-grade High Availability and Disaster Recovery (HA/DR) by default**.

**Version:** 0.0.1-alpha
**Status:** Active Development

### 🎯 **CORE MISSION: HIGH AVAILABILITY FIRST**

**HostCraft's primary goal is making HA/DR easy and accessible:**
- HA/DR is the **DEFAULT and RECOMMENDED** setup, not an advanced option
- Multi-node Docker Swarm clustering is the foundation
- Easy setup that guides users to HA without complexity
- Clear documentation that helps users understand what's happening
- Configurability while maintaining HA best practices

**Key Principles:**
1. **HA by Default**: All recommendations and defaults assume multi-node HA setup
2. **Easy Setup**: Users should achieve HA easily while understanding the architecture
3. **Proper Networking**: Correct bridge vs overlay network detection for Docker Swarm
4. **Service Resilience**: Automatic failover, health checks, and rolling updates
5. **Data Persistence**: Database replication and backup strategies built-in

---

## Recommended HA/DR Architecture

**Default Multi-Node Setup (Minimum for HA):**

### **3-Manager Quorum (Raft Consensus)**
```
Manager Node 1 (VPS/Cloud):
├─ HostCraft API (replicated)
├─ HostCraft Web (replicated)
├─ PostgreSQL (primary or replicated)
├─ Traefik (ingress controller)
└─ Raft leader/follower

Manager Node 2 (VPS/Cloud):
├─ HostCraft API (replicated)
├─ HostCraft Web (replicated)
├─ Traefik (ingress controller)
└─ Raft follower

Manager Node 3 (On-prem/Local):
├─ PostgreSQL (replica or primary)
├─ Compute workloads
└─ Raft follower
```

**Why 3 Managers?**
- **Quorum**: Survives 1 node failure (2/3 still have majority)
- **Split-brain Prevention**: Raft consensus ensures single source of truth
- **Zero-downtime**: Services continue during node maintenance
- **Automatic Failover**: Leader election happens automatically

**Geographic Distribution Best Practices:**
- Manager 1: Cloud Region A (e.g., US-East)
- Manager 2: Cloud Region B (e.g., US-West) or different provider
- Manager 3: On-premises or different availability zone
- This survives regional outages

### **Traefik HA Configuration**
- Deploy as Docker Swarm **service** (not standalone container)
- Run on all manager nodes with `--mode global` or replicas=3
- Use **ingress mode** for ports 80/443 (traffic to any node works)
- Shared Let's Encrypt storage (distributed volume or S3)
- Health checks ensure traffic only to healthy instances

### **PostgreSQL HA Options**
1. **Patroni + etcd** (recommended for full HA)
2. **PostgreSQL Streaming Replication** with automatic failover
3. **Citus** for horizontal scaling
4. **Managed Database** (AWS RDS, Azure Database) with multi-AZ

### **Application Deployment Strategy**
- **Replicas ≥ 2**: All user applications run with minimum 2 replicas
- **Placement Spread**: Distribute across different nodes/AZs
- **Health Checks**: Built-in health monitoring and auto-restart
- **Rolling Updates**: Zero-downtime deployments (start-first strategy)

### **Network Resilience**
- **Overlay Networks**: Multi-host networking with automatic DNS
- **Ingress Mesh**: Traffic to any node reaches the right service
- **Service Discovery**: Automatic service-to-service communication
- **Load Balancing**: Built-in round-robin across replicas

### **Failure Scenarios Covered**
✅ Single node failure (manager or worker)
✅ Network partition (quorum maintained)
✅ Service crash (auto-restart)
✅ Deployment failure (automatic rollback)
✅ Database failure (replica promotion)
✅ Regional outage (if geographically distributed)

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
│   ├── HostCraft.Web/            # Blazor Server UI (port 5050)
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
| Database | PostgreSQL | 18+ |
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

## Docker Volume Permissions (IMPORTANT)

**Issue:** Docker named volumes in Swarm mode can have permission conflicts when containers try to create subdirectories or when volumes have stale data from previous deployments.

**Solution: Mount Directly to Data Directory**
Mount volumes **directly** where the database writes its data, not at parent directories:
- Let the official images' entrypoint scripts handle permission setup
- Avoid subdirectory complexity that causes permission issues
- Prevent conflicts with stale data from previous deployments

**Database Volume Mount Points:**

1. **PostgreSQL**: Mount at `/var/lib/postgresql/data`
   - Official image entrypoint handles all permission setup
   - Default PGDATA location (no environment override needed)
   - Clean, predictable initialization

2. **MongoDB**: Mount at `/data/db`
   - Official image entrypoint handles permissions
   - Default MongoDB data directory

3. **MySQL/MariaDB**: Mount at `/var/lib/mysql`
   - Entrypoint scripts automatically fix ownership
   - Handles non-root user permissions

4. **Redis**: Mount at `/data`
   - Redis runs as redis user
   - Simple, single-directory mount

**Why Direct Mounting Works:**
1. Official database images have entrypoint scripts that fix permissions
2. No subdirectory creation = no permission conflicts
3. No sticky bit issues on Docker Swarm volumes
4. Clean slate on each deployment (volumes can be safely removed)
5. Follows official Docker image documentation patterns

**Current Implementation:**
- PostgreSQL: Volume at `/var/lib/postgresql/data` (direct mount) ✓
- MongoDB: Volume at `/data/db` (direct mount) ✓
- MySQL/MariaDB: Volume at `/var/lib/mysql` (direct mount) ✓
- Redis: Volume at `/data` (direct mount) ✓
- All templates tested and working in Docker Swarm mode ✓

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
hostcraft-web:     # Port 5050, Blazor UI
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

### Planned (next iterations)
- Prometheus/Grafana monitoring
- RBAC/Team management
- 1-click database templates
- HA/DR implementation (architecture done)

### Quality Expectations (always)
- Every change should improve performance and/or code clarity; avoid pure plumbing with no quality gain
- Do not add backward-compatibility shims; refactor forward (fresh reinstall workflow)
- If any database DTO/entity shape changes, update InitialCreate migration and the model snapshot directly
- Uphold separation of concerns: controllers stay thin, services handle logic, repositories handle persistence
- Follow best-practice folder structure (Controllers, Models/DTOs, Validators, Services, Repositories) and keep one class/record per file (small enums/DTOs may share when genuinely cohesive)
- Avoid anti-patterns: god classes, tight coupling to infrastructure, hidden static state, and inline DTOs in controllers

### Communication Guidelines (CRITICAL)
**NEVER do the following:**
- **NEVER create documentation files** (*.md files, README files, etc.) unless explicitly requested by the user
- **NEVER use excessive validation phrases** such as "You're absolutely right", "You're correct", "That's exactly right", or similar over-the-top confirmations
- **NEVER use dramatic discovery phrases** such as "I found the root cause", "I discovered the issue", "I identified the problem" - instead state findings directly and objectively
- **NEVER use superlatives or emotional language** - maintain technical objectivity at all times
- **NEVER suggest workarounds** - always fix the root cause in the code or install.sh
- **NEVER consider backwards compatibility** - refactor forward, fresh reinstall workflow is expected
- **NEVER create new migrations** - always update the InitialCreate migration directly

**Always:**
- State findings directly and objectively (e.g., "The issue is..." not "I found the root cause...")
- Focus on facts and problem-solving without unnecessary praise or validation
- Be concise and professional in all communications
- Disagree respectfully when necessary - honesty is more valuable than false agreement
- Fix the actual problem in the codebase rather than suggesting workarounds
- Update existing migrations directly rather than creating new ones
- Assume fresh install workflow - no need to maintain compatibility with old versions

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
5. **PostgreSQL required** - No SQLite, must have PostgreSQL 18+
6. **Blazor Server** - Not WebAssembly, runs server-side

---

*Last updated: December 19, 2025*
