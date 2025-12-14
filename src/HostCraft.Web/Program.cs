using HostCraft.Web.Components;
using HostCraft.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add SignalR for real-time terminal communication
builder.Services.AddSignalR();

// Add HttpClient for API calls
builder.Services.AddHttpClient("HostCraftAPI", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiUrl"] ?? "http://localhost:5100");
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
