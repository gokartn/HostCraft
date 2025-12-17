# GitHub Copilot Instructions for HostCraft

## CRITICAL BUILD VERIFICATION RULE

**BEFORE stating that ANY code changes are ready to push or deploy, you MUST:**

1. ✅ Run `dotnet build` on ALL affected projects
2. ✅ Show the COMPLETE build output to the user
3. ✅ Verify the output contains `Build succeeded` with **0 errors**
4. ✅ Only THEN confirm the changes are ready

**FORBIDDEN RESPONSES:**
- ❌ "It will build now"
- ❌ "This should compile"
- ❌ "The build will succeed"
- ❌ "Now it's fixed" (without proof)

**REQUIRED RESPONSE:**
- ✅ Show actual build output proving success
- ✅ Explicitly state: "Build verified - X warnings, 0 errors"

## Code Quality Standards

### When Making Changes:
1. **Always verify compilation** - No exceptions
2. **Check for missing using statements** - Common mistake
3. **Verify dependency injection registration** - Services must be registered in Program.cs
4. **Test namespace and assembly references** - Ensure they exist

### Before Confirming Work is Done:
1. Build the specific project: `dotnet build`
2. If multiple projects affected, build solution: `dotnet build` from root
3. Show the user the full terminal output
4. Count and report errors vs warnings

## Project Structure Awareness

### HostCraft Solution Structure:
- **HostCraft.Core** - Entities, interfaces, enums
- **HostCraft.Infrastructure** - Implementations (Docker, SSH, Proxy, etc.)
- **HostCraft.Api** - API controllers and endpoints
- **HostCraft.Web** - Blazor Server UI
- **HostCraft.Shared** - Shared models

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
- Real IP addresses (use 10.0.0.1, example.com instead)
- Actual passwords or credentials
- Production server names
- Real email addresses (use example@example.com)

## Workflow Requirements

When user says "Make sure it builds":
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
