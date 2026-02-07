FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Install required tools:
# - git: for cloning repositories during deployment
# - Kerberos/GSSAPI libraries: for SSH.NET and HTTP authentication
RUN apt-get clean && rm -rf /var/lib/apt/lists/* && \
    apt-get update && apt-get install -y \
    curl \
    git \
    libkrb5-3 \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Create temp directory for git operations
RUN mkdir -p /tmp/hostcraft-repos && chmod 777 /tmp/hostcraft-repos

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy all project files first for better layer caching
COPY ["src/HostCraft.Api/HostCraft.Api.csproj", "HostCraft.Api/"]
COPY ["src/HostCraft.Core/HostCraft.Core.csproj", "HostCraft.Core/"]
COPY ["src/HostCraft.Infrastructure/HostCraft.Infrastructure.csproj", "HostCraft.Infrastructure/"]
COPY ["src/HostCraft.Shared/HostCraft.Shared.csproj", "HostCraft.Shared/"]

# Restore dependencies
RUN dotnet restore "HostCraft.Api/HostCraft.Api.csproj"

# Copy source code
COPY ["src/HostCraft.Api/", "HostCraft.Api/"]
COPY ["src/HostCraft.Core/", "HostCraft.Core/"]
COPY ["src/HostCraft.Infrastructure/", "HostCraft.Infrastructure/"]
COPY ["src/HostCraft.Shared/", "HostCraft.Shared/"]
COPY ["scripts/", "scripts/"]
COPY ["VERSION", "VERSION"]

# Build
WORKDIR "/src/HostCraft.Api"
RUN dotnet build "HostCraft.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "HostCraft.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
# Copy VERSION file to publish output
COPY ["VERSION", "/app/publish/VERSION"]

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HostCraft.Api.dll"]
