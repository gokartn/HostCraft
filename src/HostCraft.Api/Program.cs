using Microsoft.EntityFrameworkCore;
using HostCraft.Infrastructure.Persistence;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Docker;
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

var app = builder.Build();

// Auto-migrate database and seed
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
    
    // Automatically apply pending migrations (creates database if needed)
    await context.Database.MigrateAsync();
    
    // Seed initial data
    await HostCraft.Infrastructure.Persistence.DbSeeder.SeedAsync(context);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("AllowAll");
}

app.UseHttpsRedirection();
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
