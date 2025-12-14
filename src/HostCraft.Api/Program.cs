using Microsoft.EntityFrameworkCore;
using HostCraft.Infrastructure.Persistence;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Docker;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=hostcraft.db";

if (connectionString.Contains("Data Source="))
{
    // SQLite for development
    builder.Services.AddDbContext<HostCraftDbContext>(options =>
        options.UseSqlite(connectionString));
}
else
{
    // PostgreSQL for production
    builder.Services.AddDbContext<HostCraftDbContext>(options =>
        options.UseNpgsql(connectionString));
}

// Services
builder.Services.AddScoped<IDockerService, DockerService>();
builder.Services.AddScoped<INetworkManager, NetworkManager>();
builder.Services.AddScoped<IProxyService, HostCraft.Infrastructure.Proxy.ProxyService>();
builder.Services.AddHttpClient<IUpdateService, HostCraft.Infrastructure.Updates.UpdateService>();

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

// Seed database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
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
