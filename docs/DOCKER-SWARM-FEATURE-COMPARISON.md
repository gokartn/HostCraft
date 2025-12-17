# Docker Swarm Feature Comparison: HostCraft vs Coolify vs Dokploy

## Executive Summary

This document compares Docker Swarm implementation across three PaaS platforms to identify feature gaps in HostCraft.

---

## 1. Coolify Docker Swarm Implementation

### Core Architecture
- **SwarmDocker Model** (`app/Models/SwarmDocker.php`): Separate model from StandaloneDocker
- **Swarm Detection**: `$server->isSwarm()` method for conditional logic
- **Deployment Jobs**: `ApplicationDeploymentJob.php` handles both standalone and swarm
- **Service Management**: Full Docker service API integration

### Key Features

#### ✅ Swarm-Specific Deployment
- **Stack Deployment**: `docker stack deploy` for multi-service apps
- **Service Updates**: Rolling updates via `docker service update`
- **Container Orchestration**: Automatic service scheduling across nodes
- **Replicas Management**: `swarm_replicas` field on Application model

#### ✅ Service Configuration
```php
// Swarm placement constraints
$application->swarm_placement_constraints  // Base64 encoded constraints
$application->swarm_replicas              // Number of service replicas

// Example constraints: node.role==manager, node.labels.region==us-east
```

#### ✅ Network Management
- **Overlay Networks**: Automatic overlay network creation for swarm
- **Network Isolation**: Service-to-service communication within swarm network
- **Traefik Integration**: Swarm-aware load balancing with labels

#### ✅ Service Discovery
```php
// Labels for swarm services
'com.docker.swarm.service.name' => $appName
'com.docker.stack.namespace' => $stackName
```

#### ✅ Deployment Strategy
- **Zero-Downtime**: Uses `update-order: start-first` by default
- **Health Checks**: Integrated with swarm service health monitoring
- **Rollback Support**: Automatic rollback on failed updates

#### ✅ Container Status Monitoring
```php
// GetContainersStatus.php
if ($server->isSwarm()) {
    $labels = data_get($container, 'Spec.Labels');
    $uuid = data_get($labels, 'coolify.name');
} else {
    $labels = data_get($container, 'Config.Labels');
}
```

#### ✅ Log Aggregation
```php
// Swarm service logs
if ($server->isSwarm()) {
    $output = instant_remote_process([
        "docker service logs -n {$lines} {$container_id} 2>&1"
    ], $server);
} else {
    $output = instant_remote_process([
        "docker logs -n {$lines} {$container_id} 2>&1"
    ], $server);
}
```

#### ✅ Resource Limits (Swarm-Specific)
```php
// Remove standalone-only settings for swarm
data_forget($docker_compose, 'services.'.$this->container_name.'.mem_limit');
data_forget($docker_compose, 'services.'.$this->container_name.'.cpus');

// Use swarm deployment resources instead
'deploy' => [
    'resources' => [
        'limits' => ['cpus' => '0.5', 'memory' => '512M'],
        'reservations' => ['cpus' => '0.25', 'memory' => '256M']
    ]
]
```

#### ✅ Validation
```php
// Docker Swarm validation
public function validateDockerSwarm() {
    $swarmStatus = instant_remote_process(['docker info|grep -i swarm'], $this, false);
    $swarmStatus = str($swarmStatus)->trim()->after(':')->trim();
    if ($swarmStatus === 'inactive') {
        throw new \Exception('Docker Swarm is not initiated.');
    }
    return true;
}
```

### UI Features
- **Swarm Toggle**: UI option to select Swarm vs Standalone destination
- **Service View**: Shows service replicas and task distribution
- **Node Management**: View and manage swarm nodes
- **Stack Removal**: `docker stack rm` for clean service removal

---

## 2. Dokploy Docker Swarm Implementation

### Core Architecture
- **Unified Container Type**: Single Application model handles both modes
- **Type Detection**: `composeType: "stack" | "docker-compose"`
- **Swarm Settings Component**: `ModifySwarmSettings.tsx` for advanced config
- **Remote Docker**: `getRemoteDocker()` abstraction for swarm operations

### Key Features

#### ✅ Advanced Swarm Configuration UI
```typescript
// Placement constraints with JSON schema validation
PlacementSwarmSchema = z.object({
    Constraints: z.array(z.string()).optional(),
    Preferences: z.array(z.object({
        Spread: z.object({ SpreadDescriptor: z.string() })
    })).optional(),
    MaxReplicas: z.number().optional(),
    Platforms: z.array(z.object({
        Architecture: z.string(),
        OS: z.string()
    })).optional()
}).strict();

// Update strategy
UpdateConfigSwarmSchema = z.object({
    Parallelism: z.number().optional(),
    Delay: z.number().optional(),
    FailureAction: z.enum(['continue', 'pause', 'rollback']).optional(),
    Monitor: z.number().optional(),
    MaxFailureRatio: z.number().optional(),
    Order: z.enum(['stop-first', 'start-first']).optional()
}).strict();

// Rollback strategy
RollbackConfigSwarmSchema = z.object({
    Parallelism: z.number().optional(),
    Delay: z.number().optional(),
    FailureAction: z.enum(['continue', 'pause']).optional(),
    Monitor: z.number().optional(),
    MaxFailureRatio: z.number().optional(),
    Order: z.enum(['stop-first', 'start-first']).optional()
}).strict();
```

#### ✅ Service Mode Configuration
```typescript
ModeSwarmSchema = z.object({
    Replicated: z.object({
        Replicas: z.number().optional()
    }).optional(),
    Global: z.object({}).optional()
}).strict();
```

#### ✅ Endpoint Configuration
```typescript
EndpointSpecSwarmSchema = z.object({
    Mode: z.string().optional(),  // vip, dnsrr
    Ports: z.array(z.object({
        Protocol: z.string().optional(),
        TargetPort: z.number().optional(),
        PublishedPort: z.number().optional(),
        PublishMode: z.string().optional()  // ingress, host
    })).optional()
}).strict();
```

#### ✅ Network Configuration
```typescript
NetworksSwarmSchema = z.object({
    Target: z.string().optional(),
    Aliases: z.array(z.string()).optional(),
    DriverOpts: z.object({}).optional()
}).strict();
```

#### ✅ Service Labels
```typescript
// Dynamic label management
LabelsSwarmSchema = z.record(z.string());
```

#### ✅ Deployment Strategies
```typescript
// mechanizeDockerContainer.ts
const settings: ServiceCreateOptions = {
    Name: application.appName,
    TaskTemplate: {
        ContainerSpec: {
            Image: dockerImage,
            Env: envVariables,
            Mounts: volumes,
            StopGracePeriod: stopGracePeriodSwarm || undefined,
            Labels: containerLabels
        },
        Resources: {
            Limits: cpuLimit || memoryLimit ? {
                NanoCPUs: cpuLimit,
                MemoryBytes: memoryLimit
            } : undefined,
            Reservations: cpuReservation || memoryReservation ? {
                NanoCPUs: cpuReservation,
                MemoryBytes: memoryReservation
            } : undefined
        },
        Placement: placementSwarm || (haveMounts ? {
            Constraints: ["node.role==manager"]
        } : undefined),
        RestartPolicy: {
            Condition: 'any',
            Delay: 5000000000, // 5 seconds in nanoseconds
            MaxAttempts: 3
        }
    },
    Mode: modeSwarm || {
        Replicated: { Replicas: replicas }
    },
    UpdateConfig: updateConfigSwarm || {
        Parallelism: 1,
        Order: 'start-first'
    },
    RollbackConfig: rollbackConfigSwarm,
    EndpointSpec: endpointSpecSwarm,
    Networks: networksSwarm,
    Labels: labelsSwarm
};
```

#### ✅ Stack Management
```typescript
// Deploy stack
if (compose.composeType === "stack") {
    command += `docker stack deploy -c ${composeFile} ${compose.appName}`;
} else {
    command += `docker compose -p ${compose.appName} up -d`;
}

// Remove stack
if (compose.composeType === "stack") {
    await execAsync(`docker stack rm ${compose.appName}`);
} else {
    await execAsync(`docker compose -p ${compose.appName} down`);
}
```

#### ✅ Node Management UI
- **Add Worker Nodes**: Join token generation with command display
- **Add Manager Nodes**: Separate manager join token
- **Node Information**: View node status, roles, availability
- **Architecture Warnings**: Alerts about architecture compatibility

#### ✅ Swarm Monitoring Dashboard
```typescript
// SwarmMonitorCard component
- Node count and status
- Service distribution across nodes
- Resource allocation per node
- Health status aggregation
```

#### ✅ Container Discovery (Dual Mode)
```typescript
// Native mode (docker ps)
const command = `docker ps --filter "label=com.docker.swarm.service.name=${appName}"`;

// Swarm mode (docker service ps)
const command = `docker service ps ${appName} --format 'CONTAINER ID : {{.ID}} | Name: {{.Name}}'`;

// Stack mode (docker stack ps)
const command = `docker stack ps ${appName} --format 'CONTAINER ID : {{.ID}} | Name: {{.Name}}'`;
```

#### ✅ Log Viewing (Dual Mode)
```typescript
// UI toggle between native and swarm logs
<Switch 
    checked={option === "native"}
    onCheckedChange={(checked) => setOption(checked ? "native" : "swarm")}
/>

// Native: docker logs <container>
// Swarm: docker service logs <service>
```

#### ✅ Service Stats
```typescript
// Get stats from swarm service
const filter = {
    status: ["running"],
    label: [`com.docker.swarm.service.name=${appName}`]
};
const containers = await docker.listContainers({
    filters: JSON.stringify(filter)
});
```

#### ✅ Backup Integration
```typescript
// Detect container type for backup
if (labels.includes("com.docker.swarm.service.name")) {
    return "Docker Swarm Service";
} else if (labels.includes("com.docker.compose.project")) {
    return "Docker Compose";
} else {
    return "Regular Container";
}
```

### Swarm Setup & Validation
```typescript
// Server setup includes swarm initialization
setupSwarm = () => `
    if docker info | grep -q 'Swarm: active'; then
        echo "Swarm already active"
    else
        advertise_addr=$(ip route get 1 | awk '{print $7; exit}')
        docker swarm init --advertise-addr $advertise_addr
        echo "Swarm initialized"
    fi
`;

// Validation
validateSwarm = () => `
    if docker info --format '{{.Swarm.LocalNodeState}}' | grep -q 'active'; then
        echo true
    else
        echo false
    fi
`;
```

---

## 3. HostCraft Current State (Feature Gap Analysis)

### ✅ What HostCraft Currently Has
1. **Docker Client**: Integrated Docker.DotNet library
2. **Container Management**: Basic container operations (start, stop, restart)
3. **Image Management**: Pull, list, inspect images
4. **Network Management**: Create and manage Docker networks
5. **Deployment System**: Basic deployment pipeline
6. **Server Management**: Multi-server support with SSH
7. **Log Viewing**: Container log streaming

### ❌ What HostCraft Is Missing (Critical Swarm Features)

#### 1. Swarm Detection & Initialization
```csharp
// MISSING: Swarm state detection
public async Task<bool> IsSwarmActive()
{
    var info = await _dockerClient.System.GetSystemInfoAsync();
    return info.Swarm?.LocalNodeState == "active";
}

// MISSING: Swarm initialization
public async Task InitializeSwarm(string advertiseAddr = null)
{
    await _dockerClient.Swarm.InitSwarmAsync(new SwarmInitParameters
    {
        AdvertiseAddr = advertiseAddr,
        ListenAddr = "0.0.0.0:2377"
    });
}
```

#### 2. Service Management (Core Swarm API)
```csharp
// MISSING: Create service
public async Task<string> CreateService(ServiceCreateParameters parameters)
{
    var response = await _dockerClient.Swarm.CreateServiceAsync(parameters);
    return response.ID;
}

// MISSING: Update service (rolling updates)
public async Task UpdateService(string serviceId, ServiceUpdateParameters parameters)
{
    await _dockerClient.Swarm.UpdateServiceAsync(serviceId, parameters);
}

// MISSING: List services
public async Task<IList<SwarmService>> ListServices()
{
    return await _dockerClient.Swarm.ListServicesAsync();
}

// MISSING: Inspect service
public async Task<SwarmService> InspectService(string serviceId)
{
    return await _dockerClient.Swarm.InspectServiceAsync(serviceId);
}

// MISSING: Remove service
public async Task RemoveService(string serviceId)
{
    await _dockerClient.Swarm.RemoveServiceAsync(serviceId);
}

// MISSING: Service logs
public async Task<Stream> GetServiceLogs(string serviceId, bool follow = false)
{
    return await _dockerClient.Swarm.GetServiceLogsAsync(serviceId, new ServiceLogsParameters
    {
        Follow = follow,
        Stdout = true,
        Stderr = true,
        Timestamps = true
    });
}
```

#### 3. Node Management
```csharp
// MISSING: List swarm nodes
public async Task<IList<NodeListResponse>> ListNodes()
{
    return await _dockerClient.Swarm.ListNodesAsync();
}

// MISSING: Inspect node
public async Task<NodeResponse> InspectNode(string nodeId)
{
    return await _dockerClient.Swarm.InspectNodeAsync(nodeId);
}

// MISSING: Update node (promote/demote, drain/active)
public async Task UpdateNode(string nodeId, NodeUpdateParameters parameters)
{
    await _dockerClient.Swarm.UpdateNodeAsync(nodeId, parameters);
}

// MISSING: Remove node
public async Task RemoveNode(string nodeId, bool force = false)
{
    await _dockerClient.Swarm.RemoveNodeAsync(nodeId, force);
}

// MISSING: Get join tokens
public async Task<(string WorkerToken, string ManagerToken)> GetJoinTokens()
{
    var swarm = await _dockerClient.Swarm.InspectSwarmAsync();
    return (swarm.JoinTokens.Worker, swarm.JoinTokens.Manager);
}
```

#### 4. Stack Deployment
```csharp
// MISSING: Deploy stack from docker-compose.yml
public async Task DeployStack(string stackName, string composeContent)
{
    // 1. Write compose file to temp location
    // 2. Use docker CLI to deploy: docker stack deploy -c file.yml stackName
    // 3. Or parse compose and create services via API
    
    // Note: Docker.DotNet doesn't have native stack support,
    // must use docker CLI or implement compose parser
}

// MISSING: Remove stack
public async Task RemoveStack(string stackName)
{
    // docker stack rm stackName
    // Or list services with label and remove them
    var services = await ListServices();
    var stackServices = services.Where(s => 
        s.Spec.Labels.ContainsKey("com.docker.stack.namespace") &&
        s.Spec.Labels["com.docker.stack.namespace"] == stackName
    );
    
    foreach (var service in stackServices)
    {
        await RemoveService(service.ID);
    }
}

// MISSING: List stacks
public async Task<IList<string>> ListStacks()
{
    var services = await ListServices();
    return services
        .Where(s => s.Spec.Labels.ContainsKey("com.docker.stack.namespace"))
        .Select(s => s.Spec.Labels["com.docker.stack.namespace"])
        .Distinct()
        .ToList();
}
```

#### 5. Swarm-Specific Configuration

**Missing Entity Fields:**
```csharp
// Application.cs additions needed
public class Application
{
    // ... existing fields ...
    
    // MISSING: Swarm configuration
    public int? SwarmReplicas { get; set; }
    public string? SwarmPlacementConstraints { get; set; }  // JSON
    public string? SwarmUpdateConfig { get; set; }          // JSON
    public string? SwarmRollbackConfig { get; set; }        // JSON
    public string? SwarmMode { get; set; }                  // replicated/global
    public string? SwarmEndpointSpec { get; set; }          // JSON
    public string? SwarmNetworks { get; set; }              // JSON
    public long? SwarmStopGracePeriod { get; set; }         // nanoseconds
}

// Server.cs additions needed
public class Server
{
    // ... existing fields ...
    
    // MISSING: Swarm state
    public bool IsSwarmManager { get; set; }
    public bool IsSwarmWorker { get; set; }
    public string? SwarmNodeId { get; set; }
}
```

#### 6. UI Components

**Missing Blazor Components:**
```razor
@* SwarmSettings.razor - Configure swarm deployment *@
<div class="swarm-settings">
    <h3>Docker Swarm Settings</h3>
    
    <InputNumber @bind-Value="Replicas" />
    
    <InputTextArea @bind-Value="PlacementConstraints" 
                   placeholder='["node.role==manager"]' />
    
    <InputSelect @bind-Value="UpdateOrder">
        <option value="start-first">Start First</option>
        <option value="stop-first">Stop First</option>
    </InputSelect>
    
    <InputNumber @bind-Value="UpdateParallelism" />
</div>

@* SwarmNodes.razor - View and manage nodes *@
<div class="swarm-nodes">
    @foreach (var node in Nodes)
    {
        <div class="node-card">
            <h4>@node.Description.Hostname</h4>
            <span class="role">@node.Spec.Role</span>
            <span class="status">@node.Status.State</span>
            <button @onclick="() => DrainNode(node.ID)">Drain</button>
            <button @onclick="() => ActivateNode(node.ID)">Activate</button>
        </div>
    }
</div>

@* SwarmServiceLogs.razor - Service log viewer *@
<div class="service-logs">
    <select @onchange="OnServiceSelected">
        @foreach (var service in Services)
        {
            <option value="@service.ID">@service.Spec.Name</option>
        }
    </select>
    <pre>@LogContent</pre>
</div>
```

#### 7. Deployment Strategy

**Missing Logic:**
```csharp
// IDeploymentService.cs
public interface ISwarmDeploymentService
{
    // MISSING: Deploy as swarm service
    Task<string> DeployAsService(Application app, string imageTag);
    
    // MISSING: Update existing service (rolling update)
    Task UpdateService(Application app, string imageTag);
    
    // MISSING: Scale service
    Task ScaleService(string serviceId, int replicas);
    
    // MISSING: Rollback service
    Task RollbackService(string serviceId);
}

// Implementation example
public async Task<string> DeployAsService(Application app, string imageTag)
{
    var serviceParams = new ServiceCreateParameters
    {
        Service = new ServiceSpec
        {
            Name = app.Name,
            TaskTemplate = new TaskSpec
            {
                ContainerSpec = new ContainerSpec
                {
                    Image = imageTag,
                    Env = BuildEnvVars(app),
                    Mounts = BuildMounts(app)
                },
                Resources = new ResourceRequirements
                {
                    Limits = new Resources
                    {
                        MemoryBytes = app.MemoryLimit,
                        NanoCPUs = app.CpuLimit
                    }
                },
                Placement = new Placement
                {
                    Constraints = ParseConstraints(app.SwarmPlacementConstraints)
                }
            },
            Mode = new ServiceMode
            {
                Replicated = new ReplicatedService
                {
                    Replicas = app.SwarmReplicas ?? 1
                }
            },
            UpdateConfig = new UpdateConfig
            {
                Parallelism = 1,
                Order = "start-first",
                FailureAction = "rollback"
            },
            EndpointSpec = new EndpointSpec
            {
                Ports = BuildPortConfigs(app)
            },
            Networks = new[] { new NetworkAttachmentConfig { Target = "dokploy-network" } }
        }
    };
    
    var response = await _dockerClient.Swarm.CreateServiceAsync(serviceParams);
    return response.ID;
}
```

#### 8. Monitoring & Health Checks

**Missing:**
```csharp
// Service health monitoring
public async Task<ServiceHealth> GetServiceHealth(string serviceId)
{
    var service = await _dockerClient.Swarm.InspectServiceAsync(serviceId);
    var tasks = await _dockerClient.Swarm.ListTasksAsync(new TasksListParameters
    {
        Filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["service"] = new Dictionary<string, bool> { [serviceId] = true }
        }
    });
    
    return new ServiceHealth
    {
        DesiredReplicas = service.Spec.Mode.Replicated.Replicas,
        RunningReplicas = tasks.Count(t => t.Status.State == "running"),
        FailedTasks = tasks.Where(t => t.Status.State == "failed").ToList()
    };
}

// Aggregate service status
public async Task<string> GetServiceStatus(string serviceId)
{
    var health = await GetServiceHealth(serviceId);
    if (health.RunningReplicas == 0) return "down";
    if (health.RunningReplicas < health.DesiredReplicas) return "degraded";
    if (health.FailedTasks.Any()) return "unhealthy";
    return "running";
}
```

---

## 4. Implementation Priority

### Phase 1: Core Swarm API (HIGH PRIORITY)
1. ✅ Swarm detection (`IsSwarmActive()`)
2. ✅ Service CRUD operations
3. ✅ Node listing and inspection
4. ✅ Service logs streaming
5. ✅ Basic deployment to swarm

**Effort:** 2-3 days
**Files:**
- `IDockerService.cs` - Add swarm methods
- `DockerService.cs` - Implement swarm operations
- `Application.cs` - Add swarm fields

### Phase 2: Deployment Integration (HIGH PRIORITY)
1. ✅ Update deployment service to detect swarm
2. ✅ Implement service-based deployment
3. ✅ Rolling update strategy
4. ✅ Rollback capability
5. ✅ Health check integration

**Effort:** 3-4 days
**Files:**
- `IDeploymentService.cs` - Add swarm deployment
- `DeploymentService.cs` - Implement swarm deployment
- `ApplicationsController.cs` - Add swarm endpoints

### Phase 3: UI & Configuration (MEDIUM PRIORITY)
1. ✅ Swarm settings component
2. ✅ Node management UI
3. ✅ Service logs viewer (swarm mode)
4. ✅ Replica scaling UI
5. ✅ Placement constraints editor

**Effort:** 4-5 days
**Files:**
- `SwarmSettings.razor` - New component
- `SwarmNodes.razor` - New component
- `ApplicationDetails.razor` - Add swarm section
- `modern-design.css` - Swarm-specific styles

### Phase 4: Stack Support (MEDIUM PRIORITY)
1. ✅ Docker Compose parser
2. ✅ Stack deployment via CLI
3. ✅ Stack listing and removal
4. ✅ Multi-service coordination

**Effort:** 3-4 days
**Files:**
- `IStackService.cs` - New interface
- `StackService.cs` - Implementation
- `ComposeParser.cs` - YAML parsing

### Phase 5: Advanced Features (LOW PRIORITY)
1. ⚠️ Service scaling automation
2. ⚠️ Custom update strategies
3. ⚠️ Network topology visualization
4. ⚠️ Secret management
5. ⚠️ Config management

**Effort:** 5-7 days

---

## 5. Code Examples for Implementation

### Example 1: Swarm Service Deployment
```csharp
// Services/SwarmDeploymentService.cs
public class SwarmDeploymentService : ISwarmDeploymentService
{
    private readonly IDockerService _dockerService;
    private readonly ILogger<SwarmDeploymentService> _logger;
    
    public async Task<DeploymentResult> DeployToSwarm(Application app, string image)
    {
        try
        {
            // Check if service already exists
            var existingServices = await _dockerService.ListServicesAsync();
            var existingService = existingServices.FirstOrDefault(s => 
                s.Spec.Name == app.Name);
            
            if (existingService != null)
            {
                // Update existing service (rolling update)
                return await UpdateSwarmService(app, image, existingService.ID);
            }
            else
            {
                // Create new service
                return await CreateSwarmService(app, image);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy {AppName} to swarm", app.Name);
            throw;
        }
    }
    
    private async Task<DeploymentResult> CreateSwarmService(Application app, string image)
    {
        var spec = new ServiceSpec
        {
            Name = app.Name,
            Labels = new Dictionary<string, string>
            {
                ["hostcraft.app.id"] = app.Id.ToString(),
                ["hostcraft.app.name"] = app.Name,
                ["com.docker.stack.namespace"] = app.Project.Name
            },
            TaskTemplate = new TaskSpec
            {
                ContainerSpec = new ContainerSpec
                {
                    Image = image,
                    Env = BuildEnvironmentVariables(app),
                    Mounts = BuildMounts(app),
                    Labels = new Dictionary<string, string>
                    {
                        ["hostcraft.container"] = app.Name
                    }
                },
                Resources = new ResourceRequirements
                {
                    Limits = new Resources
                    {
                        MemoryBytes = app.MemoryLimit,
                        NanoCPUs = ConvertCPUToNano(app.CpuLimit)
                    },
                    Reservations = new Resources
                    {
                        MemoryBytes = app.MemoryReservation,
                        NanoCPUs = ConvertCPUToNano(app.CpuReservation)
                    }
                },
                Placement = BuildPlacement(app),
                RestartPolicy = new RestartPolicy
                {
                    Condition = "any",
                    Delay = 5000000000, // 5 seconds
                    MaxAttempts = 3
                }
            },
            Mode = BuildServiceMode(app),
            UpdateConfig = BuildUpdateConfig(app),
            RollbackConfig = BuildRollbackConfig(app),
            EndpointSpec = BuildEndpointSpec(app),
            Networks = new[]
            {
                new NetworkAttachmentConfig { Target = "hostcraft-network" }
            }
        };
        
        var serviceId = await _dockerService.CreateServiceAsync(spec);
        
        return new DeploymentResult
        {
            Success = true,
            ServiceId = serviceId,
            Message = $"Service {app.Name} created successfully"
        };
    }
    
    private ServiceMode BuildServiceMode(Application app)
    {
        if (string.IsNullOrEmpty(app.SwarmMode) || app.SwarmMode == "replicated")
        {
            return new ServiceMode
            {
                Replicated = new ReplicatedService
                {
                    Replicas = (ulong)(app.SwarmReplicas ?? 1)
                }
            };
        }
        else // global
        {
            return new ServiceMode
            {
                Global = new GlobalService()
            };
        }
    }
    
    private Placement BuildPlacement(Application app)
    {
        var placement = new Placement();
        
        if (!string.IsNullOrEmpty(app.SwarmPlacementConstraints))
        {
            try
            {
                var constraints = JsonSerializer.Deserialize<List<string>>(
                    app.SwarmPlacementConstraints);
                placement.Constraints = constraints;
            }
            catch
            {
                _logger.LogWarning("Invalid placement constraints for {AppName}", 
                    app.Name);
            }
        }
        
        return placement;
    }
    
    private UpdateConfig BuildUpdateConfig(Application app)
    {
        // Default config with overrides from app settings
        var config = new UpdateConfig
        {
            Parallelism = 1,
            Delay = 10000000000, // 10 seconds
            FailureAction = "rollback",
            Order = "start-first",
            MaxFailureRatio = 0.1f
        };
        
        if (!string.IsNullOrEmpty(app.SwarmUpdateConfig))
        {
            try
            {
                var custom = JsonSerializer.Deserialize<UpdateConfig>(
                    app.SwarmUpdateConfig);
                return custom;
            }
            catch
            {
                _logger.LogWarning("Invalid update config for {AppName}, using defaults", 
                    app.Name);
            }
        }
        
        return config;
    }
}
```

### Example 2: Swarm Node Management UI
```razor
@* Components/Pages/SwarmNodes.razor *@
@page "/swarm/nodes"
@inject IDockerService DockerService
@inject IServerService ServerService

<PageTitle>Docker Swarm Nodes</PageTitle>

<div class="swarm-nodes-page">
    <div class="page-header">
        <h1>Docker Swarm Nodes</h1>
        <button class="btn btn-primary" @onclick="RefreshNodes">
            <i class="icon-refresh"></i> Refresh
        </button>
    </div>
    
    @if (loading)
    {
        <div class="loading">Loading nodes...</div>
    }
    else if (nodes == null || !nodes.Any())
    {
        <div class="empty-state">
            <p>No swarm nodes found. Initialize Docker Swarm on your server first.</p>
            <button class="btn btn-primary" @onclick="InitializeSwarm">
                Initialize Swarm
            </button>
        </div>
    }
    else
    {
        <div class="nodes-grid">
            @foreach (var node in nodes)
            {
                <div class="node-card @GetNodeStatusClass(node)">
                    <div class="node-header">
                        <h3>@node.Description.Hostname</h3>
                        <span class="node-role badge badge-@node.Spec.Role">
                            @node.Spec.Role
                        </span>
                    </div>
                    
                    <div class="node-info">
                        <div class="info-item">
                            <span class="label">ID:</span>
                            <span class="value">@node.ID.Substring(0, 12)</span>
                        </div>
                        <div class="info-item">
                            <span class="label">Status:</span>
                            <span class="value status-@node.Status.State">
                                @node.Status.State
                            </span>
                        </div>
                        <div class="info-item">
                            <span class="label">Availability:</span>
                            <span class="value">@node.Spec.Availability</span>
                        </div>
                        <div class="info-item">
                            <span class="label">Address:</span>
                            <span class="value">@node.Status.Addr</span>
                        </div>
                        <div class="info-item">
                            <span class="label">OS:</span>
                            <span class="value">
                                @node.Description.Platform.OS 
                                (@node.Description.Platform.Architecture)
                            </span>
                        </div>
                        <div class="info-item">
                            <span class="label">Engine:</span>
                            <span class="value">@node.Description.Engine.EngineVersion</span>
                        </div>
                        <div class="info-item">
                            <span class="label">Resources:</span>
                            <span class="value">
                                @FormatCPU(node.Description.Resources.NanoCPUs) CPUs, 
                                @FormatMemory(node.Description.Resources.MemoryBytes) RAM
                            </span>
                        </div>
                    </div>
                    
                    @if (node.ManagerStatus != null)
                    {
                        <div class="manager-info">
                            <span class="badge badge-manager">
                                @(node.ManagerStatus.Leader ? "Leader" : "Manager")
                            </span>
                            <span class="manager-addr">@node.ManagerStatus.Addr</span>
                        </div>
                    }
                    
                    <div class="node-actions">
                        @if (node.Spec.Availability == "active")
                        {
                            <button class="btn btn-sm btn-secondary" 
                                    @onclick="() => DrainNode(node.ID)">
                                Drain
                            </button>
                        }
                        else if (node.Spec.Availability == "drain")
                        {
                            <button class="btn btn-sm btn-primary" 
                                    @onclick="() => ActivateNode(node.ID)">
                                Activate
                            </button>
                        }
                        
                        @if (node.Spec.Role == "worker")
                        {
                            <button class="btn btn-sm btn-primary" 
                                    @onclick="() => PromoteNode(node.ID)">
                                Promote to Manager
                            </button>
                        }
                        else if (node.Spec.Role == "manager" && !node.ManagerStatus.Leader)
                        {
                            <button class="btn btn-sm btn-secondary" 
                                    @onclick="() => DemoteNode(node.ID)">
                                Demote to Worker
                            </button>
                        }
                        
                        @if (node.Status.State != "ready")
                        {
                            <button class="btn btn-sm btn-danger" 
                                    @onclick="() => RemoveNode(node.ID)">
                                Remove
                            </button>
                        }
                    </div>
                </div>
            }
        </div>
        
        <div class="add-node-section">
            <h2>Add Nodes to Swarm</h2>
            <div class="join-commands">
                <div class="command-block">
                    <h3>Worker Node</h3>
                    <pre>@workerJoinCommand</pre>
                    <button class="btn btn-sm btn-secondary" 
                            @onclick="() => CopyToClipboard(workerJoinCommand)">
                        Copy
                    </button>
                </div>
                <div class="command-block">
                    <h3>Manager Node</h3>
                    <pre>@managerJoinCommand</pre>
                    <button class="btn btn-sm btn-secondary" 
                            @onclick="() => CopyToClipboard(managerJoinCommand)">
                        Copy
                    </button>
                </div>
            </div>
        </div>
    }
</div>

@code {
    private List<NodeListResponse> nodes = new();
    private bool loading = true;
    private string workerJoinCommand = "";
    private string managerJoinCommand = "";
    
    protected override async Task OnInitializedAsync()
    {
        await RefreshNodes();
        await LoadJoinCommands();
    }
    
    private async Task RefreshNodes()
    {
        loading = true;
        try
        {
            nodes = (await DockerService.ListNodesAsync()).ToList();
        }
        catch (Exception ex)
        {
            // Show error toast
        }
        finally
        {
            loading = false;
        }
    }
    
    private async Task LoadJoinCommands()
    {
        try
        {
            var (worker, manager) = await DockerService.GetJoinTokensAsync();
            var serverIp = await ServerService.GetLocalServerIpAsync();
            workerJoinCommand = $"docker swarm join --token {worker} {serverIp}:2377";
            managerJoinCommand = $"docker swarm join --token {manager} {serverIp}:2377";
        }
        catch (Exception ex)
        {
            // Show error toast
        }
    }
    
    private async Task DrainNode(string nodeId)
    {
        // Set node availability to drain
        await DockerService.UpdateNodeAsync(nodeId, new NodeUpdateParameters
        {
            Spec = new NodeSpec { Availability = "drain" }
        });
        await RefreshNodes();
    }
    
    private async Task ActivateNode(string nodeId)
    {
        // Set node availability to active
        await DockerService.UpdateNodeAsync(nodeId, new NodeUpdateParameters
        {
            Spec = new NodeSpec { Availability = "active" }
        });
        await RefreshNodes();
    }
    
    private async Task PromoteNode(string nodeId)
    {
        await DockerService.UpdateNodeAsync(nodeId, new NodeUpdateParameters
        {
            Spec = new NodeSpec { Role = "manager" }
        });
        await RefreshNodes();
    }
    
    private async Task DemoteNode(string nodeId)
    {
        await DockerService.UpdateNodeAsync(nodeId, new NodeUpdateParameters
        {
            Spec = new NodeSpec { Role = "worker" }
        });
        await RefreshNodes();
    }
    
    private async Task RemoveNode(string nodeId)
    {
        if (await ConfirmDelete())
        {
            await DockerService.RemoveNodeAsync(nodeId, force: true);
            await RefreshNodes();
        }
    }
    
    private string GetNodeStatusClass(NodeListResponse node)
    {
        if (node.Status.State != "ready") return "node-down";
        if (node.Spec.Availability == "drain") return "node-drain";
        return "node-ready";
    }
    
    private string FormatCPU(long nanoCPUs) => $"{nanoCPUs / 1_000_000_000.0:F2}";
    private string FormatMemory(long bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
}
```

---

## 6. Recommended Implementation Steps

1. **Start with Core API** (Week 1)
   - Implement swarm detection in `DockerService`
   - Add service CRUD methods
   - Test basic service creation/update/removal

2. **Update Data Models** (Week 1)
   - Add swarm fields to `Application.cs`
   - Create migration for database changes
   - Update API controllers

3. **Build Deployment Logic** (Week 2)
   - Create `SwarmDeploymentService`
   - Implement rolling updates
   - Add rollback capability
   - Integrate with existing deployment pipeline

4. **Create UI Components** (Week 2-3)
   - `SwarmSettings.razor` for configuration
   - `SwarmNodes.razor` for node management
   - Update `ApplicationDetails.razor`
   - Add swarm-specific styles to `modern-design.css`

5. **Testing & Refinement** (Week 3)
   - Test multi-node deployments
   - Verify rolling updates work correctly
   - Test failover scenarios
   - Performance testing with multiple replicas

6. **Documentation** (Week 4)
   - User guide for swarm features
   - API documentation
   - Architecture diagrams
   - Troubleshooting guide

---

## 7. Summary

**HostCraft vs Coolify/Dokploy:**

| Feature | HostCraft | Coolify | Dokploy |
|---------|-----------|---------|---------|
| Swarm Detection | ❌ | ✅ | ✅ |
| Service Management | ❌ | ✅ | ✅ |
| Node Management | ❌ | ✅ | ✅ |
| Rolling Updates | ❌ | ✅ | ✅ |
| Stack Deployment | ❌ | ✅ | ✅ |
| Placement Constraints | ❌ | ✅ | ✅ |
| Service Logs | ❌ | ✅ | ✅ |
| Replica Scaling | ❌ | ✅ | ✅ |
| Health Monitoring | ⚠️ Basic | ✅ Full | ✅ Full |
| Swarm UI | ❌ | ✅ | ✅ Advanced |

**Estimated Effort to Reach Parity:** 3-4 weeks full-time development

**Priority Order:**
1. ✅ Core swarm API integration
2. ✅ Service-based deployment
3. ✅ Node management
4. ✅ Swarm configuration UI
5. ⚠️ Stack deployment (docker-compose)
6. ⚠️ Advanced features (secrets, configs)
