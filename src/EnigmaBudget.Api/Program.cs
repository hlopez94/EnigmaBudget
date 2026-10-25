using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Onboarding;
using EnigmaBudget.Api.Storage;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.ExchangeRates;
using EnigmaBudget.Api.Projection;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var googleConfigured = !string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<WorkspaceProvisioner>();
builder.Services.AddScoped<WorkspaceAccessService>();
builder.Services.AddScoped<WorkspaceMutationGate>();
builder.Services.AddScoped<WorkspaceAuditInterceptor>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<PrivacyGrantService>();
builder.Services.AddScoped<SavingsService>();
builder.Services.AddScoped<SavingsAccountBalanceQuery>();
builder.Services.AddScoped<SavingsAllocationService>();
builder.Services.AddScoped<ProjectionService>();
builder.Services.AddScoped<ProjectionScenarioService>();
builder.Services.AddScoped<ExchangeRateService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddSingleton<ExchangeRateRefreshCoordinator>();
if (builder.Configuration.GetValue("ExchangeRates:AutomaticRefreshEnabled", true))
    builder.Services.AddHostedService<ExchangeRateRefreshHostedService>();
builder.Services.AddHttpClient<DolarApiClient>(client => { client.Timeout = TimeSpan.FromSeconds(5); })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
builder.Services.AddScoped<NativeSessionService>();
builder.Services.AddSingleton<IGoogleIdTokenValidator, GoogleNativeIdTokenValidator>();
builder.Services.AddOptions<NativeAuthConfiguration>().Bind(builder.Configuration.GetSection(NativeAuthConfiguration.SectionName))
    .Validate(o => o.ChallengeLifetime > TimeSpan.Zero && o.AccessLifetime > TimeSpan.Zero && o.AbsoluteSessionLifetime > TimeSpan.Zero,
        "Native authentication lifetimes must be positive.").ValidateOnStart();
var developmentIdentityOptions = builder.Services.AddOptions<DevelopmentIdentityOptions>()
    .Bind(builder.Configuration.GetSection(DevelopmentIdentityOptions.SectionName));
if (appDevelopmentLoginEnabled(builder))
    developmentIdentityOptions.Validate(DevelopmentIdentityOptions.IsValid, "Configured development identities must have unique keys and subjects with valid profile fields.").ValidateOnStart();
builder.Services.AddDbContext<AppDbContext>((services, o) => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")).AddInterceptors(services.GetRequiredService<WorkspaceAuditInterceptor>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BusinessClock>();
builder.Services.AddOptions<StorageOptions>().Bind(builder.Configuration.GetSection(StorageOptions.SectionName)).Validate(StorageOptions.IsValid, "S3 storage configuration must include a bucket and region, and use a valid HTTP(S) endpoint when configured.").ValidateOnStart();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "EnigmaBudget.Csrf"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = appDevelopmentLoginEnabled(builder) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; });
const string CookieOrNativeScheme = "CookieOrNative";
var auth = builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = CookieOrNativeScheme;
    o.DefaultAuthenticateScheme = CookieOrNativeScheme;
    o.DefaultChallengeScheme = CookieOrNativeScheme;
})
    .AddPolicyScheme(CookieOrNativeScheme, CookieOrNativeScheme, o => o.ForwardDefaultSelector = context =>
        context.Request.Headers.ContainsKey("Authorization") ? NativeBearerHandler.SchemeName : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
{
    o.Cookie.Name = "EnigmaBudget.Auth"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = appDevelopmentLoginEnabled(builder) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
})
    .AddScheme<AuthenticationSchemeOptions, NativeBearerHandler>(NativeBearerHandler.SchemeName, _ => { });
if (googleConfigured) auth.AddGoogle(GoogleDefaults.AuthenticationScheme, o => { o.ClientId = googleId!; o.ClientSecret = googleSecret!; o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; o.CallbackPath = "/signin-google"; o.Events.OnCreatingTicket = context => { var sub = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(sub)) throw new InvalidOperationException("Google subject missing"); if (context.Principal?.Identity is ClaimsIdentity identity) GoogleIdentityClaims.EnrichFromTrustedProfile(context.User, identity); return Task.CompletedTask; }; });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    AddNativeRatePolicy(options, NativeAuthRateLimits.Challenge, 10);
    AddNativeRatePolicy(options, NativeAuthRateLimits.Exchange, 5);
    AddNativeRatePolicy(options, NativeAuthRateLimits.Development, 5);
    AddNativeRatePolicy(options, NativeAuthRateLimits.Refresh, 30);
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();
if (!app.Environment.IsEnvironment("Testing")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(); }
var forwardedHeaders = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
{
    if (!System.Net.IPAddress.TryParse(proxy, out var address)) throw new InvalidOperationException($"Invalid ReverseProxy:KnownProxies address: {proxy}");
    forwardedHeaders.KnownProxies.Add(address);
}
app.UseForwardedHeaders(forwardedHeaders);
app.Use(async (context, next) =>
{
    if (!HttpMethods.IsPost(context.Request.Method)
        || !context.Request.Path.StartsWithSegments("/api/auth/native", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if (!context.Request.IsHttps)
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        return;
    }

    const int maximumBodyBytes = 16 * 1024;
    if (context.Request.ContentLength > maximumBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    var source = context.Request.Body;
    using var copy = new MemoryStream(maximumBodyBytes);
    var buffer = new byte[4096];
    var total = 0;
    while (true)
    {
        var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBodyBytes + 1 - total)), context.RequestAborted);
        if (read == 0) break;
        total += read;
        if (total > maximumBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        await copy.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
    }
    copy.Position = 0;
    context.Request.Body = copy;
    try { await next(); }
    finally { context.Request.Body = source; }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
    await next();
});
app.UseRateLimiter();
app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});
app.MapGet("/api/auth/session", async (HttpContext http, IAntiforgery antiforgery, WorkspaceProvisioner provisioner, Microsoft.Extensions.Options.IOptions<DevelopmentIdentityOptions> identityOptions) =>
{
    var tokens = antiforgery.GetAndStoreTokens(http);
    var authenticated = http.User.Identity?.IsAuthenticated == true;
    if (authenticated) await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
    var developmentLoginEnabled = app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Authentication:DevelopmentLoginEnabled");
    var developmentUsers = developmentLoginEnabled ? identityOptions.Value.Users.Select(user => new { key = user.Key, displayName = user.DisplayName }).ToArray() : [];
    return Results.Ok(new { authenticated, displayName = authenticated ? http.User.Identity!.Name : null, googleConfigured, developmentLoginEnabled, developmentUsers, csrfToken = tokens.RequestToken });
});
app.MapGet("/api/auth/google", (HttpContext http) => googleConfigured ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [GoogleDefaults.AuthenticationScheme]) : Results.StatusCode(503));
if (app.Environment.IsDevelopment()) app.MapPost("/api/auth/development", async (HttpContext http, IAntiforgery antiforgery, Microsoft.Extensions.Options.IOptions<DevelopmentIdentityOptions> identityOptions) =>
{
    if (!builder.Configuration.GetValue<bool>("Authentication:DevelopmentLoginEnabled")) return Results.NotFound();
    try { await antiforgery.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    DevelopmentLoginRequest? request = null;
    if (http.Request.ContentLength is > 0 or null)
    {
        try { request = await http.Request.ReadFromJsonAsync<DevelopmentLoginRequest>(http.RequestAborted); }
        catch (JsonException) when (http.Request.ContentLength is null) { }
    }
    var configured = identityOptions.Value.Users;
    var selected = string.IsNullOrWhiteSpace(request?.Key) ? null : configured.SingleOrDefault(x => string.Equals(x.Key, request.Key, StringComparison.Ordinal));
    if (!string.IsNullOrWhiteSpace(request?.Key) && selected is null) return Results.NotFound();
    var identity = selected is null
        ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "development-local-user"), new Claim(ClaimTypes.Name, "Usuario de desarrollo")], CookieAuthenticationDefaults.AuthenticationScheme)
        : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, selected.Subject), new Claim(ClaimTypes.Name, selected.DisplayName), new Claim(ClaimTypes.Email, selected.Email), new Claim("email_verified", "true")], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.NoContent();
});
app.MapPost("/api/auth/logout", async (HttpContext http, IAntiforgery antiforgery) => { try { await antiforgery.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); } await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.NoContent(); });
app.MapNativeAuthEndpoints();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/onboarding", async (HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner) => Results.Ok(await GetState(db, provisioner, http.User)));
api.MapPut("/onboarding", async (HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner, IAntiforgery csrf, SaveDraftRequest request) =>
{
    if (!await Csrf(http, csrf)) return Results.BadRequest();
    if (!ValidDraftShape(request)) return Problem(400, "El borrador excede los límites permitidos o contiene identificadores repetidos.");
    var workspace = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
    if (workspace.IsArchived) return Problem(403, "El espacio Personal está archivado y no admite cambios.");
    var draft = await db.Drafts.FindAsync(workspace.Id);
    if (draft is { Completed: true }) return Problem(409, "La configuración inicial ya está confirmada.");
    if ((draft?.Version ?? 0) != request.Version) return Problem(409, "El borrador cambió. Actualizá la pantalla antes de guardar.");
    if (draft is null) { draft = new OnboardingDraft { WorkspaceId = workspace.Id }; db.Drafts.Add(draft); }
    draft.AccountsJson = JsonSerializer.Serialize(request.Accounts); draft.CardsJson = JsonSerializer.Serialize(request.Cards); draft.Version++;
    try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Problem(409, "El borrador cambió. Actualizá la pantalla antes de guardar."); }
    return Results.Ok(State(draft));
});
api.MapPost("/onboarding/complete", async (HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner, IAntiforgery csrf, CompleteDraftRequest request) =>
{
    if (!await Csrf(http, csrf)) return Results.BadRequest();
    var workspace = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
    var draft = await db.Drafts.FindAsync(workspace.Id);
    if (draft is null) return Problem(409, "Guardá el borrador antes de confirmar.");
    if (draft.Completed) return Results.Ok(State(draft));
    if (workspace.IsArchived) return Problem(403, "El espacio Personal está archivado y no admite cambios.");
    if (draft.Version != request.Version) return Problem(409, "El borrador cambió. Actualizá la pantalla antes de confirmar.");
    var accounts = Deserialize<AccountInput>(draft.AccountsJson); var cards = Deserialize<CardInput>(draft.CardsJson);
    var validation = ValidateForCompletion(accounts, cards);
    if (validation is not null) return Problem(400, validation);
    await using var tx = await db.Database.BeginTransactionAsync();
    foreach (var a in accounts) { var amount = decimal.Parse(a.OpeningBalance, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture); var date = DateOnly.ParseExact(a.OpeningDate, "yyyy-MM-dd", CultureInfo.InvariantCulture); db.Accounts.Add(new BankAccount { Id=a.Id, WorkspaceId=workspace.Id, Name=a.Name.Trim(), BankName=a.BankName.Trim(), Currency=a.Currency, OpeningBalance=amount, OpeningDate=date }); db.OpeningBalances.Add(new OpeningBalanceRecord { WorkspaceId=workspace.Id, AccountId=a.Id, Currency=a.Currency, Amount=amount, EffectiveDate=date }); }
    foreach (var c in cards) db.Cards.Add(new PaymentCard { Id=c.Id, WorkspaceId=workspace.Id, Name=c.Name.Trim(), BankName=c.BankName.Trim(), ClosingDay=c.ClosingDay, DueDay=c.DueDay, StatementDebtArs=decimal.Parse(c.StatementDebtArs, CultureInfo.InvariantCulture), StatementDebtUsd=decimal.Parse(c.StatementDebtUsd, CultureInfo.InvariantCulture), AsOfDate=DateOnly.ParseExact(c.AsOfDate,"yyyy-MM-dd",CultureInfo.InvariantCulture) });
    draft.Completed = true;
    draft.Version++;
    try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateException) { await tx.RollbackAsync(); db.ChangeTracker.Clear(); var latest = await db.Drafts.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id); if (latest is { Completed: true }) return Results.Ok(State(latest)); return Problem(409, "La configuración ya fue confirmada o cambió."); }
    return Results.Ok(State(draft));
});
api.MapFinanceEndpoints();
api.MapBudgetEndpoints();
api.MapSavingsEndpoints();
api.MapProjectionEndpoints();
api.MapExchangeRateEndpoints();
app.MapWorkspaceEndpoints();
app.Run();

static async Task<OnboardingState> GetState(AppDbContext db, WorkspaceProvisioner provisioner, System.Security.Claims.ClaimsPrincipal principal)
{
    var workspace = await provisioner.EnsurePersonalAsync(principal); var draft = workspace.Draft!; return State(draft);
}
static OnboardingState State(OnboardingDraft d) => new(d.Version, d.Completed, Deserialize<AccountInput>(d.AccountsJson), Deserialize<CardInput>(d.CardsJson));
static T[] Deserialize<T>(string json) => JsonSerializer.Deserialize<T[]>(json) ?? [];
static Task<bool> Csrf(HttpContext http, IAntiforgery csrf) => NativeAuthCsrf.ValidateRequestAsync(http, csrf);
static IResult Problem(int status, string message) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: message);
static bool ValidDraftShape(SaveDraftRequest r) => r.Version >= 0 && r.Accounts is { Length: <=50 } && r.Cards is { Length: <=50 } && r.Accounts.All(x => x is not null) && r.Cards.All(x => x is not null) && r.Accounts.Select(x=>x.Id).Concat(r.Cards.Select(x=>x.Id)).Distinct().Count()==r.Accounts.Length+r.Cards.Length && r.Accounts.All(x=>x is not null && (x.Name?.Length ?? 0)<=120 && (x.BankName?.Length ?? 0)<=120) && r.Cards.All(x=>x is not null && (x.Name?.Length ?? 0)<=120 && (x.BankName?.Length ?? 0)<=120);
static string? ValidateForCompletion(AccountInput[] accounts, CardInput[] cards)
{
    if (!ValidDraftShape(new(0, accounts, cards))) return "Hay campos repetidos o fuera de los límites permitidos.";
    foreach (var a in accounts) if (string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.BankName) || a.Currency is not ("ARS" or "USD") || !Amount(a.OpeningBalance, false) || !Date(a.OpeningDate)) return "Revisá nombre, banco, moneda, saldo y fecha de cada cuenta.";
    foreach (var c in cards) if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.BankName) || !Day(c.ClosingDay) || !Day(c.DueDay) || !Amount(c.StatementDebtArs, true) || !Amount(c.StatementDebtUsd, true) || !Date(c.AsOfDate)) return "Revisá nombre, banco, deuda, fechas y días de cierre y vencimiento de cada tarjeta.";
    return null;
}
static bool Amount(string s, bool nonnegative)
{
    if (string.IsNullOrEmpty(s)) return false;
    var decimalPoint = s.IndexOf('.');
    if (decimalPoint >= 0 && s.Length - decimalPoint - 1 > 2) return false;
    return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && (!nonnegative || value >= 0)
        && value >= -9999999999999999.99m
        && value <= 9999999999999999.99m;
}
static bool appDevelopmentLoginEnabled(WebApplicationBuilder builder) => builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Authentication:DevelopmentLoginEnabled");
static void AddNativeRatePolicy(RateLimiterOptions options, string name, int permitsPerMinute) => options.AddPolicy(name, context =>
    RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitsPerMinute,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    }));
static bool Date(string s) => DateOnly.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
static bool Day(int? n) => n is null or (>=1 and <=31);
public partial class Program { }
