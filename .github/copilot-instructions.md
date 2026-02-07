# GitHub Copilot Instructions for HostCraft

## 🎯 CORE MISSION: HIGH AVAILABILITY FIRST

**HostCraft is designed for production-grade High Availability and Disaster Recovery (HA/DR) by default.**

### Primary Goal: Making HA/DR Easy and Accessible

HostCraft's fundamental purpose is to make HA/DR deployment as easy as possible while maintaining clarity:

- ✅ **HA/DR is the DEFAULT** - Not an advanced option, but the recommended starting point
- ✅ **Multi-node Docker Swarm clustering** is the foundation of HostCraft
- ✅ **Easy setup with clear guidance** - Users achieve HA while understanding what's happening
- ✅ **Production-ready from day one** - Zero-downtime deployments, automatic failover
- ✅ **Configurable but opinionated** - Sensible defaults that ensure high availability

### Key Principles for All Code:

1. **HA by Default**: All recommendations, documentation, and defaults assume multi-node HA setup
2. **Easy Setup**: Users should achieve HA easily while understanding the architecture
3. **Proper Networking**: Correct bridge vs overlay network detection for Docker Swarm
4. **Service Resilience**: Automatic failover, health checks, and rolling updates built-in
5. **Data Persistence**: Database replication and backup strategies are core features

### Recommended HA Architecture (Always Present This):

**3-Manager Quorum for Production:**
- Manager Node 1 (Cloud/VPS): HostCraft API, Web, Traefik, PostgreSQL
- Manager Node 2 (Cloud/VPS): HostCraft API, Web, Traefik, PostgreSQL replica
- Manager Node 3 (On-prem/Local): HostCraft API, Web, PostgreSQL, Compute workloads

**Why 3 Managers?**
- Survives single node failure (2/3 quorum maintained)
- Raft consensus prevents split-brain
- Zero-downtime deployments and updates
- Automatic leader election on failure

### When Writing Code or Documentation:

- ❌ DON'T present standalone mode as the default
- ❌ DON'T hide HA features behind "advanced" sections
- ❌ DON'T make HA seem complicated or optional
- ✅ DO assume users want HA from the start
- ✅ DO make HA the primary path in documentation
- ✅ DO explain HA concepts clearly but accessibly
- ✅ DO provide fallback to standalone mode when explicitly needed

---

## ALPHA DEVELOPMENT GUIDELINES

**HostCraft is in ALPHA state with a single user. This means:**

### 1. **No Backward Compatibility**
- ❌ Do NOT keep deprecated code paths "for backward compatibility"
- ❌ Do NOT add migration scripts - append to InitialCreate only
- ✅ Refactor aggressively for clean, optimal architecture
- ✅ Break things if needed - deployment is reinstalled every time
- ✅ Go for INITIAL PERFECTION, not incremental evolution

### 2. **Database Migration Strategy**
- ❌ NEVER create new migration files (Add*, Update*, etc.)
- ✅ ALWAYS modify `20251218130443_InitialCreate.cs` directly
- ✅ Goal: ONE clean migration that creates perfect schema
- ✅ Update `HostCraftDbContextModelSnapshot.cs` to match
- ⚠️ This approach works because deployment is fresh installed each time

### 3. **Build Verification is MANDATORY**
- ✅ Run `dotnet build` after EVERY code change
- ✅ Show full build output proving all 6 projects compile
- ✅ Verify 0 errors before stating work is complete
- ⚠️ No exceptions - Alpha means we catch issues immediately

## CRITICAL BUILD VERIFICATION RULE

**BEFORE stating that ANY code changes are ready to push or deploy, you MUST:**

1. ✅ Run `dotnet build` from the solution root to build ALL projects
2. ✅ Show the COMPLETE build output to the user
3. ✅ Verify ALL projects build successfully with **0 errors**:
   - ✅ HostCraft.Core
   - ✅ HostCraft.Infrastructure
   - ✅ HostCraft.Api
   - ✅ HostCraft.Web
   - ✅ HostCraft.Infrastructure.Tests
   - ✅ HostCraft.Api.Tests
4. ✅ Only THEN confirm the changes are ready

**FORBIDDEN RESPONSES:**
- ❌ "It will build now"
- ❌ "This should compile"
- ❌ "The build will succeed"
- ❌ "Now it's fixed" (without proof)
- ❌ Stating success without showing actual build output

**REQUIRED RESPONSE:**
- ✅ Show actual build output proving ALL projects succeeded
- ✅ Explicitly state: "Build verified - ALL 6 projects compiled, X warnings, 0 errors"
- ✅ List any projects that failed (there should be none)

### Quality Expectations (always)
- Every change should include performance and/or code-quality improvements, not just feature wiring
- Avoid backward-compatibility shims; refactor forward (fresh reinstall workflow)
- If any database DTO/entity shape changes, update InitialCreate migration and the model snapshot directly
- Enforce separation of concerns: thin controllers, services for logic, repositories for persistence
- Keep best-practice folder structure (Controllers, Models/DTOs, Validators, Services, Repositories) and prefer one class/record per file (except truly cohesive small enums/DTOs)
- Avoid anti-patterns such as god classes, hidden static state, tight coupling to infrastructure, and inline DTOs in controllers

## CRITICAL DEPLOYMENT VALIDATION RULE

**BEFORE stating that ANY deployment changes are ready to deploy, you MUST:**

1. ✅ **Build Verification**: Run `dotnet build` and verify ALL 6 projects compile
2. ✅ **Docker Compose Validation**: Thoroughly validate `docker-compose.yml`:
   - YAML syntax correctness
   - Ubuntu 24.04 Docker Compose compatibility
   - Service dependencies (no complex `condition` properties)
   - Swarm mode configuration (deploy sections, constraints, labels)
   - Network configuration (overlay driver, attachable)
   - Volume configuration (local driver)
3. ✅ **Install Script Validation**: Verify `install.sh` functionality:
   - Environment variable generation (secure random keys/passwords)
   - Variable substitution method (`envsubst` for special characters)
   - Swarm vs Compose deployment logic
   - Service startup and health check handling
   - Database migration and user setup
4. ✅ **Dockerfile Validation**: Check all Dockerfiles:
   - Multi-stage build correctness
   - Proper base images and dependencies
   - Security configurations
   - Entrypoint and user permissions
5. ✅ **Application Configuration**: Validate Program.cs files:
   - Database connection and migration logic
   - Authentication and authorization setup
   - Service registration and dependency injection
   - Health check endpoints
6. ✅ **Environment Variables**: Verify secure handling:
   - No hardcoded credentials in code
   - Proper variable substitution in compose files
   - Secure key generation in install script

**FORBIDDEN DEPLOYMENT RESPONSES:**
- ❌ "It should deploy now"
- ❌ "The YAML looks correct"
- ❌ "The script should work"
- ❌ "Docker will handle it"
- ❌ Stating deployment readiness without comprehensive validation

**REQUIRED DEPLOYMENT RESPONSE:**
- ✅ Show build output proving compilation success
- ✅ Document all validation checks performed
- ✅ Explicitly state: "Deployment validated - docker-compose.yml syntax OK, install.sh tested, all configurations verified"
- ✅ List any issues found and how they were resolved

### Common Deployment Issues:
- **docker-compose.yml**: YAML syntax errors, incompatible `depends_on` conditions, missing Swarm deploy sections
- **install.sh**: Regex failures with special characters, incorrect variable substitution, missing environment exports
- **Dockerfiles**: Missing dependencies, incorrect base images, security issues
- **Environment Variables**: Hardcoded credentials, insecure key generation, substitution failures
- **Ubuntu 24.04**: Docker Compose version compatibility, systemd service issues

### Deployment Validation Checklist:
1. **YAML Syntax**: `docker-compose.yml` parses without errors
2. **Service Dependencies**: Compatible with target Docker Compose version
3. **Environment Variables**: `envsubst` handles special characters correctly
4. **Swarm Mode**: All services have proper deploy configurations
5. **Network Config**: Overlay networks configured correctly
6. **Volume Persistence**: Data volumes use appropriate drivers
7. **Security**: No credentials in code, secure key generation
8. **Application Startup**: Database migrations, health checks, service registration

## Code Quality Standards

### When Making Changes:
1. **Always verify compilation** - No exceptions
2. **Check for missing using statements** - Common mistake
3. **Verify dependency injection registration** - Services must be registered in Program.cs
4. **Test namespace and assembly references** - Ensure they exist

### Before Confirming Work is Done:
1. **ALWAYS** build from solution root: `dotnet build` (never build individual projects)
2. Wait for complete output from ALL 6 projects:
   - HostCraft.Core
   - HostCraft.Infrastructure
   - HostCraft.Infrastructure.Tests
   - HostCraft.Api
   - HostCraft.Api.Tests
   - HostCraft.Web (Blazor UI - critical for Docker deployment)
3. **ALWAYS** validate deployment configuration:
   - docker-compose.yml syntax and compatibility
   - install.sh environment variable handling
   - Dockerfile configurations
   - Application startup logic
4. Show the user the full terminal output for both build and deployment validation
5. Count and report: "X projects succeeded, Y errors, Z warnings"
6. If ANY project fails OR deployment validation fails, fix it and re-validate ALL checks again

## Project Structure Awareness

### HostCraft Solution Structure (ALL 6 PROJECTS MUST BUILD):
1. **HostCraft.Core** - Entities, interfaces, enums (foundation for all projects)
2. **HostCraft.Infrastructure** - Implementations (Docker, SSH, Proxy, Swarm, Stack services)
3. **HostCraft.Infrastructure.Tests** - Unit tests for Infrastructure layer
4. **HostCraft.Api** - API controllers and endpoints (runs in Docker)
5. **HostCraft.Api.Tests** - Unit tests for API controllers
6. **HostCraft.Web** - Blazor Server UI (runs in Docker, critical for deployment)

**CRITICAL**: If HostCraft.Web fails to build, Docker deployment will fail on remote server

### Common Dependencies:
- Docker.DotNet for Docker API
- SSH.NET (Renci.SshNet) for SSH operations
- Microsoft.Extensions.Logging for logging
- Entity Framework Core for database

### Dependency Injection Location:
All services are registered in:
- `src/HostCraft.Api/Program.cs` for API services
- `src/HostCraft.Web/Program.cs` for Web services

## Security Standards

**NEVER include in code or documentation:**
- Real IP addresses (use 10.0.0.1, e or "It can't build":
1. Run `dotnet build` from solution root (c:\Users\firefighter\Documents\GitHub\HostCraft)
2. Wait for complete output showing ALL 6 projects
3. Analyze the output for each project
4. Report findings clearly: "X/6 projects succeeded, Y errors, Z warnings"
5. Fix any errors (common issues: missing using statements, wrong namespaces, DI registration)
6. Re-run build to verify ALL 6 projects compile
7. Only then confirm success with proof

### Common Build Issues:
- **HostCraft.Web**: Blazor render mode errors (remove @rendermode from pages)
- **HostCraft.Api**: Missing controller dependencies, wrong DI registration
- **HostCraft.Infrastructure**: Missing Docker.DotNet, SSH.NET references
- **ALL projects**: Missing using statements, wrong namespace referencet builds":
1. Run the build command
2. Wait for complete output
3. Analyze the output
4. Report findings clearly
5. Fix any errors
6. Re-run build to verify fixes
7. Only then confirm success

## Accountability

**This file exists because of past mistakes. Respect it.**

Every time code is marked as "ready" without verification, it wastes the user's time:
- Failed Docker builds on remote server
- Wasted bandwidth pushing broken code
- Lost productivity debugging preventable errors
- Damaged trust

**Take the extra 30 seconds to verify builds AND deployments. Always.**

### Build Verification:
1. Run `dotnet build` from solution root
2. Wait for complete output showing ALL 6 projects
3. Analyze the output for each project
4. Report findings clearly: "X/6 projects succeeded, Y errors, Z warnings"
5. Fix any errors (common issues: missing using statements, wrong namespaces, DI registration)
6. Re-run build to verify ALL 6 projects compile
7. Only then confirm success with proof

### Deployment Verification:
1. Validate docker-compose.yml syntax and compatibility
2. Test install.sh environment variable handling
3. Verify Dockerfile configurations
4. Check application startup logic
5. Confirm Swarm/Compose deployment readiness
6. Only then state deployment is ready
