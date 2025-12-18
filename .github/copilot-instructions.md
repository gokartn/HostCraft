# GitHub Copilot Instructions for HostCraft

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
3. Show the user the full terminal output
4. Count and report: "X projects succeeded, Y errors, Z warnings"
5. If ANY project fails, fix it and rebuild ALL projects again

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

**Take the extra 30 seconds to verify. Always.**
