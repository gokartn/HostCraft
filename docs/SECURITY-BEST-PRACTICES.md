# Security Best Practices for HostCraft Development

## 🔒 Critical Security Rules

### 1. Never Commit Sensitive Data

**NEVER commit to the repository:**
- ❌ Server IP addresses
- ❌ Domain names (your actual domains)
- ❌ Email addresses (your personal/business emails)
- ❌ Passwords or API keys
- ❌ Database credentials
- ❌ SSH keys or certificates
- ❌ Any personally identifiable information (PII)

### 2. Use Environment Variables

**Always use environment variables for configuration:**

```bash
# ✅ Good - Use environment variables
ApiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "http://localhost:5100"

# ❌ Bad - Hardcoded values
ApiUrl = "http://10.0.0.1:5100"
```

### 3. Use Configuration Files (Gitignored)

**Create `.env` files for local development:**

```bash
# .env (gitignored)
SERVER_IP=10.0.0.1
DOMAIN=hostcraft.example.com
LETSENCRYPT_EMAIL=admin@example.com
```

**Use `.env.example` for templates:**

```bash
# .env.example (committed to repo)
SERVER_IP=your_server_ip_here
DOMAIN=your_domain_here
LETSENCRYPT_EMAIL=your_email_here
```

### 4. Default Values Should Be Generic

**Use generic defaults in code:**

```csharp
// ✅ Good - Generic defaults
hostcraftDomain = settings.HostCraftDomain ?? "localhost:5000";
defaultEmail = config["Email"] ?? "admin@example.com";

// ❌ Bad - Specific defaults
hostcraftDomain = settings.HostCraftDomain ?? "10.0.0.1:5000";
defaultEmail = config["Email"] ?? "john@mycompany.com";
```

## 📋 Pre-Commit Checklist

Before committing code, always check:

- [ ] No hardcoded IP addresses in code
- [ ] No real domain names in defaults
- [ ] No email addresses (except example.com)
- [ ] No passwords or secrets
- [ ] `.env` file is in `.gitignore`
- [ ] Only `.env.example` with placeholders is committed
- [ ] Configuration uses environment variables
- [ ] Default values are generic (localhost, example.com, etc.)

## 🛠️ How to Fix Existing Issues

### Step 1: Audit the Codebase

```bash
# Search for potential IPs
git grep -E '[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}'

# Search for email addresses
git grep -E '[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}'

# Search for common domain patterns
git grep -E '\.com|\.net|\.org|\.io|\.dev'
```

### Step 2: Remove from Git History (if committed)

```bash
# WARNING: This rewrites history!
# Use git-filter-repo (recommended) or BFG Repo-Cleaner

# Example with BFG
bfg --replace-text passwords.txt
git reflog expire --expire=now --all
git gc --prune=now --aggressive
git push --force
```

### Step 3: Update Code

Replace hardcoded values with environment variables or configuration.

## 📁 File Organization

```
/HostCraft
├── .env                    # ❌ NEVER COMMIT (gitignored)
├── .env.example            # ✅ Commit template with placeholders
├── .gitignore              # ✅ Must include .env
├── appsettings.json        # ✅ Generic defaults only
└── appsettings.Development.json  # ❌ NEVER COMMIT if contains sensitive data
```

## 🔐 Secrets Management

For production deployments:

1. **Docker Secrets** (recommended for Swarm)
   ```bash
   echo "my_secret_password" | docker secret create db_password -
   ```

2. **Environment Variables**
   ```bash
   docker service update --env-add DB_PASSWORD=$DB_PASSWORD myservice
   ```

3. **Configuration Management Tools**
   - HashiCorp Vault
   - AWS Secrets Manager
   - Azure Key Vault

## 🚨 What to Do If You Accidentally Committed Secrets

1. **Immediately rotate the exposed secrets** (change passwords, regenerate API keys)
2. **Remove from Git history** (see Step 2 above)
3. **Force push** to overwrite remote history
4. **Notify team members** to pull the cleaned history
5. **Review access logs** for any unauthorized access

## ✅ Good Examples

```csharp
// Configuration with fallbacks
var apiUrl = Environment.GetEnvironmentVariable("ApiUrl") 
    ?? builder.Configuration["ApiUrl"] 
    ?? "http://localhost:5100";

// Defaults for examples
var exampleDomain = "example.com";
var exampleEmail = "admin@example.com";
var exampleIp = "127.0.0.1";

// UI placeholders
<input placeholder="hostcraft.example.com" />
<input placeholder="admin@yourdomain.com" />
```

## ❌ Bad Examples

```csharp
// Hardcoded production values
var apiUrl = "http://10.0.0.1:5100";
var domain = "hostcraft.yourcompany.com";
var email = "john.doe@yourcompany.com";

// Specific defaults
hostcraftDomain = settings.HostCraftDomain ?? "10.0.0.1:5000";
```

## 📚 Additional Resources

- [OWASP Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- [GitHub: Removing sensitive data](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)
- [12-Factor App: Config](https://12factor.net/config)

## 🎓 Training for Team Members

Ensure all contributors understand:
1. Why secrets in code are dangerous
2. How to use environment variables
3. The pre-commit checklist
4. How to identify and report accidental commits
5. Proper incident response procedures

---

**Remember:** It only takes one commit to expose sensitive data to the world. Always review your changes before pushing!
