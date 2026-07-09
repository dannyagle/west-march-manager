using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using WestMarch.Application.Common;
using WestMarch.Application.Sessions;
using WestMarch.Domain.Users;
using WestMarch.Infrastructure;
using WestMarch.Infrastructure.Identity;
using WestMarch.Infrastructure.Persistence;
using WestMarch.Infrastructure.Seeding;
using WestMarch.Infrastructure.Storage;
using WestMarch.Web.Components;
using WestMarch.Web.Components.Account;
using WestMarch.Web.Hubs;
using WestMarch.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Domain persistence, repositories, application services, adapters.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// --- Authentication -------------------------------------------------------
// One User, many login methods: Identity's external-login linkage is the provider
// abstraction. Discord is registered below purely as an OAuth handler; adding a
// future provider is one more AddOAuth/AddGoogle/... call — the user model is untouched.
var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    });
authBuilder.AddIdentityCookies();

var discord = builder.Configuration.GetSection("Authentication:Discord");
if (!string.IsNullOrEmpty(discord["ClientId"]))
{
    authBuilder.AddOAuth("Discord", "Discord", options =>
    {
        options.ClientId = discord["ClientId"]!;
        options.ClientSecret = discord["ClientSecret"]!;
        options.CallbackPath = "/signin-discord";
        options.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
        options.TokenEndpoint = "https://discord.com/api/oauth2/token";
        options.UserInformationEndpoint = "https://discord.com/api/users/@me";
        options.Scope.Add("identify");
        options.Scope.Add("email");
        options.SignInScheme = IdentityConstants.ExternalScheme;

        options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Email, "email");
        options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Name, "username");
        options.ClaimActions.MapCustomJson("urn:discord:displayname", user =>
            user.TryGetProperty("global_name", out var g) && g.ValueKind == System.Text.Json.JsonValueKind.String
                ? g.GetString()
                : user.GetProperty("username").GetString());
        options.ClaimActions.MapCustomJson("urn:discord:avatar", user =>
        {
            var id = user.GetProperty("id").GetString();
            return user.TryGetProperty("avatar", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.String
                ? $"https://cdn.discordapp.com/avatars/{id}/{a.GetString()}.png"
                : null;
        });

        options.Events.OnCreatingTicket = async context =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
            using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();
            using var user = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
            context.RunClaimActions(user.RootElement);
        };
    });
}

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // Community app: no email round-trip for local accounts. Wire a real
        // IEmailSender and flip this on when an SMTP/provider is available.
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// --- Authorization: policy-based, keyed on the additive role set -----------
builder.Services.AddAuthorization(AuthPolicies.Configure);

// Demo mode: Development, or the hosted test/demo site (SeedDemoData=true). Gates the
// CA "reset demo data" tool — never available against a real campaign database.
builder.Services.AddSingleton(new DemoMode(
    builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("SeedDemoData")));
builder.Services.AddScoped<IDemoResetService, DemoResetService>();

// Web-layer glue: current-user accessor and the SignalR broadcast seam.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
builder.Services.AddSingleton<ISessionMessageBroadcaster, SignalRSessionMessageBroadcaster>();
builder.Services.AddSignalR();

// Local image store writes under wwwroot/media unless configured otherwise.
builder.Services.PostConfigure<ImageStoreOptions>(options =>
    options.Local.RootPath ??= Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "media"));

// DataProtection guards Blazor circuits, antiforgery, and the auth cookie. On Azure App
// Service the framework already persists keys to the durable %HOME% share automatically;
// setting DataProtection:KeyPath pins them explicitly (e.g. a mounted share) if we ever
// scale beyond a single instance.
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("WestMarch");
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    await DevDataSeeder.SeedAsync(app.Services);
}
else
{
    // SeedDemoData=true (used by the hosted test/demo site) loads the full demo campaign —
    // reference data plus users, adventures, sessions, and economy. Otherwise, just apply
    // migrations and bootstrap the first Campaign Admin from configuration.
    if (builder.Configuration.GetValue<bool>("SeedDemoData"))
    {
        await DevDataSeeder.SeedAsync(app.Services);
    }
    else
    {
        await ProductionInitializer.InitializeAsync(app.Services, app.Logger);
    }

    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.UseStaticFiles(); // runtime uploads (wwwroot/media) are not build-time assets

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<WestMarch.Web.Hubs.SessionBoardHub>("/hubs/session-board");

app.MapAdditionalIdentityEndpoints();

app.Run();
