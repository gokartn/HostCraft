---
description: How to create a new release
---

# Creating a New Release

This workflow describes how to create a new release of HostCraft on GitHub.

## Prerequisites

- You must be the repository owner (`gokartn` or `firefighter`) to trigger releases
- Ensure all changes are committed and pushed to the `main` branch
- All tests should be passing

## Release Process

1. **Navigate to GitHub Actions**
   - Go to the repository on GitHub
   - Click on the "Actions" tab
   - Select "Create Release" workflow from the left sidebar

2. **Trigger the Workflow**
   - Click "Run workflow" button
   - Fill in the required inputs:
     - **Release version**: Enter the version number (e.g., `0.0.1-alpha`, `0.1.0-beta`, `1.0.0`)
     - **Mark as pre-release**: Check this for alpha/beta releases, uncheck for stable releases

3. **What the Workflow Does**
   The release workflow automatically:
   - ✅ Builds the .NET solution
   - ✅ Runs all tests
   - ✅ Updates the VERSION file with the release version
   - ✅ Publishes API and Web projects
   - ✅ Creates release archives (.tar.gz files)
   - ✅ Builds and pushes Docker images to GitHub Container Registry
     - `ghcr.io/gokartn/hostcraft-api:VERSION`
     - `ghcr.io/gokartn/hostcraft-web:VERSION`
     - Also tags as `:latest`
   - ✅ Generates a changelog from git commits
   - ✅ Creates a Git tag (e.g., `v0.0.1-alpha`)
   - ✅ Creates a GitHub Release with:
     - Release notes
     - Installation instructions
     - Download links for archives
     - Docker image references
   - ✅ Updates version in the codebase (VERSION file, NavMenu.razor, CLAUDE.md)
   - ✅ Commits and pushes version updates back to main

4. **After Release**
   - The release will be visible on the GitHub Releases page
   - Docker images will be available in GitHub Container Registry
   - Users can install using: `curl -fsSL https://github.com/gokartn/hostcraft/releases/download/vVERSION/install.sh | bash`
   - The update checker in Settings will detect the new version

## Version Numbering

Follow semantic versioning with pre-release tags:

- **Alpha releases**: `0.0.X-alpha` (early development, unstable)
- **Beta releases**: `0.X.0-beta` (feature complete, testing phase)
- **Release candidates**: `X.Y.Z-rc.N` (stable, final testing)
- **Stable releases**: `X.Y.Z` (production-ready)

Examples:
- `0.0.1-alpha` - First alpha release
- `0.0.2-alpha` - Second alpha release
- `0.1.0-beta` - First beta release
- `1.0.0-rc.1` - First release candidate
- `1.0.0` - First stable release

## Security

The release workflow includes a security check that only allows:
- `gokartn` (repository owner)
- `firefighter` (authorized user)

to trigger releases. This prevents unauthorized releases even if someone has write access to the repository.

## Troubleshooting

**Release workflow fails:**
- Check the Actions tab for error logs
- Ensure all tests pass locally first
- Verify Docker builds work locally

**Docker images not appearing:**
- Check GitHub Container Registry permissions
- Ensure GITHUB_TOKEN has package write permissions

**Version not updating:**
- Check that the VERSION file exists in the repository
- Verify the workflow has write permissions to the repository
