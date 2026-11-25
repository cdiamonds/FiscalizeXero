using FiscalizeXero.Api;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Security;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using FiscalizeXero.Api.Extensions;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.AppUserCompanies;
using FiscalizeXero.Api.Services.AppUsers;
using FiscalizeXero.Api.Services.Auths;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.DeviceConfig;
using FiscalizeXero.Api.Filters;
using FiscalizeXero.Api.Services.DeviceSession;
using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Api.Services.HsCodes;
using FiscalizeXero.Api.Services.PdfStamp;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.Sync;
using FiscalizeXero.Api.Services.Webhooks;
using FiscalizeXero.Shared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

// Cross-platform log location: on macOS/Linux the literal Windows path becomes
// a directory named "C:\Fiscalize\logs" inside the project root, which breaks
// MSBuild's recursive glob enumeration on the next build. Fall back to a sibling
// "logs" dir under the binary's base directory on non-Windows.
var logsRoot = OperatingSystem.IsWindows()
    ? "C:\\Fiscalize\\logs"
    : Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logsRoot);
var logPath = Path.Combine(logsRoot, "fiscalizexero-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    // Every SQL statement and every outbound HTTP request at Information is noise (and disk) in production.
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 90, // three months of daily files for support investigations; the privacy policy allows up to 12
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Allowed browser origins for the SPA. Configured via Cors:AllowedOrigins so the
// production origin can be changed without a rebuild; falls back to the local dev
// server. An empty list would lock every browser out, so treat that as "dev only".
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (corsOrigins is null || corsOrigins.Length == 0)
    corsOrigins = ["http://localhost:50910"];
// Configuration merges arrays by index, so a localhost entry from a base file can survive into a deployed one.
// Only a development machine may call the API from localhost.
if (!builder.Environment.IsDevelopment() && corsOrigins.Any(o => !o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
    corsOrigins = corsOrigins.Where(o => !o.Contains("localhost", StringComparison.OrdinalIgnoreCase)).ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                     policy =>
                     {
                         policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
                     });
});

builder.Services.AddHttpClient("Fiscalize", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Fiscalize:ApiUrl"] ?? "https://fiscalize.onaerp.africa");
});

builder.Services.AddControllers(o =>
{
    o.Filters.Add<FiscalizeSessionExceptionFilter>();
    // A Xero error no action handled itself goes out with Xero's own message (daily limit, reconnect) instead of a 500.
    o.Filters.Add<XeroApiExceptionFilter>();
    // Company data is shared by every member, so re-confirm membership with the platform before touching it.
    o.Filters.Add<CompanyMembershipFilter>();
})
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<DataContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Xero tokens, Fiscalize session tokens and device API secrets are encrypted before they
// reach SQL Server, keyed from AuthSettings:Key (long, random, server-only in production; see the checks below).
builder.Services.AddSingleton<ISecretProtector>(_ =>
    new AesGcmSecretProtector(builder.Configuration["AuthSettings:Key"]
        ?? throw new InvalidOperationException("AuthSettings:Key is required to encrypt stored secrets.")));

// Credential-bearing endpoints get a tight per-client budget so passwords and launch tokens cannot be
// guessed at speed. Everything else is unlimited; Xero webhooks must never be throttled.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    // Sign-in is delegated to the Fiscalize platform; these govern any local Identity password that may exist.
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole>()
  .AddEntityFrameworkStores<DataContext>()
  .AddDefaultTokenProviders();

// Real production config lives on the server: appsettings.json is maintained there
// by hand (the published copy is renamed aside so it cannot overwrite it), and
// appsettings.Production.json is git-ignored and never published. The consequence is
// that config added to this repo does NOT reach production on its own - someone has
// to add it to the server file. Anything whose absence would break or weaken the
// deployment is therefore checked here, so it fails loudly at startup rather than
// misbehaving in a way that looks like something else.
// See appsettings.Production.template.json for the required keys.
// Every environment except Development: a server started as "Staging" (or with no environment set to Production)
// must not quietly accept the committed signing key, which would let anyone forge a sign-in.
if (!builder.Environment.IsDevelopment())
{
    var placeholders = new (string Key, string Reason)[]
    {
        ("AuthSettings:Key", "tokens would be signed with the key that is committed to source control"),
        ("ConnectionStrings:DefaultConnection", "the database connection string was never filled in"),
        ("Xero:ClientId", "nobody could connect a Xero organisation, and every existing connection would stop refreshing"),
        ("Xero:ClientSecret", "nobody could connect a Xero organisation, and every existing connection would stop refreshing"),
        ("Xero:RedirectUri", "Xero would reject the consent flow, or send users to the wrong host"),
    };

    foreach (var (key, reason) in placeholders)
    {
        var value = builder.Configuration[key];
        var unset = string.IsNullOrWhiteSpace(value)
            || value.Contains("HERE", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith('<')
            || value == "This is the key that we will use in the encryption";

        if (unset)
            throw new InvalidOperationException(
                $"Production configuration '{key}' is missing or still a placeholder: {reason}. " +
                "Set it in appsettings.Production.json on the server (see appsettings.Production.template.json).");
    }

    // A short key is brute-forceable; HS256 wants at least 256 bits of secret.
    var signingKey = builder.Configuration["AuthSettings:Key"]!;
    if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        throw new InvalidOperationException(
            "Production 'AuthSettings:Key' must be at least 32 bytes. Generate one with: openssl rand -base64 48");

    // Cors:AllowedOrigins is newer than the server's hand-maintained appsettings.json.
    // If it is missing we fall back to the localhost dev origin, which does not fail
    // at startup - it fails later, in the browser, as a CORS error that looks like a
    // frontend bug. Catch it here instead.
    if (corsOrigins.All(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException(
            "Production 'Cors:AllowedOrigins' only contains localhost origins, so the deployed site would be " +
            "blocked by CORS. Set it to the frontend's public origin, e.g. https://xero.fiscalize.co.zw");

    // The old Fiscalize host stopped resolving; leaving it configured makes every
    // login hang for 75s on a socket timeout before failing.
    var fiscalizeUrl = builder.Configuration["Fiscalize:ApiUrl"];
    if (fiscalizeUrl?.Contains("erpona.com", StringComparison.OrdinalIgnoreCase) == true)
        throw new InvalidOperationException(
            $"Production 'Fiscalize:ApiUrl' still points at the retired domain ({fiscalizeUrl}), which no longer " +
            "resolves. Use https://fiscalize.onaerp.africa");
}

builder.Services.AddAuthentication(auth =>
{
    auth.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    auth.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    auth.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidAudience = builder.Configuration["AuthSettings:Audience"],
        ValidIssuer = builder.Configuration["AuthSettings:Issuer"],
        RequireExpirationTime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["AuthSettings:Key"]!)),
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    // A token issued before the user chose "Sign out of all devices" is refused. Tokens from before this check
    // existed carry no version and count as version 0, which stays valid until the user's first revocation.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var id = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(id, out var userId)) return;
            var issued = int.TryParse(ctx.Principal!.FindFirst(FiscalizeXero.Api.Security.SessionRevocation.Claim)?.Value, out var v) ? v : 0;
            var current = await ctx.HttpContext.RequestServices.GetRequiredService<FiscalizeXero.Api.Security.ISessionRevocation>().CurrentVersionAsync(userId);
            if (issued != current) ctx.Fail("This sign-in was ended from another device.");
        }
    };
});
builder.Services.AddSingleton<FiscalizeXero.Api.Security.ISessionRevocation, FiscalizeXero.Api.Security.SessionRevocation>();

builder.Services.Configure<XeroOptions>(builder.Configuration.GetSection(XeroOptions.SectionName));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();

        return new BadRequestObjectResult(new ErrorModel
        {
            Code = 400,
            Message = "One or more validation errors occurred.",
            Errors = errors
        });
    };
});

#region REPOSITORIES
builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();
builder.Services.AddScoped<IAppUserCompanyRepository, AppUserCompanyRepository>();
builder.Services.AddScoped<IClientConfigRepository, ClientConfigRepository>();
builder.Services.AddScoped<IFiscalRecordRepository, FiscalRecordRepository>();
builder.Services.AddScoped<FiscalizeXero.Api.Services.Fiscalization.IFiscalFailureService, FiscalizeXero.Api.Services.Fiscalization.FiscalFailureService>();
builder.Services.AddScoped<IContactVatRepository, ContactVatRepository>();
builder.Services.AddScoped<IItemHsCodeRepository, ItemHsCodeRepository>();
builder.Services.AddHealthChecks()
    .AddCheck<FiscalizeXero.Api.Services.Health.DatabaseHealthCheck>("database")
    .AddCheck<FiscalizeXero.Api.Services.Health.WebhookKeyHealthCheck>(FiscalizeXero.Api.Services.Health.WebhookKeyHealthCheck.Name)
    .AddCheck<FiscalizeXero.Api.Services.CompanyConnections.CompanyConnectionsHealthCheck>(FiscalizeXero.Api.Services.CompanyConnections.CompanyConnectionsHealthCheck.Name);
#endregion

#region SERVICES
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAppUserService, AppUserService>();
builder.Services.AddScoped<IAppUserCompanyService, AppUserCompanyService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IClientConfigService, ClientConfigService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<FiscalizeXero.Api.Services.Membership.IPlatformCompanyDirectory, FiscalizeXero.Api.Services.Membership.PlatformCompanyDirectory>();
builder.Services.AddScoped<FiscalizeXero.Api.Services.Membership.ICompanyMembershipVerifier, FiscalizeXero.Api.Services.Membership.CompanyMembershipVerifier>();
// Xero's identity server (code exchange and token refresh).
builder.Services.AddHttpClient<IXeroAuthService, XeroAuthService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<IXeroAuthBuilder, XeroAuthBuilder>();
builder.Services.AddSingleton<IXeroConnections, XeroConnections>();
// An organisation's tax rates are read from Xero at most once every few minutes (Xero's daily call limit is small).
builder.Services.AddSingleton<IXeroTaxRateCache, XeroTaxRateCache>();
builder.Services.AddScoped<IFiscalizationService, FiscalizationService>();
builder.Services.AddScoped<IDeviceConfigService, DeviceConfigService>();
builder.Services.AddScoped<IHsCodeService, HsCodeService>();
builder.Services.AddScoped<IFiscalizeSessionService, FiscalizeSessionService>();
builder.Services.AddSingleton<IDeviceSessionService, DeviceSessionService>();
builder.Services.AddSingleton<IPdfStampService, PdfStampService>();
builder.Services.AddScoped<FiscalizeXero.Api.Services.FiscalDocuments.IFiscalDocumentService, FiscalizeXero.Api.Services.FiscalDocuments.FiscalDocumentService>();

// Background workers
// The backstop behind webhooks: a "modified since" sync a few times a day per organisation, and after every (re)connection.
builder.Services.AddXeroBackgroundSync(builder.Configuration);
builder.Services.AddSingleton<IWebhookQueue, WebhookQueue>();
builder.Services.AddSingleton<FiscalizeXero.Api.Services.FiscalDocuments.IAttachmentQueue, FiscalizeXero.Api.Services.FiscalDocuments.AttachmentQueue>();
builder.Services.AddHostedService<FiscalizeXero.Api.Services.FiscalDocuments.AttachmentWorker>();
// Fiscalize layouts (designed on the platform, plugin type "Xero") and the platform's Connected apps page.
builder.Services.AddScoped<FiscalizeXero.Api.Services.DeviceSession.IDeviceCaller, FiscalizeXero.Api.Services.DeviceSession.DeviceCaller>();
builder.Services.AddScoped<FiscalizeXero.Api.Services.Layouts.IPlatformLayoutService, FiscalizeXero.Api.Services.Layouts.PlatformLayoutService>();
builder.Services.AddScoped<FiscalizeXero.Api.Services.Layouts.IFiscalPdfCounter, FiscalizeXero.Api.Services.Layouts.FiscalPdfCounter>();
builder.Services.AddScoped<FiscalizeXero.Api.Services.ConnectedApps.IConnectedAppReportSender, FiscalizeXero.Api.Services.ConnectedApps.ConnectedAppReportSender>();
builder.Services.AddSingleton<FiscalizeXero.Api.Services.ConnectedApps.IConnectedAppReportQueue, FiscalizeXero.Api.Services.ConnectedApps.ConnectedAppReportQueue>();
builder.Services.AddHostedService<FiscalizeXero.Api.Services.ConnectedApps.ConnectedAppReporter>();
builder.Services.AddHostedService<WebhookProcessingService>();
builder.Services.AddHostedService<FiscalizeXero.Api.Services.Fiscalization.FailureRetryService>();
#endregion

var app = builder.Build();

{
    var secretLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SecretProtector");
    FiscalizeXero.Api.Security.ProtectedStringConverter.OnUnreadable = ex => secretLog.LogError(ex,
        "A stored secret could not be decrypted and is treated as missing (that connection or session must be renewed). " +
        "If this appears for every row, AuthSettings:Key changed: restore the previous key.");
}

// Missing webhook key: not fatal (the sync and user actions still fiscalise) but every Xero delivery gets 401, so say so loudly.
if (!app.Environment.IsDevelopment() &&
    FiscalizeXero.Api.Services.Health.WebhookKeyHealthCheck.StartupWarning(app.Configuration["Xero:WebhookKey"]) is { } webhookWarning)
    Log.Warning("{Warning}", webhookWarning);

// Behind Cloudflare / a reverse proxy the app otherwise sees the proxy's http
// scheme, which breaks https redirects and any absolute URL we generate. Trust
// X-Forwarded-Proto/For so Request.Scheme reflects what the browser actually used.
// Only proxies listed in ReverseProxy:TrustedNetworks (CIDR, e.g. Cloudflare's published ranges) may set the
// client address; with none listed only a proxy on this machine is trusted, so a visitor can never spoof
// X-Forwarded-For to dodge the sign-in rate limit.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
};
foreach (var cidr in app.Configuration.GetSection("ReverseProxy:TrustedNetworks").Get<string[]>() ?? [])
{
    if (System.Net.IPNetwork.TryParse(cidr.Trim(), out var network)) forwarded.KnownIPNetworks.Add(network);
    else Log.Warning("ReverseProxy:TrustedNetworks entry {Entry} is not a CIDR range and was ignored", cidr);
}
app.UseForwardedHeaders(forwarded);

var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
    app.UseRouting();
}

// API documentation is a development aid; production must not publish the API surface.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
    app.MapOpenApi();
}

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var msg = app.Environment.IsDevelopment() && feature?.Error != null
        ? feature.Error.Message
        : "An unexpected error occurred. Please try again.";
    await ctx.Response.WriteAsJsonAsync(new { code = 500, message = msg });
}));

await SeedData.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Browser-facing hardening for API responses (the SPA host sets its own headers in .htaccess).
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.UseCors(MyAllowSpecificOrigins);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// Liveness, database reachability, whether the webhook key is set and the connection invariants, for IIS / uptime monitoring.
// Anonymous; reports statuses and a true/false flag, never configuration values.
app.MapHealthChecks("/health", FiscalizeXero.Api.Services.Health.WebhookKeyHealthCheck.ResponseOptions());
// Which build is running (version, commit, build and start time). Public: no configuration value is in it.
app.MapGet("/api/version", (IHostEnvironment env) => Results.Ok(FiscalizeXero.Api.Services.Version.AppVersionInfo.Current(env.EnvironmentName)))
   .AllowAnonymous().ExcludeFromDescription();

app.Run();
