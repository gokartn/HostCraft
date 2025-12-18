FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Install Kerberos libraries for SSH.NET GSSAPI support
RUN apt-get update && apt-get install -y libkrb5-3 && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/HostCraft.Api/HostCraft.Api.csproj", "src/HostCraft.Api/"]
COPY ["src/HostCraft.Core/HostCraft.Core.csproj", "src/HostCraft.Core/"]
COPY ["src/HostCraft.Infrastructure/HostCraft.Infrastructure.csproj", "src/HostCraft.Infrastructure/"]
COPY ["src/HostCraft.Shared/HostCraft.Shared.csproj", "src/HostCraft.Shared/"]
RUN dotnet restore "src/HostCraft.Api/HostCraft.Api.csproj"
COPY . .
WORKDIR "/src/src/HostCraft.Api"
RUN dotnet build "HostCraft.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HostCraft.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HostCraft.Api.dll"]
