using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using HostCraft.Infrastructure.Persistence;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Docker;
using HostCraft.Infrastructure.Auth;
using Serilog;
using Serilog.Events;

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

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names to match Web client expectations
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        // Handle circular references in entity relationships
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        // Make JSON more readable in development
        options.JsonSerializerOptions.WriteIndented = true;
    });
builder.Services.AddEndpointsApiExplorer();

// Database - PostgreSQL only
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found. HostCraft requires PostgreSQL.");

builder.Services.AddDbContext<HostCraftDbContext>(options =>
    options.UseNpgsql(connectionString)
        .ConfigureWarnings(warnings => 
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// Services
// DockerService as singleton to maintain SSH tunnels across requests
builder.Services.AddSingleton<IDockerService, DockerService>();
builder.Services.AddSingleton<ISshService, HostCraft.Infrastructure.Ssh.SshService>();
builder.Services.AddScoped<INetworkManager, NetworkManager>();
builder.Services.AddScoped<IProxyService, HostCraft.Infrastructure.Proxy.ProxyService>();
builder.Services.AddHttpClient<IUpdateService, HostCraft.Infrastructure.Updates.UpdateService>();

// Git integration services
builder.Services.AddHttpClient(); // For GitProviderService
builder.Services.AddScoped<IGitProviderService, HostCraft.Infrastructure.Git.GitProviderService>();
builder.Services.AddScoped<IGitService, HostCraft.Infrastructure.Git.GitService>();
builder.Services.AddScoped<IBuildService, BuildService>();

// Docker Swarm services
builder.Services.AddScoped<ISwarmDeploymentService, SwarmDeploymentService>();
builder.Services.AddScoped<IStackService, StackService>();

// Deployment orchestration
builder.Services.AddScoped<IDeploymentService, DeploymentService>();

// Authentication service
builder.Services.AddScoped<IAuthService, AuthService>();

// Health monitoring service
builder.Services.AddHttpClient<IHealthMonitorService, HostCraft.Infrastructure.Health.HealthMonitorService>();

// Backup service
builder.Services.AddScoped<IBackupService, HostCraft.Infrastructure.Backups.BackupService>();

// Certificate/SSL service
builder.Services.AddHttpClient<ICertificateService, HostCraft.Infrastructure.Certificates.CertificateService>();

// Security services (encryption and secret management)
builder.Services.AddSingleton<IEncryptionService, HostCraft.Infrastructure.Security.EncryptionService>();
builder.Services.AddScoped<ISecretManager, HostCraft.Infrastructure.Security.SecretManager>();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? GenerateDefaultJwtSecret();
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "HostCraft";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "HostCraft";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
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

// Helper to generate a secure JWT secret if not configured
static string GenerateDefaultJwtSecret()
{
    var bytes = new byte[64];
    using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
    rng.GetBytes(bytes);
    return Convert.ToBase64String(bytes);
}
