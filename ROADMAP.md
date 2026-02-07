# HostCraft Development Roadmap

**Goal:** Complete HostCraft's feature set to be a production-ready, enterprise-grade Platform-as-a-Service with **HA/DR as the default**.

**Last Updated:** 2026-02-01
**Version:** 0.0.1-alpha
**Status:** Active Development

---

## Our Unique Value Proposition

**HostCraft = Production-grade High Availability & Disaster Recovery by default**

- 3-manager Docker Swarm clusters as the recommended setup
- Multi-node deployments with automatic failover
- Geographic distribution for true disaster recovery
- Built for production from day one, not an afterthought

---

## Current Status Summary

### Completed
- ✅ Core Docker Swarm integration
- ✅ GitHub OAuth and webhooks
- ✅ 3-Manager HA setup wizard
- ✅ Real-time cluster monitoring with node metrics
- ✅ Basic container/service deployment
- ✅ Git-based deployments (Dockerfile only)
- ✅ Traefik HA proxy configuration
- ✅ **Phase 1 Security Hardening (P0 Critical)** 🔒

### Security Phase 1 Complete ✅
- ✅ Firewall configuration (UFW)
- ✅ Docker daemon hardening
- ✅ Secure Swarm initialization (localhost binding)
- ✅ Docker Registry authentication

---

## Priority System

| Priority | Timeline | Meaning |
|----------|----------|---------|
| **P0 - BLOCKING** | This Week | Critical security or core functionality |
| **P1 - CRITICAL** | 2-4 Weeks | Needed for MVP feature parity |
| **P2 - HIGH** | 1-2 Months | Competitive parity features |
| **P3 - MEDIUM** | 2-3 Months | Nice-to-have, enhances UX |
| **P4 - LOW** | 3+ Months | Future enhancements |

---

## NEXT UP: Security Phase 2 (P1 - High Priority)

**Goal:** Production-ready security hardening
**Priority:** P1 - Critical
**Timeline:** 2-3 days

### Tasks

#### 1. SSH Hardening ⏰ 1-2 hours
- Disable password authentication (keys only)
- Disable root password login
- Strong ciphers only
- Optional: Change SSH port from 22

**Why Critical:**
- Prevent brute force attacks
- Industry security best practice
- Required for production deployments

**Acceptance Criteria:**
- [ ] SSH configured for key-only authentication
- [ ] Strong ciphers enforced
- [ ] Configuration tested and working

---

#### 2. Fail2Ban Installation ⏰ 1 hour
- Auto-ban after 3 failed SSH attempts
- Protect against brute force attacks
- Email notifications on bans (optional)

**Why Critical:**
- Essential defense against automated attacks
- Reduces attack surface
- Industry standard for production servers

**Acceptance Criteria:**
- [ ] Fail2Ban installed and configured
- [ ] SSH jail enabled
- [ ] Ban rules tested

---

#### 3. Automatic Security Updates ⏰ 1 hour
- Configure unattended-upgrades
- Security patches only (no breaking changes)
- Auto-cleanup old kernels

**Why Critical:**
- Keep system patched against vulnerabilities
- Reduces manual maintenance
- Production security requirement

**Acceptance Criteria:**
- [ ] Unattended-upgrades configured
- [ ] Security-only updates enabled
- [ ] Auto-cleanup configured

---

**Security Phase 2 Total Time:** 3-4 hours

**See:** `docs/SECURITY_HARDENING.md` for detailed implementation instructions

---

## Phase 3A: Database & Buildpacks (4-6 weeks)

**Goal:** Comprehensive one-click database deployment and multi-buildpack support
**Priority:** P0 - Must Have for MVP

### Task P3A-T1: Database Template System ⏰ 12-16 hours
- One-click deployment for PostgreSQL, MySQL, MongoDB, Redis, MariaDB, etc.
- Automatic volume creation and persistence
- Secure credential generation
- Built-in backup strategies

**Acceptance Criteria:**
- [ ] Deploy PostgreSQL with one click
- [ ] Persistent volumes created automatically
- [ ] Connection strings provided
- [ ] 8+ database templates available

---

### Task P3A-T2: Nixpacks Integration ⏰ 10-14 hours
- Auto-detect application type (Node.js, Python, Go, Ruby, etc.)
- Build without manual Dockerfile
- Support 10+ frameworks

**Acceptance Criteria:**
- [ ] Deploy Next.js app without Dockerfile
- [ ] Deploy Django app without Dockerfile
- [ ] Auto-detection working for common frameworks

---

### Task P3A-T3: Static Site Detection ⏰ 8-10 hours
- Auto-detect Hugo, Jekyll, Gatsby, Vite, Next.js static
- Build and serve with Nginx
- SPA routing support

**Acceptance Criteria:**
- [ ] Hugo blog deploys and serves correctly
- [ ] Gatsby site with routing works
- [ ] Fast build times

---

### Task P3A-T4: Complete Docker Compose ⏰ 16-20 hours
- Full docker-compose.yml deployment
- Multi-service support
- Network and volume creation
- Per-service monitoring

**Acceptance Criteria:**
- [ ] Deploy WordPress + MySQL from compose
- [ ] Services communicate correctly
- [ ] Volumes persist data

---

**Phase 3A Total:** 46-60 hours (1.5-2 weeks)

---

## Phase 3B: Operational Essentials (2-3 weeks)

**Goal:** Production-ready deployment operations
**Priority:** P0 - Must Have for MVP

### Task P3B-T1: Notification System ⏰ 12-14 hours
- Discord, Slack, Email, Telegram, Webhooks
- Deployment success/failure notifications
- HA alerts (manager down)

**Acceptance Criteria:**
- [ ] Discord notifications working
- [ ] Deployment notifications sent
- [ ] Manager down alerts sent

---

### Task P3B-T2: Manual Rollback ⏰ 6-8 hours
- One-click rollback to previous deployment
- Deployment history with rollback buttons
- Audit logging

**Acceptance Criteria:**
- [ ] Rollback button visible
- [ ] Rollback deploys previous version
- [ ] History shows rollback

---

### Task P3B-T3: Build Secrets Injection ⏰ 6-8 hours
- Secure secrets during Docker build
- BuildKit integration
- NPM tokens, Maven credentials support

**Acceptance Criteria:**
- [ ] NPM private packages work
- [ ] Secrets not in final image
- [ ] Build succeeds with secrets

---

### Task P3B-T4: Resource Limits ⏰ 8-10 hours
- CPU and memory limits per container
- Resource monitoring with alerts
- Visual usage indicators

**Acceptance Criteria:**
- [ ] Memory limit enforced
- [ ] CPU throttled correctly
- [ ] Alerts sent at 90% usage

---

**Phase 3B Total:** 32-40 hours (1 week)

---

## Phase 4A: Developer Experience (3-4 weeks)

**Goal:** Best-in-class developer experience
**Priority:** P1 - Essential for Production Use

### Task P4A-T1: Container Terminal ⏰ 10-12 hours
- Web-based SSH to containers
- xterm.js integration
- Multi-replica support

---

### Task P4A-T2: Complete Preview Deployments ⏰ 10-12 hours
- PreviewUrlTemplate usage
- MaxPreviewDeployments enforcement
- GitHub PR comments
- Traefik routing

---

### Task P4A-T3: S3 Backup Integration ⏰ 12-14 hours
- Automated backups to S3-compatible storage
- Database-specific backup commands
- One-click restore
- Retention policies

---

### Task P4A-T4: Deployment Queue Management ⏰ 8-10 hours
- Cancel queued/building deployments
- Retry failed deployments
- Real-time queue visibility

---

**Phase 4A Total:** 40-48 hours (1-1.5 weeks)

---

## Phase 4B: Scale & Collaboration (3-4 weeks)

**Goal:** Multi-user and enterprise features
**Priority:** P2 - Nice to Have

### Task P4B-T1: Git Provider Expansion ⏰ 12-14 hours
- GitLab OAuth and webhooks
- Bitbucket OAuth and webhooks
- Support for self-hosted Git

---

### Task P4B-T2: Team & RBAC ⏰ 16-20 hours
- Organizations and teams
- Role-based access control
- Team invitations
- Permission enforcement

---

### Task P4B-T3: Prometheus + Grafana ⏰ 8-10 hours
- Prometheus metrics export
- Custom application metrics
- Grafana dashboard templates

---

### Task P4B-T4: Custom SSL Certificates ⏰ 6-8 hours
- Upload custom SSL certificates
- Traefik integration
- Expiration warnings

---

**Phase 4B Total:** 42-52 hours (1.5-2 weeks)

---

## Summary Timeline

| Phase | Duration | Priority | Status |
|-------|----------|----------|--------|
| **Security Phase 1** | 1 day | P0 | ✅ Complete |
| **Security Phase 2** | 3-4 hours | P1 | ⏰ NEXT |
| **Phase 3A** | 1.5-2 weeks | P0 | Pending |
| **Phase 3B** | 1 week | P0 | Pending |
| **Phase 4A** | 1-1.5 weeks | P1 | Pending |
| **Phase 4B** | 1.5-2 weeks | P2 | Pending |

**Total Estimated Timeline:** 5-7 weeks to production-ready MVP (excluding Security Phase 2)

---

## Feature Completeness Target

After all phases complete:

| Feature Category | Status Goal |
|------------------|-------------|
| **High Availability** | ✅ Production-grade HA/DR |
| **Security** | ✅ Hardened (Phase 1 & 2) |
| **Database Management** | ✅ One-click templates |
| **Build Systems** | ✅ Nixpacks auto-detection |
| **Deployment** | ✅ Compose, rollback, preview |
| **Notifications** | ✅ Multi-channel alerts |
| **Observability** | ✅ Terminal, metrics, monitoring |
| **Backups** | ✅ S3 automated backups |
| **Collaboration** | ✅ Teams & RBAC |

---

## Next Actions

**Immediate (This Week):**
1. ⏰ **Security Phase 2** - SSH hardening, Fail2Ban, auto-updates
2. Begin Phase 3A planning

**This Month:**
- Complete Phase 3A (Database templates, Nixpacks, Compose)
- Complete Phase 3B (Notifications, Rollback, Secrets, Limits)

---

**HostCraft's Competitive Advantage:**
- ✅ Production-grade HA/DR from day one
- ✅ Enterprise security by default
- ✅ Docker Swarm native (mature orchestration)
- ✅ Self-hosted with no vendor lock-in

---

*This is a living document. Update as implementation progresses.*
