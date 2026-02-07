using HostCraft.Web.Components;
using HostCraft.Web.Hubs;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Services;
using Serilog;
using Serilog.Events;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using HostCraft.Web.Services;
using HostCraft.Web.Handlers;

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
        path: "/app/logs/hostcraft-web-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting HostCraft Web");

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add SignalR for real-time terminal communication
builder.Services.AddSignalR();

// Add HttpClient for API calls
// Priority: Environment variable > appsettings.json > default
var apiUrl = Environment.GetEnvironmentVariable("ApiUrl") 
    ?? builder.Configuration["ApiUrl"] 
    ?? "http://localhost:5100";

// Ensure URL ends properly
if (!apiUrl.EndsWith("/"))
{
    apiUrl = apiUrl + "/";
}

Log.Information("Configured API URL: {ApiUrl}", apiUrl);
Log.Information("ASPNETCORE_ENVIRONMENT: {Environment}", builder.Environment.EnvironmentName);
Log.Information("Attempting to resolve 'api' hostname...");

// Try to diagnose DNS issues
try
{
    var hostEntry = System.Net.Dns.GetHostEntry("api");
    Log.Information("Successfully resolved 'api' to: {Addresses}", string.Join(", ", hostEntry.AddressList.Select(a => a.ToString())));
}
catch (Exception ex)
{
    Log.Warning(ex, "FAILED to resolve 'api' hostname");
    Log.Information("Trying 'hostcraft_api' instead...");
    try
    {
        var hostEntry2 = System.Net.Dns.GetHostEntry("hostcraft_api");
        Log.Information("Successfully resolved 'hostcraft_api' to: {Addresses}", string.Join(", ", hostEntry2.AddressList.Select(a => a.ToString())));
    }
    catch (Exception ex2)
    {
        Log.Warning(ex2, "FAILED to resolve 'hostcraft_api' hostname");
    }
}

// Configure typed HttpClient with proper lifetime management
builder.Services.AddHttpClient("HostCraftAPI", client =>
{
    client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromSeconds(60); // Allow time for backup/restore operations
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // Don't cache DNS forever - API container IP can change during Swarm rolling updates
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    // Allow connections to be recycled quickly after transient failures
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
    // Disable connection reuse when the server is unreachable
    ConnectTimeout = TimeSpan.FromSeconds(10),
})
.AddHttpMessageHandler<AuthenticationHandler>()
.SetHandlerLifetime(Timeout.InfiniteTimeSpan); // Let PooledConnectionLifetime handle recycling

// Register the authentication handler
builder.Services.AddTransient<AuthenticationHandler>();

// Register as scoped to match Blazor component lifecycle
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("HostCraftAPI");
});

// Add authentication and authorization services
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = "HostCraft.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<AuthenticationStateProvider, HostCraftAuthenticationStateProvider>();
builder.Services.AddSingleton<ITokenStore, TokenStore>();
builder.Services.AddScoped<IWebAuthService, AuthService>();

// Add Docker Compose parser
builder.Services.AddScoped<IComposeParser, HostCraft.Infrastructure.Docker.ComposeParser>();

// Add SSH service for certificate status checks
builder.Services.AddSingleton<ISshService, HostCraft.Infrastructure.Ssh.SshService>();

// Add certificate status service for SSL transparency (simplified for demo)
builder.Services.AddScoped<ICertificateStatusService, CertificateStatusService>();

// Add HA Dashboard background service for real-time cluster updates
builder.Services.AddHostedService<HADashboardBackgroundService>();

// Configure YARP reverse proxy to forward /api/* requests to API service
// This enables OAuth callbacks and webhooks to work through Traefik -> Web -> API
var apiBaseUrl = apiUrl.TrimEnd('/');
builder.Services.AddReverseProxy()
    .LoadFromMemory(
        routes: new[]
        {
            new RouteConfig
            {
                RouteId = "api-route",
                ClusterId = "api-cluster",
                Match = new RouteMatch
                {
                    Path = "/api/{**catch-all}"
                },
                // Forward X-Forwarded-* headers to preserve original host info for OAuth callbacks
                Transforms = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string>
                    {
                        { "RequestHeadersCopy", "true" }
                    },
                    new Dictionary<string, string>
                    {
                        { "X-Forwarded", "Append" }  // Append to existing X-Forwarded headers
                    }
                }
            }
        },
        clusters: new[]
        {
            new ClusterConfig
            {
                ClusterId = "api-cluster",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    { "api", new DestinationConfig { Address = apiBaseUrl } }
                }
            }
        });

Log.Information("Configured YARP reverse proxy to forward /api/* to {ApiUrl}", apiBaseUrl);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// Don't use HTTPS redirection - running behind reverse proxy
// app.UseHttpsRedirection();

app.UseAntiforgery();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Map YARP reverse proxy BEFORE static assets and Razor components
// This ensures /api/* requests are forwarded to the API service
app.MapReverseProxy();

// Only apply 404 handler for non-API routes
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"), appBuilder =>
{
    appBuilder.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<TerminalHub>("/terminalhub");
app.MapHub<LogStreamHub>("/logstreamhub");
app.MapHub<HADashboardHub>("/hubs/hadashboard");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
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
