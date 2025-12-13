FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/HostCraft.Web/HostCraft.Web.csproj", "src/HostCraft.Web/"]
COPY ["src/HostCraft.Core/HostCraft.Core.csproj", "src/HostCraft.Core/"]
COPY ["src/HostCraft.Infrastructure/HostCraft.Infrastructure.csproj", "src/HostCraft.Infrastructure/"]
COPY ["src/HostCraft.Shared/HostCraft.Shared.csproj", "src/HostCraft.Shared/"]
RUN dotnet restore "src/HostCraft.Web/HostCraft.Web.csproj"
COPY . .
WORKDIR "/src/src/HostCraft.Web"
RUN dotnet build "HostCraft.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HostCraft.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HostCraft.Web.dll"]
