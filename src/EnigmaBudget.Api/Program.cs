using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Onboarding;
using EnigmaBudget.Api.Storage;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var googleConfigured = !string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<WorkspaceProvisioner>();
builder.Services.AddScoped<WorkspaceAccessService>();
builder.Services.AddScoped<WorkspaceAuditInterceptor>();
builder.Services.AddDbContext<AppDbContext>((services, o) => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")).AddInterceptors(services.GetRequiredService<WorkspaceAuditInterceptor>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BusinessClock>();
builder.Services.AddOptions<StorageOptions>().Bind(builder.Configuration.GetSection(StorageOptions.SectionName)).Validate(StorageOptions.IsValid, "S3 storage configuration must include a bucket and region, and use a valid HTTP(S) endpoint when configured.").ValidateOnStart();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "EnigmaBudget.Csrf"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = appDevelopmentLoginEnabled(builder) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; });
var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "EnigmaBudget.Auth"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = appDevelopmentLoginEnabled(builder) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
if (googleConfigured) auth.AddGoogle(GoogleDefaults.AuthenticationScheme, o => { o.ClientId = googleId!; o.ClientSecret = googleSecret!; o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; o.CallbackPath = "/signin-google"; o.ClaimActions.MapJsonKey("email_verified", "email_verified", ClaimValueTypes.Boolean); o.Events.OnCreatingTicket = async context => { var sub = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(sub)) throw new InvalidOperationException("Google subject missing"); await Task.CompletedTask; }; });
builder.Services.AddAuthorization();
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
app.MapGet("/api/auth/session", async (HttpContext http, IAntiforgery antiforgery, WorkspaceProvisioner provisioner) =>
{
    var tokens = antiforgery.GetAndStoreTokens(http);
    var authenticated = http.User.Identity?.IsAuthenticated == true;
    if (authenticated) await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
    return Results.Ok(new { authenticated, displayName = authenticated ? http.User.Identity!.Name : null, googleConfigured, developmentLoginEnabled = app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Authentication:DevelopmentLoginEnabled"), csrfToken = tokens.RequestToken });
});
app.MapGet("/api/auth/google", (HttpContext http) => googleConfigured ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [GoogleDefaults.AuthenticationScheme]) : Results.StatusCode(503));
if (app.Environment.IsDevelopment()) app.MapPost("/api/auth/development", async (HttpContext http, IAntiforgery antiforgery) =>
{
    if (!builder.Configuration.GetValue<bool>("Authentication:DevelopmentLoginEnabled")) return Results.NotFound();
    try { await antiforgery.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "development-local-user"), new Claim(ClaimTypes.Name, "Usuario de desarrollo")], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.NoContent();
});
app.MapPost("/api/auth/logout", async (HttpContext http, IAntiforgery antiforgery) => { try { await antiforgery.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); } await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.NoContent(); });

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/onboarding", async (HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner) => Results.Ok(await GetState(db, provisioner, http.User)));
api.MapPut("/onboarding", async (HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner, IAntiforgery csrf, SaveDraftRequest request) =>
{
    if (!await Csrf(http, csrf)) return Results.BadRequest();
    if (!ValidDraftShape(request)) return Problem(400, "El borrador excede los límites permitidos o contiene identificadores repetidos.");
    var workspace = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
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
app.MapWorkspaceEndpoints();
app.Run();

static async Task<OnboardingState> GetState(AppDbContext db, WorkspaceProvisioner provisioner, System.Security.Claims.ClaimsPrincipal principal)
{
    var workspace = await provisioner.EnsurePersonalAsync(principal); var draft = workspace.Draft!; return State(draft);
}
static OnboardingState State(OnboardingDraft d) => new(d.Version, d.Completed, Deserialize<AccountInput>(d.AccountsJson), Deserialize<CardInput>(d.CardsJson));
static T[] Deserialize<T>(string json) => JsonSerializer.Deserialize<T[]>(json) ?? [];
static async Task<bool> Csrf(HttpContext http, IAntiforgery csrf) { try { await csrf.ValidateRequestAsync(http); return true; } catch (AntiforgeryValidationException) { return false; } }
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
static bool Date(string s) => DateOnly.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
static bool Day(int? n) => n is null or (>=1 and <=31);
public partial class Program { }
