FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Install Kerberos libraries for SSH.NET GSSAPI support
RUN apt-get update && apt-get install -y libkrb5-3 && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy all project files first for better layer caching
COPY ["src/HostCraft.Web/HostCraft.Web.csproj", "HostCraft.Web/"]
COPY ["src/HostCraft.Core/HostCraft.Core.csproj", "HostCraft.Core/"]
COPY ["src/HostCraft.Infrastructure/HostCraft.Infrastructure.csproj", "HostCraft.Infrastructure/"]
COPY ["src/HostCraft.Shared/HostCraft.Shared.csproj", "HostCraft.Shared/"]

# Restore dependencies
RUN dotnet restore "HostCraft.Web/HostCraft.Web.csproj"

# Copy source code
COPY ["src/HostCraft.Web/", "HostCraft.Web/"]
COPY ["src/HostCraft.Core/", "HostCraft.Core/"]
COPY ["src/HostCraft.Infrastructure/", "HostCraft.Infrastructure/"]
COPY ["src/HostCraft.Shared/", "HostCraft.Shared/"]

# Build
WORKDIR "/src/HostCraft.Web"
RUN dotnet build "HostCraft.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "HostCraft.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Install nsenter for host terminal access when running in container
RUN apt-get update && apt-get install -y util-linux && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HostCraft.Web.dll"]
