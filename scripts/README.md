# HostCraft Installation Scripts

Quick setup scripts for deploying HostCraft and configuring Docker Swarm.

## 🚀 Quick Start

### 1. Install Docker on Your Servers

Run on **both** your local and cloud servers:

```bash
curl -fsSL https://raw.githubusercontent.com/your-repo/HostCraft/main/scripts/install-docker.sh | sudo bash
```

Or manually:
```bash
chmod +x scripts/install-docker.sh
sudo ./scripts/install-docker.sh
```

### 2. Setup Swarm Manager

On your **cloud server** (or whichever will be the manager):

```bash
chmod +x scripts/setup-swarm-manager.sh
sudo ./scripts/setup-swarm-manager.sh
```

This will:
- Initialize Docker Swarm
- Display join tokens (save these!)
- Configure firewall rules
- Show cluster status

### 3. Setup Swarm Worker

On your **local server** (or worker node):

```bash
chmod +x scripts/setup-swarm-worker.sh
sudo ./scripts/setup-swarm-worker.sh
```

You'll be prompted for:
- Worker join token (from manager output)
- Manager address (e.g., `your-cloud-ip:2377`)

## 📋 Required Ports

### On Manager:
- **2377/tcp** - Cluster management
- **7946/tcp+udp** - Node communication
- **4789/udp** - Overlay network traffic

### On Workers:
- **7946/tcp+udp** - Node communication
- **4789/udp** - Overlay network traffic

## 🏠 Local + Cloud Setup

### Home Server (Port Forwarding)
Forward these ports on your router to your local server:
```
External → Internal
2377 → 192.168.x.x:2377
7946 → 192.168.x.x:7946
4789 → 192.168.x.x:4789
```

### Cloud Server (Security Group)
Allow inbound traffic:
- TCP 2377 from your home IP
- TCP/UDP 7946 from your home IP
- UDP 4789 from your home IP

## 🔍 Verify Setup

On the manager node:
```bash
# Check cluster status
docker node ls

# View swarm info
docker info | grep Swarm -A 10

# Test overlay network
docker network create --driver overlay test-network
docker network ls
docker network rm test-network
```

## 🐳 Deploy HostCraft

### Option 1: Docker Compose (Single Server)
```bash
cd HostCraft
docker compose up -d
```

Access:
- Web UI: http://localhost:5000
- API: http://localhost:5100

### Option 2: Docker Stack (Swarm)
Coming soon - stack deployment for multi-node setup

## 🛠️ Troubleshooting

### Check if ports are open:
```bash
# From worker to manager
nc -zv <manager-ip> 2377
nc -zv <manager-ip> 7946
nc -zu <manager-ip> 4789
```

### View swarm logs:
```bash
journalctl -u docker.service -f
```

### Leave and reset swarm:
```bash
# On worker
docker swarm leave

# On manager (force if last node)
docker swarm leave --force
```

## 📝 Notes

- **Static IP/DDNS**: If using home server as manager, set up DDNS if you don't have static IP
- **Firewall**: Scripts attempt to configure UFW automatically
- **Security**: Change default passwords in docker-compose.yml
- **SSH Keys**: HostCraft will manage servers via SSH - ensure key-based auth is set up
