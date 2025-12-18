using HostCraft.Web.Components;
using HostCraft.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

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

Console.WriteLine($"[HostCraft.Web] Configured API URL: {apiUrl}");
Console.WriteLine($"[HostCraft.Web] ASPNETCORE_ENVIRONMENT: {builder.Environment.EnvironmentName}");
Console.WriteLine($"[HostCraft.Web] Attempting to resolve 'api' hostname...");

// Try to diagnose DNS issues
try
{
    var hostEntry = System.Net.Dns.GetHostEntry("api");
    Console.WriteLine($"[HostCraft.Web] Successfully resolved 'api' to: {string.Join(", ", hostEntry.AddressList.Select(a => a.ToString()))}");
}
catch (Exception ex)
{
    Console.WriteLine($"[HostCraft.Web] FAILED to resolve 'api': {ex.Message}");
    Console.WriteLine($"[HostCraft.Web] Trying 'hostcraft_api' instead...");
    try
    {
        var hostEntry2 = System.Net.Dns.GetHostEntry("hostcraft_api");
        Console.WriteLine($"[HostCraft.Web] Successfully resolved 'hostcraft_api' to: {string.Join(", ", hostEntry2.AddressList.Select(a => a.ToString()))}");
    }
    catch (Exception ex2)
    {
        Console.WriteLine($"[HostCraft.Web] FAILED to resolve 'hostcraft_api': {ex2.Message}");
    }
}

builder.Services.AddHttpClient("HostCraftAPI", client =>
{
    client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("HostCraftAPI"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hub for terminal
app.MapHub<TerminalHub>("/terminalhub");

app.Run();
