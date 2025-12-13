# HostCraft Development Guide

## Local Development Setup

### Prerequisites
- .NET 8 SDK
- Docker Desktop (for local Docker daemon)
- Optional: OpenSSH Server (for terminal testing)

### Quick Start

1. **Run the Web UI**
   ```powershell
   cd src/HostCraft.Web
   dotnet run
   ```
   Navigate to `https://localhost:5001`

2. **Run the API**
   ```powershell
   cd src/HostCraft.Api
   dotnet run
   ```
   API available at `https://localhost:7001`

### Testing Locally

#### 1. Test UI Components (No servers needed)
- All pages, forms, navigation work immediately
- Mock data is already in place
- Test: Dashboard, server list, forms, layouts

#### 2. Test Docker Operations (Requires Docker Desktop)

**Add a "local" server:**
- Name: `localhost`
- Host: `localhost` or `127.0.0.1`
- Port: `2375` (or use SSH to port 22)
- Type: `Standalone`

**Enable Docker API (Windows):**
```powershell
# Expose Docker daemon on TCP (WARNING: Development only!)
# Docker Desktop → Settings → General → "Expose daemon on tcp://localhost:2375"
```

**Or use SSH tunnel to Docker socket:**
```powershell
# If you have OpenSSH Server installed
ssh -L 2375:/var/run/docker.sock localhost
```

Now you can test:
- ✅ List containers
- ✅ Create containers
- ✅ Manage images
- ✅ Network operations (bridge networks)
- ✅ Volume management

#### 3. Test Terminal (Requires local SSH)

**Install OpenSSH Server (Windows):**
```powershell
# Install
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0

# Start service
Start-Service sshd
Set-Service -Name sshd -StartupType 'Automatic'

# Allow firewall
New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

**Generate SSH key pair:**
```powershell
ssh-keygen -t ed25519 -f $HOME\.ssh\hostcraft_test
```

**Add to authorized_keys:**
```powershell
# Copy public key content to authorized_keys
cat $HOME\.ssh\hostcraft_test.pub | Out-File -Append $HOME\.ssh\authorized_keys
```

**Test connection:**
```powershell
ssh -i $HOME\.ssh\hostcraft_test localhost
```

**In HostCraft:**
- Add server with `Host: localhost`, `Port: 22`
- Paste private key content
- Click "Test Connection" → should work!
- Open terminal → full interactive shell

#### 4. Test Database Operations
```powershell
cd src/HostCraft.Api

# Create initial migration
dotnet ef migrations add InitialCreate

# Apply to database
dotnet ef database update

# Check SQLite database
sqlite3 hostcraft.db
.tables
.schema servers
```

### What You CAN'T Test Locally

#### Multi-node Swarm Cluster
- Need actual remote servers
- Minimum 3 nodes for proper quorum testing
- Can't simulate overlay networks locally

#### Solution: Use your existing Swarm cluster
- housio-Hetzner (manager)
- housio-home (worker)
- housio-worker-node (worker)

#### HA/DR Features
- Multi-region failover
- Geographic backup distribution
- Cross-datacenter operations

These are final-stage features anyway.

### Local Testing Workflow

**Phase 1: Pure Local (Day 1-7)**
```
✅ Build all UI pages
✅ API endpoints with SQLite
✅ Docker operations on local daemon
✅ Form validation, routing
✅ Database migrations
```

**Phase 2: Local + SSH (Day 7-14)**
```
✅ Terminal functionality
✅ SSH key management
✅ Remote command execution
✅ Real-time output streaming
```

**Phase 3: Deploy to Dev Server (Day 14-21)**
```
✅ Connect to one remote server
✅ Test real SSH connections
✅ Docker operations over SSH tunnel
✅ Container deployment
```

**Phase 4: Full Swarm Cluster (Day 21+)**
```
✅ Multi-node Swarm operations
✅ Overlay networks
✅ Service replication
✅ Manager/worker coordination
✅ Network scope validation (THE BUG FIX!)
```

### Tips for Local Development

1. **Mock Data is Your Friend**
   - All UI components have mock data
   - Replace with API calls progressively
   - Comment out when ready: `// TODO: Replace with API call`

2. **Docker Desktop Settings**
   - Resources → Use at least 4GB RAM
   - Enable Kubernetes if testing K8s support later
   - WSL2 backend recommended on Windows

3. **Use SQLite for Dev, PostgreSQL for Prod**
   - Already configured in appsettings
   - No PostgreSQL installation needed locally
   - Switch connection string for production

4. **Hot Reload Works**
   - Blazor: Changes reflect immediately
   - API: May need restart for some changes
   - CSS: Instant with hot reload

5. **SignalR Debugging**
   - Browser DevTools → Network → WS (WebSockets)
   - Watch real-time messages
   - See terminal commands/responses

### Common Local Testing Scenarios

**Scenario 1: Test Server Form**
```
1. Run Web project
2. Navigate to /servers/new
3. Fill form with localhost details
4. Click "Test Connection" → validates form
5. Submit → saves to SQLite
6. View in /servers list
```

**Scenario 2: Test Docker Operations**
```
1. Add localhost as server
2. Docker Desktop must be running
3. Navigate to /servers/1
4. Quick Actions:
   - Docker Info → shows local daemon info
   - List Containers → your running containers
   - List Services → if Swarm init'd locally
```

**Scenario 3: Test Terminal**
```
1. Install OpenSSH Server
2. Add localhost server with SSH key
3. Navigate to /servers/1
4. Click "Connect Terminal"
5. Run commands:
   > docker ps
   > ls -la
   > top
All output streams to browser!
```

### When to Deploy to Real Servers

Deploy when you want to test:
- ❌ Real Swarm cluster operations
- ❌ Overlay network validation (the Coolify bug fix)
- ❌ Multi-node service deployment
- ❌ Manager/worker interaction
- ❌ Production-grade SSH tunneling

But **80-90% can be developed and tested locally** first!

### Incremental Deployment Strategy

1. **Start local** - build features, test UI
2. **Add localhost** - test Docker + SSH locally  
3. **Connect to 1 server** - test real remote operations
4. **Add Swarm cluster** - test multi-node features
5. **Production** - full HA/DR deployment

This way you minimize the "deploy to test" cycle and keep development fast.
