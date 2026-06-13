using AirCoverage.Api;
using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Endpoints;
using AirCoverage.Api.Security;
using AirCoverage.Api.Stores;
using AirCoverage.Api.Sync;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Transport. A TLS-terminating platform (Railway, Heroku, etc.) sets the PORT
//     env var and forwards plain HTTP to the container, handling HTTPS at its edge.
//     In that mode we bind HTTP on $PORT and trust the X-Forwarded-* headers. Locally
//     (no PORT) we serve HTTPS on 8443 with a self-signed dev cert (BYO cert via
//     Kestrel:Certificates:Default, else auto-generated and persisted to DevCert:Path). ---
var platformPort = HostingMode.ResolvePlatformPort(builder.Configuration["PORT"]);
var behindProxy = platformPort is not null;

builder.WebHost.ConfigureKestrel((context, options) =>
{
    if (behindProxy)
    {
        options.ListenAnyIP(platformPort!.Value); // HTTP; the platform terminates TLS
        return;
    }

    var config = context.Configuration;
    var httpsPort = config.GetValue("Https:Port", 8443);
    var byoCertPath = config["Kestrel:Certificates:Default:Path"];

    options.ListenAnyIP(httpsPort, listen =>
    {
        if (!string.IsNullOrWhiteSpace(byoCertPath))
        {
            listen.UseHttps(); // bring-your-own cert from Kestrel:Certificates:Default
        }
        else
        {
            var certPath = config["DevCert:Path"] ?? "aircoverage-dev.pfx";
            var certPassword = config["DevCert:Password"] ?? "aircoverage-dev";
            listen.UseHttps(DevCertificate.LoadOrCreate(certPath, certPassword));
        }
    });
});

// Behind a proxy, honor X-Forwarded-Proto/For so the app sees the original HTTPS
// scheme (keeps the Secure auth cookie correct) and the real client IP.
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

// --- Persistence: SQLite via EF Core (one file; override the path in containers
//     with ConnectionStrings__Default=Data Source=/data/aircoverage.db). ---
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=aircoverage.db";
builder.Services.AddDbContext<CacheDbContext>(options =>
    options.UseSqlite(connectionString).AddInterceptors(new SqlitePragmaInterceptor()));

// ADO options + authenticated, resilient HTTP client + store + background sync.
builder.Services.Configure<AdoOptions>(builder.Configuration.GetSection(AdoOptions.Section));
builder.Services.AddTransient<PatAuthHandler>();
builder.Services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>()
    .AddHttpMessageHandler<PatAuthHandler>()
    .AddStandardResilienceHandler();   // Polly: retries (incl. 429), timeout, circuit breaker
builder.Services.AddScoped<IItemStore, AdoItemStore>();
builder.Services.AddScoped<CacheSynchronizer>();
builder.Services.AddHostedService<SyncService>();

// --- Data Protection: the auth cookie is signed/encrypted with the DP key ring.
//     By default the keys live INSIDE the container (/root/.aspnet/DataProtection-Keys),
//     so every rebuild generates new keys and invalidates existing sessions (the
//     classic "login broke after rebuild"). Persist them to the mounted /data volume
//     instead. DataProtection__KeysDirectory is set to /data/dp-keys in the container;
//     left unset locally → ephemeral dev keys, which is fine for `dotnet run`. ---
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("AirCoverage");
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(keysDirectory))
{
    Directory.CreateDirectory(keysDirectory);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
}

// --- Auth v1: shared credential -> HttpOnly cookie. API returns 401 instead of
//     redirecting to a login page so the SPA can react. ---
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ac_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// --- Apply cache-schema migrations on startup. ADO is the source of truth; the
//     cache is populated by the SyncService's initial reconcile (no seeding). ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CacheDbContext>();
    db.Database.Migrate();
}

// Behind a proxy, apply forwarded headers first so all downstream middleware sees
// the real scheme (https) and client IP.
if (behindProxy)
{
    app.UseForwardedHeaders();
}

// Serve the built Vue SPA (wwwroot) as static files; these are public so the
// login screen can load. API routes are gated below.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Liveness probe for the hosting platform (anonymous).
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthApi();
app.MapItemsApi();
app.MapSyncApi();

// SPA fallback: any non-API, non-file route returns index.html for client routing.
app.MapFallbackToFile("index.html");

app.Run();
