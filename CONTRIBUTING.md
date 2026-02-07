# Contributing to HostCraft

Thank you for your interest in contributing to HostCraft! This document provides guidelines and instructions for contributing to the project.

## 🤝 Code of Conduct

By participating in this project, you agree to maintain a respectful and inclusive environment for all contributors.

## 🔒 Security First

Before contributing, please review our [Security Policy](SECURITY.md) to understand how to report vulnerabilities responsibly.

## 🚀 Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- Docker 24.0+ and Docker Compose v2.20+
- Git
- A GitHub account

### Development Setup

1. **Fork the repository**
   - Click the "Fork" button at the top right of the repository page
   - This creates your own copy of the repository

2. **Clone your fork**
   ```bash
   git clone https://github.com/YOUR_USERNAME/hostcraft.git
   cd hostcraft
   ```

3. **Add upstream remote**
   ```bash
   git remote add upstream https://github.com/gokartn/hostcraft.git
   ```

4. **Install dependencies**
   ```bash
   dotnet restore HostCraft.sln
   ```

5. **Build the project**
   ```bash
   dotnet build HostCraft.sln
   ```

6. **Run tests**
   ```bash
   dotnet test HostCraft.sln
   ```

## 📝 Contribution Workflow

### 1. Create a Feature Branch

**Always create branches in your fork, not in the main repository.**

```bash
# Update your fork
git fetch upstream
git checkout main
git merge upstream/main

# Create a new feature branch
git checkout -b feature/your-feature-name
# or for bug fixes:
git checkout -b fix/bug-description
```

### Branch Naming Convention

- `feature/` - New features (e.g., `feature/backup-system`)
- `fix/` - Bug fixes (e.g., `fix/login-error`)
- `docs/` - Documentation updates (e.g., `docs/api-guide`)
- `refactor/` - Code refactoring (e.g., `refactor/auth-service`)
- `test/` - Test additions/updates (e.g., `test/backup-service`)

### 2. Make Your Changes

- Write clean, readable code
- Follow existing code style and conventions
- Add tests for new functionality
- Update documentation as needed
- Keep commits focused and atomic

### 3. Commit Your Changes

Write clear, descriptive commit messages:

```bash
git add .
git commit -m "feat: add S3 backup storage provider"
```

**Commit Message Format:**

```
<type>: <subject>

<body (optional)>

<footer (optional)>
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Maintenance tasks
- `perf`: Performance improvements
- `style`: Code style changes (formatting, etc.)

**Examples:**
```
feat: add Google Drive backup integration

Implements Google Drive as a backup storage provider with OAuth2 authentication.

Closes #123
```

```
fix: resolve database connection timeout

Increased connection timeout and added retry logic for transient failures.
```

### 4. Push to Your Fork

```bash
git push origin feature/your-feature-name
```

### 5. Open a Pull Request

1. Go to your fork on GitHub
2. Click "Pull Request" button
3. Select your feature branch
4. Fill out the PR template with:
   - Clear description of changes
   - Related issue numbers
   - Testing performed
   - Screenshots (if UI changes)

### 6. Code Review Process

- **No force-push after review**: Once a reviewer has commented, avoid force-pushing. Make new commits instead.
- **Address feedback**: Respond to all review comments
- **CI must pass**: All automated checks must pass before merge
- **Approval required**: At least one maintainer approval is required

## ✅ Pull Request Checklist

Before submitting your PR, ensure:

- [ ] Code builds without errors
- [ ] All tests pass
- [ ] New tests added for new functionality
- [ ] Documentation updated (if applicable)
- [ ] Commit messages follow the convention
- [ ] No merge conflicts with main branch
- [ ] Code follows project style guidelines
- [ ] No sensitive information (passwords, keys, etc.) in code
- [ ] PR description clearly explains the changes

## 🧪 Testing Guidelines

### Running Tests

```bash
# Run all tests
dotnet test HostCraft.sln

# Run specific test project
dotnet test src/HostCraft.Tests/HostCraft.Tests.csproj

# Run with coverage
dotnet test HostCraft.sln --collect:"XPlat Code Coverage"
```

### Writing Tests

- Write unit tests for new functionality
- Ensure tests are deterministic and isolated
- Use descriptive test names
- Follow AAA pattern (Arrange, Act, Assert)

Example:
```csharp
[Fact]
public async Task CreateBackup_WithValidConfiguration_ShouldSucceed()
{
    // Arrange
    var service = new BackupService();
    var config = new BackupConfiguration { /* ... */ };
    
    // Act
    var result = await service.CreateBackupAsync(config);
    
    // Assert
    Assert.True(result.Success);
}
```

## 📚 Documentation

- Update relevant documentation for any changes
- Add XML comments to public APIs
- Update README.md if adding new features
- Add examples for new functionality

## 🎨 Code Style

### C# Guidelines

- Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable and method names
- Keep methods small and focused
- Add XML documentation comments for public APIs
- Use async/await for asynchronous operations

### File Organization

```
src/
├── HostCraft.Api/          # API controllers and endpoints
├── HostCraft.Core/         # Domain entities and interfaces
├── HostCraft.Infrastructure/ # Implementation of services
├── HostCraft.Web/          # Blazor web UI
└── HostCraft.Shared/       # Shared models and utilities
```

## 🐛 Reporting Bugs

### Before Reporting

1. Check existing issues to avoid duplicates
2. Verify the bug exists in the latest version
3. Collect relevant information (logs, screenshots, etc.)

### Bug Report Template

```markdown
**Describe the bug**
A clear description of what the bug is.

**To Reproduce**
Steps to reproduce the behavior:
1. Go to '...'
2. Click on '...'
3. See error

**Expected behavior**
What you expected to happen.

**Screenshots**
If applicable, add screenshots.

**Environment:**
- OS: [e.g., Ubuntu 22.04]
- Docker version: [e.g., 24.0.5]
- HostCraft version: [e.g., 0.0.1-alpha]

**Additional context**
Any other relevant information.
```

## 💡 Feature Requests

We welcome feature requests! Please:

1. Check if the feature has already been requested
2. Clearly describe the feature and its use case
3. Explain why it would be valuable
4. Consider implementation complexity

## 🔐 Security Considerations

- **Never commit secrets**: No passwords, API keys, or tokens
- **Validate input**: Always validate and sanitize user input
- **Use parameterized queries**: Prevent SQL injection
- **Follow least privilege**: Request minimum necessary permissions
- **Review dependencies**: Check for known vulnerabilities

## 📋 Release Process

Releases are managed by repository maintainers. Contributors should:

- Focus on feature branches
- Not modify version numbers
- Not create tags or releases

## 🤔 Questions?

- Open a [Discussion](https://github.com/gokartn/hostcraft/discussions) for general questions
- Open an [Issue](https://github.com/gokartn/hostcraft/issues) for bugs or feature requests
- Check existing documentation in the `docs/` folder

## 📄 License

By contributing to HostCraft, you agree that your contributions will be licensed under the same license as the project.

## 🙏 Thank You!

Your contributions help make HostCraft better for everyone. We appreciate your time and effort!

---

**Remember:**
- Be respectful and constructive
- Ask questions if you're unsure
- Have fun coding! 🚀
