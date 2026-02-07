using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using HostCraft.Infrastructure.Persistence;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Docker;
using HostCraft.Infrastructure.Auth;
using HostCraft.Infrastructure.Services;
using HostCraft.Infrastructure.BackgroundJobs;
using HostCraft.Api.Services;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Hosting;

using FluentValidation;
using FluentValidation.AspNetCore;
using HostCraft.Api.Filters;
using HostCraft.Api.Models.Errors;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Persistence.Repositories;
using HostCraft.Infrastructure.Ssh;
using HostCraft.Infrastructure.Proxy;
using HostCraft.Infrastructure.Updates;
using HostCraft.Infrastructure.Git;
using HostCraft.Infrastructure.Storage;
using HostCraft.Infrastructure.Health;
using HostCraft.Infrastructure.Backups;
using HostCraft.Infrastructure.Certificates;
using HostCraft.Infrastructure.Security;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .Enrich.WithMachineName()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "/app/logs/hostcraft-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting HostCraft API");

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

// Error response factory
builder.Services.AddSingleton<ApiErrorFactory>();


// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddControllers(options =>
{
    // Add global exception filter
    options.Filters.Add<GlobalExceptionFilter>();
    options.Filters.Add<ApiErrorResponseFilter>();
})
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names to match Web client expectations
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        // Handle circular references in entity relationships
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        // Make JSON more readable in development
        options.JsonSerializerOptions.WriteIndented = true;
    });

// FluentValidation - Register all validators from the Api assembly
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();

builder.Services.AddEndpointsApiExplorer();

// Database - PostgreSQL only
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found. HostCraft requires PostgreSQL.");

void ConfigureDatabase(DbContextOptionsBuilder options)
{
    options.UseNpgsql(connectionString)
        .ConfigureWarnings(warnings =>
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
}

builder.Services.AddDbContext<HostCraftDbContext>(ConfigureDatabase);

// AutoMapper
builder.Services.AddAutoMapper(typeof(Program));

// Configuration Options
builder.Services.Configure<HostCraft.Core.Configuration.DockerRegistryOptions>(
    builder.Configuration.GetSection(HostCraft.Core.Configuration.DockerRegistryOptions.SectionName));

// Repositories
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IDomainRepository, DomainRepository>();
builder.Services.AddScoped<IServerRepository, ServerRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IDeploymentRepository, DeploymentRepository>();
builder.Services.AddScoped<IPrivateKeyRepository, PrivateKeyRepository>();
builder.Services.AddScoped<IRegionRepository, RegionRepository>();
builder.Services.AddScoped<IGitProviderRepository, GitProviderRepository>();
builder.Services.AddScoped<IGitProviderSettingsRepository, GitProviderSettingsRepository>();
builder.Services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
builder.Services.AddScoped<IHealthCheckRepository, HealthCheckRepository>();
builder.Services.AddScoped<ICertificateRepository, CertificateRepository>();
builder.Services.AddScoped<IComposeEnvironmentVariableRepository, ComposeEnvironmentVariableRepository>();
builder.Services.AddScoped<IEnvironmentVariableRepository, EnvironmentVariableRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// Services
// DockerService as singleton to maintain SSH tunnels across requests
builder.Services.AddSingleton<IDockerService, DockerService>();
builder.Services.AddSingleton<ISshService, SshService>();
builder.Services.AddScoped<INetworkManager, NetworkManager>();
builder.Services.AddScoped<IProxyService, ProxyService>();
builder.Services.AddHttpClient<IUpdateService, UpdateService>();
builder.Services.AddSingleton<IDeploymentJobQueue, DeploymentJobQueue>();
builder.Services.AddHostedService<DeploymentWorker>();

// DNS and Traefik services (Phase 2 refactor)
builder.Services.AddScoped<IDnsValidationService, DnsValidationService>();
builder.Services.AddScoped<ITraefikService, TraefikService>();
builder.Services.AddScoped<IDeploymentOrchestrator, DeploymentOrchestrator>();
builder.Services.AddScoped<ISwarmManagementService, SwarmManagementService>();
builder.Services.AddScoped<IApplicationManagementService, ApplicationManagementService>();
builder.Services.AddScoped<IApplicationOperationsService, ApplicationOperationsService>();
builder.Services.AddScoped<IServerManagementService, ServerManagementService>();
builder.Services.AddScoped<IServerOrchestrationService, ServerOrchestrationService>();
builder.Services.AddScoped<IServerConfigurationService, ServerConfigurationService>();
builder.Services.AddScoped<IServerMetricsService, ServerMetricsService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddScoped<IHADashboardWorkflowService, HADashboardWorkflowService>();
builder.Services.AddScoped<ISystemSettingsWorkflowService, SystemSettingsWorkflowService>();
builder.Services.AddScoped<IApplicationsWorkflowService, ApplicationsWorkflowService>();
builder.Services.AddScoped<IServersWorkflowService, ServersWorkflowService>();
builder.Services.AddScoped<IDomainsWorkflowService, DomainsWorkflowService>();

// Git integration services
builder.Services.AddHttpClient(); // For GitProviderService
builder.Services.AddScoped<IGitProviderService, GitProviderService>();
builder.Services.AddScoped<IGitOAuthService, GitOAuthService>();
builder.Services.AddScoped<IGitService, GitService>();
builder.Services.AddScoped<IBuildService, BuildService>();

// Docker Swarm services
builder.Services.AddScoped<ISwarmDeploymentService, SwarmDeploymentService>();
builder.Services.AddScoped<IStackService, StackService>();
builder.Services.AddScoped<IComposeParser, ComposeParser>();

// Storage services (HA/GlusterFS)
builder.Services.AddScoped<IGlusterFsService, GlusterFsService>();

// Deployment orchestration
builder.Services.AddScoped<IDeploymentService, DeploymentService>();
builder.Services.AddScoped<IDeploymentLogService, DeploymentLogService>();

// Authentication service
builder.Services.AddScoped<IAuthenticationWorkflowService, AuthenticationWorkflowService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Health monitoring service
builder.Services.AddHttpClient<IHealthMonitorService, HealthMonitorService>();

// Node metrics service
builder.Services.AddSingleton<INodeMetricsService, NodeMetricsService>();

// HA dashboard and system settings services
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();

// Database template service
builder.Services.AddScoped<IDatabaseTemplateService, DatabaseTemplateService>();

// Backup service
builder.Services.AddScoped<IBackupService, BackupService>();

// Certificate/SSL service
builder.Services.AddHttpClient<ICertificateService, CertificateService>();

// Security services (encryption and secret management)
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<ISecretManager, SecretManager>();

// JWT Settings service - manages JWT configuration from database
builder.Services.AddScoped<IJwtSettingsService, JwtSettingsService>();

// JWT Authentication - configured to read settings from database via IOptionsMonitor
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();

// Configure JWT Bearer options to use database settings
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IServiceScopeFactory>((options, scopeFactory) =>
    {
        // We need to get JWT settings synchronously during startup
        // Create a scope to access scoped services
        using var scope = scopeFactory.CreateScope();
        var jwtSettingsService = scope.ServiceProvider.GetRequiredService<IJwtSettingsService>();
        var jwtSettings = jwtSettingsService.GetJwtSettingsAsync().GetAwaiter().GetResult();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure forwarded headers for reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Trust all proxies in Docker/Swarm environment
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Initialize the encrypted string converter with the encryption service
using (var scope = app.Services.CreateScope())
{
    var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
    HostCraft.Infrastructure.Security.EncryptedStringConverter.Initialize(encryptionService);
}

// Use forwarded headers (must be first in pipeline for reverse proxy)
app.UseForwardedHeaders();

// Auto-migrate database and seed with retry logic for Docker Swarm DNS resolution
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
    var hostEnvironment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

    if (hostEnvironment.IsDevelopment())
    {
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            Log.Information("EF entity mapped: {ClrType} -> {Schema}{Table}",
                entityType.ClrType?.Name ?? entityType.Name,
                string.IsNullOrEmpty(entityType.GetSchema()) ? string.Empty : entityType.GetSchema() + ".",
                entityType.GetTableName());
        }
    }
    
    // Retry database operations for Docker Swarm DNS propagation
    const int maxRetries = 10;
    const int retryDelaySeconds = 5;
    
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            Log.Information("Attempting database migration (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
            
            // Automatically apply pending migrations (creates database if needed)
            await context.Database.MigrateAsync();
            
            // Seed initial data
            await HostCraft.Infrastructure.Persistence.DbSeeder.SeedAsync(context);

            // Seed SystemSettings from environment variables (set by install.sh)
            await SeedSystemSettingsFromEnvironmentAsync(context, scope.ServiceProvider);

            Log.Information("Database migration and seeding completed successfully");
            break;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database operation failed on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
            
            if (attempt == maxRetries)
            {
                Log.Error(ex, "Database operations failed after {MaxRetries} attempts, application will terminate", maxRetries);
                throw;
            }
            
            Log.Information("Waiting {Delay} seconds before retry...", retryDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("AllowAll");
}

// Don't use HTTPS redirection - running behind reverse proxy
// app.UseHttpsRedirection();

// Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck");

// Version endpoint
app.MapGet("/api/version", () => Results.Ok(new
{
    version = "0.0.1-alpha",
    buildDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
    message = "HostCraft API - Redeploy Test Successful!"
}))
    .WithName("Version")
    .AllowAnonymous();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Seeds SystemSettings with domain and email configuration from environment variables.
/// This ensures the Settings page is pre-populated with values from install.sh.
/// Only seeds if SystemSettings doesn't already have these values configured.
/// Also applies the configuration to Traefik/hostcraft-web services if seeding occurs.
/// </summary>
static async Task SeedSystemSettingsFromEnvironmentAsync(HostCraftDbContext context, IServiceProvider serviceProvider)
{
    try
    {
        Log.Information("Checking for SystemSettings configuration from environment variables...");

        // Read environment variables set by install.sh via .env file
        var hostcraftDomain = Environment.GetEnvironmentVariable("HOSTCRAFT_DOMAIN");
        var hostcraftApiDomain = Environment.GetEnvironmentVariable("HOSTCRAFT_API_DOMAIN");
        var traefikEmail = Environment.GetEnvironmentVariable("TRAEFIK_EMAIL")
                           ?? Environment.GetEnvironmentVariable("LETSENCRYPT_EMAIL");

        // Skip if no configuration provided (e.g., local development without .env)
        if (string.IsNullOrWhiteSpace(hostcraftDomain) || hostcraftDomain == "hostcraft.localhost")
        {
            Log.Information("No production domain configured in environment variables (HOSTCRAFT_DOMAIN is empty or 'hostcraft.localhost'). User can configure via Settings page.");
            return;
        }

        Log.Information("Found environment configuration - Domain: {Domain}, Email: {Email}",
            hostcraftDomain, traefikEmail ?? "not set");

        // Get or create SystemSettings
        var settings = await context.SystemSettings.FirstOrDefaultAsync();
        bool isNew = false;

        if (settings == null)
        {
            settings = new HostCraft.Core.Entities.SystemSettings
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            isNew = true;
            Log.Information("Creating new SystemSettings record");
        }

        // Only update if not already configured by user
        bool needsUpdate = false;
        bool needsProxyUpdate = false;

        if (string.IsNullOrWhiteSpace(settings.HostCraftDomain))
        {
            settings.HostCraftDomain = hostcraftDomain;
            settings.HostCraftEnableHttps = true; // Enable HTTPS for production domains
            needsUpdate = true;
            needsProxyUpdate = true;
            Log.Information("Set HostCraftDomain: {Domain}", hostcraftDomain);
        }

        if (string.IsNullOrWhiteSpace(settings.HostCraftApiDomain) && !string.IsNullOrWhiteSpace(hostcraftApiDomain))
        {
            settings.HostCraftApiDomain = hostcraftApiDomain;
            needsUpdate = true;
            Log.Information("Set HostCraftApiDomain: {ApiDomain}", hostcraftApiDomain);
        }

        if (string.IsNullOrWhiteSpace(settings.HostCraftLetsEncryptEmail) && !string.IsNullOrWhiteSpace(traefikEmail))
        {
            settings.HostCraftLetsEncryptEmail = traefikEmail;
            needsUpdate = true;
            needsProxyUpdate = true;
            Log.Information("Set HostCraftLetsEncryptEmail: {Email}", traefikEmail);
        }

        if (needsUpdate)
        {
            settings.UpdatedAt = DateTime.UtcNow;
            settings.ConfiguredAt = DateTime.UtcNow;

            if (isNew)
            {
                context.SystemSettings.Add(settings);
            }

            await context.SaveChangesAsync();
            Log.Information("✅ SystemSettings seeded successfully from environment variables");
            Log.Information("   Domain: {Domain}, HTTPS: {Https}, Email: {Email}",
                settings.HostCraftDomain, settings.HostCraftEnableHttps, settings.HostCraftLetsEncryptEmail);

            // Apply configuration to running services (Traefik + hostcraft-web)
            if (needsProxyUpdate)
            {
                try
                {
                    Log.Information("Applying domain/email configuration to Traefik and hostcraft-web services...");
                    var proxyService = serviceProvider.GetRequiredService<IProxyService>();

                    var configured = await proxyService.ConfigureHostCraftDomainAsync(
                        settings.HostCraftDomain!,
                        settings.HostCraftEnableHttps,
                        settings.HostCraftLetsEncryptEmail,
                        CancellationToken.None);

                    if (configured)
                    {
                        settings.ProxyUpdatedAt = DateTime.UtcNow;
                        settings.CertificateStatus = "Requesting...";
                        await context.SaveChangesAsync();
                        Log.Information("✅ Proxy configuration applied successfully. SSL certificate will be requested automatically.");
                    }
                    else
                    {
                        Log.Warning("⚠️ Proxy configuration failed. User may need to apply configuration via Settings page.");
                    }
                }
                catch (Exception proxyEx)
                {
                    Log.Warning(proxyEx, "⚠️ Could not apply proxy configuration during seeding. User may need to apply configuration via Settings page.");
                }
            }
        }
        else
        {
            Log.Information("SystemSettings already configured, skipping environment variable seeding");
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "❌ Failed to seed SystemSettings from environment variables");
    }
}

