# HostCraft

A self-hosted Platform-as-a-Service (PaaS) for managing Docker Swarm deployments with a modern Blazor UI.

## Getting Started

### Quick Install

```bash
# Download the install script
curl -fsSL -o install.sh https://github.com/gokartn/hostcraft/releases/download/v0.0.1-alpha/install.sh

# Review it (always review scripts!)
less install.sh

# Run installation
chmod +x install.sh
./install.sh
```

### From Source

```bash
git clone https://github.com/gokartn/hostcraft.git
cd hostcraft
chmod +x install.sh
./install.sh
```

Access the UI at `http://localhost:5000`

### First Steps

After installation, configure your domain and SSL:

1. **Navigate to Settings** (`⚙️ Settings` in the sidebar)
2. **Configure Domain & SSL**:
   - Enter your **HostCraft Web Domain** (e.g., `hostcraft.yourdomain.com`)
   - Enter your **HostCraft API Domain** (can be the same as Web Domain)
   - Enable **HTTPS** and provide your **Let's Encrypt email**
3. **Save Configuration**

Your domain's DNS A record should point to your server's IP address.

## Uninstallation

HostCraft provides two cleanup options:

### Quick Uninstall (Keeps Data)

Removes containers and networks but preserves volumes and data:

```bash
./uninstall.sh
```

### Complete Removal (Deletes Everything)

⚠️ **WARNING:** This deletes ALL data including databases, backups, and configuration!

```bash
./cleanup.sh
```

The complete cleanup script will:
- Remove all Docker containers, services, and stacks
- Delete all volumes (database, backups, etc.)
- Remove networks and images
- Optionally leave/destroy Docker Swarm
- Clean up application data directories
- Remove configuration files

## Current Status

**Version:** 0.0.1-alpha

⚠️ **Alpha Release** - Not production-ready. Expect bugs and breaking changes.

## License

TBD

---
