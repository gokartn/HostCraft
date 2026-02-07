# Security Policy

## 🔒 Reporting a Vulnerability

The HostCraft team takes security vulnerabilities seriously. We appreciate your efforts to responsibly disclose your findings.

### How to Report

I'm sorry but right now we don't have a security contact Email address. 
Until we get one, please create a GitHub issue with the label "Security".

Include the following information in your report:

- **Type of vulnerability** (e.g., SQL injection, XSS, authentication bypass)
- **Affected component** (e.g., API endpoint, web UI, Docker configuration)
- **Steps to reproduce** the vulnerability
- **Potential impact** of the vulnerability
- **Suggested fix** (if you have one)
- **Your contact information** for follow-up questions

### What to Expect

1. **Acknowledgment**: We will acknowledge receipt of your vulnerability report within 48 hours.

2. **Initial Assessment**: We will provide an initial assessment within 5 business days, including:
   - Confirmation of the vulnerability
   - Severity assessment
   - Estimated timeline for a fix

3. **Resolution**: We will work to resolve the issue as quickly as possible:
   - **Critical**: Within 7 days
   - **High**: Within 14 days
   - **Medium**: Within 30 days
   - **Low**: Within 60 days

4. **Disclosure**: Once the vulnerability is fixed:
   - We will release a security update
   - You will be credited (unless you prefer to remain anonymous)
   - We will publish a security advisory

## 🛡️ Supported Versions

We provide security updates for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 0.0.x-alpha | ⚠️ Limited (Development) |
| 0.x.x-beta  | ✅ Yes (Testing) |
| 1.x.x       | ✅ Yes (Stable) |

**Note:** Alpha versions are for development and testing only. We recommend not using them in production environments.

## 🔐 Security Best Practices

### For Users

#### Installation Security

**Never pipe curl directly to bash:**
```bash
# ❌ DON'T DO THIS
curl https://example.com/install.sh | bash

# ✅ DO THIS INSTEAD
curl -fsSL -o install.sh https://github.com/gokartn/hostcraft/releases/download/vX.X.X/install.sh
less install.sh  # Review the script
chmod +x install.sh
./install.sh
```

#### Deployment Security

1. **Use Strong Passwords**
   - Change default database passwords
   - Use complex passwords (minimum 16 characters)
   - Store passwords securely (use password managers)

2. **Enable HTTPS**
   - Always use SSL/TLS in production
   - Configure Let's Encrypt certificates
   - Never transmit credentials over HTTP

3. **Secure Docker**
   - Keep Docker up to date
   - Use Docker secrets for sensitive data
   - Limit container privileges
   - Use read-only filesystems where possible

4. **Network Security**
   - Use firewalls to restrict access
   - Only expose necessary ports
   - Use VPNs for remote access
   - Enable Traefik authentication

5. **Regular Updates**
   - Keep HostCraft updated
   - Monitor security advisories
   - Update Docker images regularly
   - Patch the host OS

6. **Backup Security**
   - Encrypt backups
   - Store backups securely
   - Test backup restoration regularly
   - Use separate credentials for backup storage

### For Contributors

#### Code Security

1. **Never Commit Secrets**
   - No API keys, passwords, or tokens in code
   - Use environment variables for sensitive data
   - Review commits before pushing
   - Use `.gitignore` properly

2. **Input Validation**
   - Validate all user input
   - Sanitize data before use
   - Use parameterized queries
   - Implement rate limiting

3. **Authentication & Authorization**
   - Use secure authentication methods
   - Implement proper authorization checks
   - Use JWT tokens securely
   - Implement session management properly

4. **Dependencies**
   - Keep dependencies updated
   - Review dependency security advisories
   - Use `dotnet list package --vulnerable`
   - Avoid unnecessary dependencies

5. **Error Handling**
   - Don't expose sensitive information in errors
   - Log security events
   - Implement proper exception handling
   - Use generic error messages for users

#### CI/CD Security

1. **GitHub Actions Security**
   - Never use `pull_request_target` unless necessary
   - Restrict workflows to authorized users
   - Use granular action versions (e.g., `v4.1.1` not `v4`)
   - Minimize secret exposure

2. **Secret Management**
   - Never echo secrets
   - Never dump environment variables
   - Avoid `set -x` in scripts with secrets
   - Use GitHub's secret masking

## 🚨 Known Security Considerations

### Current Alpha Version (0.0.1-alpha)

⚠️ **This is an alpha version and should NOT be used in production.**

Known limitations:
- Limited security hardening
- Potential breaking changes
- Not all security features implemented
- Limited security testing

### Docker Security

- Containers run as root by default (will be addressed in future versions)
- Docker socket exposure for Swarm management
- Network isolation in development

### Authentication

- Default admin credentials must be changed immediately
- Session management is basic in alpha versions
- OAuth integration is in development

## 🔍 Security Features

### Implemented

- ✅ JWT-based authentication
- ✅ Password hashing (bcrypt)
- ✅ HTTPS support via Traefik
- ✅ Database connection encryption
- ✅ Encrypted sensitive data storage
- ✅ Docker Swarm secrets support
- ✅ Rate limiting on API endpoints
- ✅ Input validation

### Planned

- ⏳ Two-factor authentication (2FA)
- ⏳ Audit logging
- ⏳ Role-based access control (RBAC)
- ⏳ API key management
- ⏳ Container security scanning
- ⏳ Automated security testing
- ⏳ Penetration testing

## 📋 Security Checklist for Deployment

Before deploying HostCraft to production:

- [ ] Changed all default passwords
- [ ] Enabled HTTPS with valid certificates
- [ ] Configured firewall rules
- [ ] Enabled authentication on Traefik dashboard
- [ ] Reviewed and secured exposed ports
- [ ] Set up regular backups
- [ ] Configured backup encryption
- [ ] Updated all dependencies
- [ ] Reviewed security advisories
- [ ] Implemented monitoring and logging
- [ ] Tested disaster recovery procedures
- [ ] Documented security configuration
- [ ] Trained team on security practices

## 🔗 Security Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [Docker Security Best Practices](https://docs.docker.com/engine/security/)
- [.NET Security Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/security/)
- [GitHub Security Best Practices](https://docs.github.com/en/code-security)

## 📞 Contact

For security-related questions or concerns:

- **Security Issues**: [Your security email]
- **General Questions**: Open a [Discussion](https://github.com/gokartn/hostcraft/discussions)
- **Non-Security Bugs**: Open an [Issue](https://github.com/gokartn/hostcraft/issues)

## 🙏 Responsible Disclosure

We kindly ask security researchers to:

- Give us reasonable time to fix vulnerabilities before public disclosure
- Make a good faith effort to avoid privacy violations and data destruction
- Not exploit vulnerabilities beyond what's necessary to demonstrate the issue
- Not perform attacks that could harm our users or services

We commit to:

- Respond promptly to your report
- Keep you informed of our progress
- Credit you for your discovery (if desired)
- Work with you to understand and resolve the issue

## 📜 Legal

This security policy is subject to change. We reserve the right to modify this policy at any time. Continued participation in our security program constitutes acceptance of any changes.

**Bug Bounty**: We do not currently offer a bug bounty program, but we deeply appreciate responsible disclosure and will publicly acknowledge your contribution.

---

**Thank you for helping keep HostCraft and our users safe!** 🛡️
