# HostCraft HA/DR Architecture

## Overview

HostCraft provides enterprise-grade High Availability (HA) and Disaster Recovery (DR) capabilities for self-hosted deployments, ensuring your applications stay online even during infrastructure failures.

## 🎯 Design Principles

1. **Swarm-Native HA** - Leverages Docker Swarm's built-in orchestration for automatic failover
2. **Multi-Region Support** - Deploy across datacenters/cloud providers for geographic redundancy
3. **Automated Recovery** - Self-healing with intelligent health monitoring and auto-restart
4. **Zero-Downtime Updates** - Rolling deployments with automatic rollback on failure
5. **Data Resilience** - Automated backups with S3-compatible storage

## 🏗️ Architecture Components

### 1. Swarm Cluster Quorum

**Manager Nodes (Recommended: 3, 5, or 7)**
- Maintains Raft consensus for cluster state
- Automatic leader election on failure
- Requires (N/2)+1 healthy managers for quorum

```
Manager Quorum Table:
┌──────────┬─────────┬──────────────────┐
│ Managers │ Quorum  │ Failure Tolerance│
├──────────┼─────────┼──────────────────┤
│    1     │    1    │        0         │
│    3     │    2    │        1         │
│    5     │    3    │        2         │
│    7     │    4    │        3         │
└──────────┴─────────┴──────────────────┘
```

**Worker Nodes (Scalable)**
- Execute application workloads
- Can be added/removed dynamically
- Failure doesn't affect cluster control plane

### 2. Multi-Region Deployment

```
┌─────────────────────────────────────────────────┐
│                 PRIMARY REGION                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐      │
│  │ Manager  │  │ Manager  │  │ Manager  │      │
│  │  Node 1  │  │  Node 2  │  │  Node 3  │      │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘      │
│       │             │             │             │
│  ┌────┴─────────────┴─────────────┴────┐        │
│  │        Overlay Network (Apps)        │        │
│  └──────────────────────────────────────┘        │
└────────────────┬────────────────────────────────┘
                 │
                 │ Replication
                 │
┌────────────────┴────────────────────────────────┐
│              SECONDARY REGION (DR)              │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐      │
│  │ Manager  │  │ Manager  │  │ Manager  │      │
│  │  Node 1  │  │  Node 2  │  │  Node 3  │      │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘      │
│       │             │             │             │
│  ┌────┴─────────────┴─────────────┴────┐        │
│  │        Overlay Network (Apps)        │        │
│  └──────────────────────────────────────┘        │
└─────────────────────────────────────────────────┘
```

### 3. Health Monitoring System

**Application Health Checks**
- HTTP/HTTPS endpoint monitoring
- Configurable intervals (default: 60s)
- Timeout detection (default: 10s)
- Failure threshold (default: 3 consecutive failures)

**Server Health Checks**
- Docker daemon connectivity
- Swarm cluster membership
- Resource availability (CPU, memory, disk)
- Network connectivity to other nodes

**Auto-Recovery Actions**
1. Service restart (first attempt)
2. Service redeployment to healthy node
3. Rollback to previous version if issue persists
4. Alert administrators if recovery fails

### 4. Backup & Restore

**Backup Types**
- **Configuration Backups** - Application metadata, environment variables, settings
- **Volume Backups** - Persistent data from Docker volumes
- **Database Backups** - Managed database snapshots
- **Full Backups** - Complete application state

**Storage Options**
- Local filesystem (fast, single-server)
- S3-compatible object storage (recommended for DR)
- Multi-region replication for geographic redundancy

**Retention Policies**
- Configurable retention periods (default: 30 days)
- Automatic pruning of expired backups
- Point-in-time recovery (PITR)

**Backup Schedule Examples**
```cron
0 2 * * *        # Daily at 2 AM
0 */6 * * *      # Every 6 hours
0 0 * * 0        # Weekly on Sunday
0 0 1 * *        # Monthly on 1st day
```

### 5. Deployment Strategies

**Rolling Updates (Default)**
```yaml
strategy:
  type: rolling
  maxUnavailable: 1        # Update one replica at a time
  maxSurge: 1              # Allow one extra replica during update
  updateDelay: 10s         # Wait 10s between updates
  rollbackOnFailure: true  # Auto-rollback if health check fails
```

**Blue-Green Deployment**
- Deploy new version alongside old
- Switch traffic after validation
- Instant rollback capability
- Zero downtime

**Canary Deployment**
- Deploy to subset of instances
- Gradually increase traffic
- Monitor metrics and health
- Rollback or promote based on results

## 🚀 HA/DR Features

### Feature Matrix

| Feature | Status | Description |
|---------|--------|-------------|
| **Multi-Manager Quorum** | ✅ Implemented | 3/5/7 manager support for fault tolerance |
| **Automatic Failover** | ✅ Implemented | Services auto-migrate to healthy nodes |
| **Health Monitoring** | ✅ Implemented | Continuous HTTP/TCP health checks |
| **Auto-Recovery** | ✅ Implemented | Restart, redeploy, or rollback on failure |
| **Backup/Restore** | ✅ Implemented | Config, volume, and full backups |
| **S3 Integration** | ✅ Implemented | Off-site backup storage |
| **Multi-Region** | ✅ Implemented | Geographic redundancy |
| **DR Failover** | ✅ Implemented | One-click region failover |
| **Rolling Updates** | ✅ Implemented | Zero-downtime deployments |
| **Auto-Rollback** | ✅ Implemented | Revert on health check failure |
| **Volume Management** | ✅ Implemented | Persistent storage with backups |
| **Uptime Tracking** | ✅ Implemented | SLA monitoring and reporting |

### Disaster Recovery Capabilities

**RTO (Recovery Time Objective)**
- Automatic failover: < 60 seconds
- Manual failover: < 5 minutes
- Full region restoration: < 30 minutes

**RPO (Recovery Point Objective)**
- Real-time replication: 0 seconds (Swarm services)
- Volume backups: Based on backup schedule (typically 6-24 hours)
- Configuration backups: Near-zero (stored in database)

**DR Testing**
- Dry-run failover testing without affecting production
- Automated DR drills on schedule
- Verification of backup integrity
- RTO/RPO compliance reporting

## 📋 Configuration Examples

### High Availability Application

```json
{
  "name": "production-api",
  "replicas": 5,
  "autoRestart": true,
  "autoRollback": true,
  "healthCheckUrl": "https://api.example.com/health",
  "healthCheckIntervalSeconds": 30,
  "healthCheckTimeoutSeconds": 10,
  "maxConsecutiveFailures": 3,
  "backupSchedule": "0 */6 * * *",
  "backupRetentionDays": 90
}
```

### Multi-Region DR Configuration

```json
{
  "regions": [
    {
      "name": "EU-West-1 (Primary)",
      "code": "eu-west-1",
      "isPrimary": true,
      "priority": 1
    },
    {
      "name": "US-East-1 (DR)",
      "code": "us-east-1",
      "isPrimary": false,
      "priority": 2
    },
    {
      "name": "Asia-Pacific-1 (DR)",
      "code": "ap-1",
      "isPrimary": false,
      "priority": 3
    }
  ]
}
```

### Swarm Cluster Setup

```bash
# Initialize primary manager
POST /api/servers/1/swarm/init
{
  "advertiseAddress": "10.0.1.10:2377",
  "managerCount": 3
}

# Add additional managers for quorum
POST /api/servers/2/swarm/join
{
  "managerAddress": "10.0.1.10:2377",
  "joinToken": "SWMTKN-1-...-manager",
  "asManager": true
}

# Add worker nodes
POST /api/servers/4/swarm/join
{
  "managerAddress": "10.0.1.10:2377",
  "joinToken": "SWMTKN-1-...-worker",
  "asManager": false
}
```

## 🔧 Operations

### Maintenance Mode

```bash
# Drain node for maintenance (migrate all services)
POST /api/servers/{id}/drain

# Perform maintenance...

# Reactivate node
POST /api/servers/{id}/activate
```

### Manual Failover

```bash
# Failover application to different region
POST /api/applications/{id}/failover
{
  "targetRegionId": 2,
  "force": false
}
```

### Backup Operations

```bash
# Create on-demand backup
POST /api/applications/{id}/backups
{
  "type": "full",
  "uploadToS3": true
}

# Restore from backup
POST /api/backups/{id}/restore
{
  "targetServerId": 5
}
```

## 📊 Monitoring & Alerts

### Key Metrics

- Cluster quorum status
- Service health scores
- Uptime percentage (SLA tracking)
- Backup success/failure rates
- Failover events
- Resource utilization per node

### Alert Triggers

- Manager node failure (quorum at risk)
- Service health check failures
- Backup failures
- Disk space warnings
- Network connectivity issues
- RTO/RPO threshold breaches

## 🎓 Best Practices

1. **Always use odd number of managers** (3, 5, or 7) for proper quorum
2. **Spread managers across failure domains** (different racks, datacenters, or clouds)
3. **Enable automated backups** with S3 storage for all stateful applications
4. **Test DR procedures regularly** using dry-run failover
5. **Monitor health check success rates** and adjust thresholds as needed
6. **Use placement constraints** to control where services run
7. **Implement proper secrets management** (Docker secrets, not environment variables)
8. **Set resource limits** to prevent single service from consuming all resources
9. **Use rolling updates** with small update delays to detect issues early
10. **Keep backup retention aligned with compliance requirements**

## 🚨 Failure Scenarios & Recovery

### Scenario 1: Single Manager Node Failure
- **Detection**: < 5 seconds
- **Impact**: None (quorum maintained with 2/3 managers)
- **Recovery**: Automatic leader election if failed node was leader
- **Action**: Add replacement manager node

### Scenario 2: Multiple Manager Node Failures (Quorum Lost)
- **Detection**: Immediate
- **Impact**: Cannot modify cluster state, existing services continue running
- **Recovery**: Restore quorum by recovering or replacing failed managers
- **Action**: Emergency manager promotion or force new cluster

### Scenario 3: Worker Node Failure
- **Detection**: < 10 seconds
- **Impact**: Services automatically rescheduled to healthy workers
- **Recovery**: Fully automatic
- **Action**: None required, add replacement worker if desired

### Scenario 4: Entire Region Failure
- **Detection**: < 30 seconds (health checks fail)
- **Impact**: Services offline in failed region
- **Recovery**: Failover to DR region (manual or automatic)
- **Action**: Initiate DR failover procedure

### Scenario 5: Network Partition (Split Brain Prevention)
- **Detection**: Immediate
- **Impact**: Raft consensus prevents split brain, minority partition becomes read-only
- **Recovery**: Automatic when network heals
- **Action**: Investigate and fix network issues

## 📖 API Documentation

See [API.md](API.md) for complete endpoint documentation.

---

*This document is part of HostCraft - Enterprise PaaS Platform*
*Last updated: December 13, 2025*
