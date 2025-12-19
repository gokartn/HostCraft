using HostCraft.Web.Components;
using HostCraft.Web.Hubs;
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
    client.Timeout = TimeSpan.FromSeconds(30);
})
.SetHandlerLifetime(TimeSpan.FromMinutes(5)); // Prevent socket exhaustion

// Register as scoped to match Blazor component lifecycle
builder.Services.AddScoped(sp => 
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("HostCraftAPI");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Don't use HTTPS redirection - running behind reverse proxy
// app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<TerminalHub>("/terminalhub");
app.MapHub<LogStreamHub>("/logstreamhub");

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
