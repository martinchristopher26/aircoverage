using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Endpoints;
using AirCoverage.Api.Security;
using AirCoverage.Api.Stores;
using AirCoverage.Api.Sync;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- HTTPS: listen on a single TLS port (default 8443). The cert is either a
//     real one supplied via Kestrel config (Kestrel:Certificates:Default:*) or,
//     by default, an auto-generated self-signed cert for localhost persisted to
//     DevCert:Path (the mounted /data volume in Docker). ---
builder.WebHost.ConfigureKestrel((context, options) =>
{
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

// --- Persistence: SQLite via EF Core (one file; override the path in containers
//     with ConnectionStrings__Default=Data Source=/data/aircoverage.db). ---
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=aircoverage.db";
builder.Services.AddDbContext<CacheDbContext>(options => options.UseSqlite(connectionString));

// ADO options + authenticated, resilient HTTP client + store + background sync.
builder.Services.Configure<AdoOptions>(builder.Configuration.GetSection(AdoOptions.Section));
builder.Services.AddTransient<PatAuthHandler>();
builder.Services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>()
    .AddHttpMessageHandler<PatAuthHandler>()
    .AddStandardResilienceHandler();   // Polly: retries (incl. 429), timeout, circuit breaker
builder.Services.AddScoped<IItemStore, AdoItemStore>();
builder.Services.AddScoped<CacheSynchronizer>();
builder.Services.AddHostedService<SyncService>();

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

// Serve the built Vue SPA (wwwroot) as static files; these are public so the
// login screen can load. API routes are gated below.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthApi();
app.MapItemsApi();
app.MapSyncApi();

// SPA fallback: any non-API, non-file route returns index.html for client routing.
app.MapFallbackToFile("index.html");

app.Run();
