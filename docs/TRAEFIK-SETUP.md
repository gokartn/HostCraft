# Traefik Setup Guide for HostCraft

This guide explains how Traefik reverse proxy works with HostCraft to enable domain routing and automatic SSL certificates.

## Automatic Setup (Recommended)

**Traefik is automatically set up during installation!**

When running the install script, you'll be prompted:

```bash
./install.sh
```

The installer will ask if you want to:
1. Initialize Docker Swarm
2. Set up Traefik reverse proxy
3. Configure Let's Encrypt email for SSL certificates
4. Optionally set up Traefik dashboard domain

Simply answer "yes" and provide your email when prompted. The installer handles everything automatically.

## Manual Setup (Advanced)

If you skipped Traefik during installation or need to set it up separately:

### Prerequisites

- Docker Swarm initialized
- Server accessible on ports 80 and 443
- Domain DNS records pointing to your server

### 1. Create Traefik Network

```bash
docker network create --driver=overlay traefik-public
```

### 2. Deploy Traefik Stack

Create a `traefik-compose.yml` file:

```yaml
version: '3.8'

services:
  traefik:
    image: traefik:v2.11
    command:
      # Enable Docker provider
      - --providers.docker=true
      - --providers.docker.swarmMode=true
      - --providers.docker.exposedByDefault=false
      - --providers.docker.network=traefik-public
      
      # Entrypoints (ports)
      - --entrypoints.web.address=:80
      - --entrypoints.websecure.address=:443
      
      # Enable dashboard (optional, secure in production!)
      - --api.dashboard=true
      
      # Let's Encrypt configuration
      - --certificatesresolvers.letsencrypt.acme.email=your-email@example.com
      - --certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json
      - --certificatesresolvers.letsencrypt.acme.httpchallenge=true
      - --certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web
      
      # Logging
      - --log.level=INFO
      - --accesslog=true
    
    ports:
      - "80:80"
      - "443:443"
      - "8080:8080"  # Dashboard (remove in production or secure properly)
    
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - traefik-certificates:/letsencrypt
    
    networks:
      - traefik-public
    
    deploy:
      mode: replicated
      replicas: 1
      placement:
        constraints:
          - node.role == manager
      labels:
        # Enable Traefik dashboard
        - "traefik.enable=true"
        - "traefik.http.routers.traefik-dashboard.rule=Host(`traefik.yourdomain.com`)"
        - "traefik.http.routers.traefik-dashboard.entrypoints=websecure"
        - "traefik.http.routers.traefik-dashboard.tls.certresolver=letsencrypt"
        - "traefik.http.routers.traefik-dashboard.service=api@internal"
        - "traefik.http.services.traefik-dashboard.loadbalancer.server.port=8080"

volumes:
  traefik-certificates:
    driver: local

networks:
  traefik-public:
    external: true
```

### 3. Deploy Traefik

```bash
# Update the email in traefik-compose.yml first!
docker stack deploy -c traefik-compose.yml traefik
```

### 4. Connect HostCraft to Traefik

```bash
# Connect HostCraft web service to Traefik network
docker service update --network-add traefik-public hostcraft_hostcraft-web
```

### 5. Verify Deployment

```bash
 5. Configure HostCraft Services

Update your HostCraft services to connect to the Traefik network:

```bash
# Connect HostCraft web service to Traefik network
docker service update --network-add traefik-public hostcraft_hostcraft-web
```

## Security Notes

🔒 **IMPORTANT SECURITY CONSIDERATIONS:**

1. **Never commit sensitive data** to the repository:
   - Server IPs
   - Domain names
   - Email addresses
   - API keys or passwords

2. **Secure the Traefik Dashboard:**
   - Use authentication (BasicAuth or OAuth)
   - Restrict access by IP
   - Or disable it entirely in production

3. **Use Environment Variables:**
   - Store sensitive configuration in `.env` files (gitignored)
   - Use Docker secrets for production deployments

## Configuration for HostCraft Domain

Once Traefik is running:

1. Go to Settings in HostCraft UI
2. Enter your domain (e.g., `hostcraft.example.com`)
3. Enable HTTPS and provide Let's Encrypt email
4. Save configuration

HostCraft will automatically apply Traefik labels and restart the service to apply changes.

## Troubleshooting

### Connection Refused

If you get "Connection Refused":
- Verify Traefik is running: `docker service ls | grep traefik`
- Check if service is on traefik-public network: `docker service inspect hostcraft_hostcraft-web | grep traefik-public`
- Verify DNS: `nslookup your-domain.com`

### Certificate Issues

- Check Traefik logs: `docker service logs traefik_traefik`
- Verify email address in Traefik configuration
- Ensure port 80 is accessible (Let's Encrypt uses HTTP challenge)

### Service Not Found

- Ensure the service has `traefik.enable=true` label
- Verify the service is on the `traefik-public` network
- Check service logs for connection errors

## Example: Minimal Production Setup

```bash
# 1. Create network
docker network create --driver=overlay traefik-public

# 2. Deploy Traefik (with your email)
# ... edit aComplete Manual Setup

```bash
# 1. Create network
docker network create --driver=overlay traefik-public

# 2. Deploy Traefik (edit traefik-compose.yml with your email first)
docker stack deploy -c traefik-compose.yml traefik

# 3. Connect HostCraft
docker service update --network-add traefik-public hostcraft_hostcraft-web

# 4. Configure via UI
# Go to Settings → HostCraft Domain & SSL
```

## Why Use the Automatic Installer?

The `install.sh` script:
- ✅ Handles all configuration automatically
- ✅ Prompts for required information interactively
- ✅ Creates networks and deploys Traefik in one step
- ✅ Connects HostCraft services automatically
- ✅ No manual file editing required
- ✅ Fewer chances for configuration errors

**Recommendation:** Always use `./install.sh` for initial setup. Only use manual setup if you need custom Traefik