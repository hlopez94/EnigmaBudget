using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EnigmaBudget.Api.Tests;

public sealed class OnboardingIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Onboarding_requires_authentication()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/onboarding");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Development_login_issues_session_and_initial_state_is_empty()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var response = await client.GetAsync("/api/onboarding");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<State>();
        Assert.NotNull(state);
        Assert.Equal(0, state.Version);
        Assert.False(state.Completed);
        Assert.Empty(state.Accounts);
        Assert.Empty(state.Cards);
    }

    [Fact]
    public async Task Save_increments_version_and_stale_save_conflicts()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var save = new SaveRequest(0, [], []);
        var saved = await client.PutAsJsonAsync("/api/onboarding", save);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(1, (await saved.Content.ReadFromJsonAsync<State>())!.Version);
        var stale = await client.PutAsJsonAsync("/api/onboarding", save);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Mutations_require_csrf_header()
    {
        using var client = _factory.CreateClient();
        var session = await client.GetFromJsonAsync<Session>("/api/auth/session");
        Assert.NotNull(session);
        var response = await client.PostAsJsonAsync("/api/auth/development", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Google_login_fails_closed_when_unconfigured()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/auth/google")).StatusCode);
    }

    [Fact]
    public async Task Production_development_login_is_not_found()
    {
        using var client = _factory.CreateProductionClient();
        var session = await client.GetFromJsonAsync<Session>("/api/auth/session");
        Assert.NotNull(session);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Confirmation_is_idempotent_and_preserves_currency_and_amounts()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var account = new AccountInput(Guid.NewGuid(), "Caja", "Banco", "ARS", "12345.67", "2026-10-01");
        var usdAccount = new AccountInput(Guid.NewGuid(), "Ahorro", "Banco", "USD", "987.65", "2026-10-02");
        var card = new CardInput(Guid.NewGuid(), "Visa", "Banco", 10, 20, "12.34", "56.78", "2026-10-15");
        var saved = await client.PutAsJsonAsync("/api/onboarding", new SaveRequest(0, [account, usdAccount], [card]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await saved.Content.ReadFromJsonAsync<State>())!.Version;
        var first = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var accounts = await client.GetFromJsonAsync<AccountSummary[]>("/api/accounts");
        var cards = await client.GetFromJsonAsync<CardSummary[]>("/api/cards");
        Assert.Equal(2, accounts!.Length); Assert.Contains(accounts, x => x.Currency == "ARS"); Assert.Contains(accounts, x => x.Currency == "USD");
        Assert.Equal("12345.67", accounts!.Single(x => x.Currency == "ARS").OpeningBalance);
        Assert.Equal("987.65", accounts!.Single(x => x.Currency == "USD").OpeningBalance);
        Assert.Equal("12.34", Assert.Single(cards!).StatementDebtArs);
        Assert.Equal("56.78", cards![0].StatementDebtUsd);
    }

    [Fact]
    public async Task Two_identities_have_isolated_drafts()
    {
        using var a = await _factory.AuthenticatedClientAsync("test-user-a");
        using var b = await _factory.AuthenticatedClientAsync("test-user-b");
        var account = new AccountInput(Guid.NewGuid(), "Mi cuenta", "Banco", "USD", "8.25", "2026-10-01");
        Assert.Equal(HttpStatusCode.OK, (await a.PutAsJsonAsync("/api/onboarding", new SaveRequest(0, [account], []))).StatusCode);
        var stateB = await b.GetFromJsonAsync<State>("/api/onboarding");
        Assert.Empty(stateB!.Accounts);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("duplicate-id")]
    public async Task Invalid_completion_inputs_are_rejected(string invalidKind)
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        var currency = invalidKind == "currency" ? "EUR" : "ARS";
        var amount = invalidKind == "amount" ? "1.001" : "1.00";
        var date = invalidKind == "date" ? "2026-02-30" : "2026-02-28";
        var account = new AccountInput(id, "Cuenta", "Banco", currency, amount, date);
         CardInput[] cards = invalidKind == "duplicate-id" ? [new CardInput(id, "Tarjeta", "Banco", null, null, "0", "0", "2026-02-28")] : [];
        var saved = await client.PutAsJsonAsync("/api/onboarding", new SaveRequest(0, [account], cards));
        if (invalidKind == "duplicate-id") { Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode); return; }
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await saved.Content.ReadFromJsonAsync<State>())!.Version;
        var response = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record Session(string CsrfToken);
    private sealed record State(int Version, bool Completed, AccountInput[] Accounts, CardInput[] Cards);
    private sealed record SaveRequest(int Version, AccountInput[] Accounts, CardInput[] Cards);
    private sealed record AccountInput(Guid Id, string Name, string BankName, string Currency, string OpeningBalance, string OpeningDate);
    private sealed record CardInput(Guid Id, string Name, string BankName, int? ClosingDay, int? DueDay, string StatementDebtArs, string StatementDebtUsd, string AsOfDate);
    private sealed record AccountSummary(string Currency, string OpeningBalance);
    private sealed record CardSummary(string StatementDebtArs, string StatementDebtUsd);
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _schema = "test_" + Guid.NewGuid().ToString("N");
    private string _connection = "";
    private ApiFactory? _productionFactory;
    public ApiFactory()
    {
        var b = new NpgsqlConnectionStringBuilder("Host=127.0.0.1;Port=55432;Database=enigmabudget;Username=enigma;Password=local-development-only");
        using var c = new NpgsqlConnection(b.ConnectionString); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = $"CREATE SCHEMA \"{_schema}\""; cmd.ExecuteNonQuery();
        b.SearchPath = _schema; _connection = b.ConnectionString;
    }
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services => { services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "TestOrCookie"; o.DefaultChallengeScheme = "TestOrCookie"; }).AddPolicyScheme("TestOrCookie", "Test identity or cookie", o => o.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("X-Test-Identity") ? "TestIdentity" : CookieAuthenticationDefaults.AuthenticationScheme).AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>("TestIdentity", _ => { }); services.Configure<AntiforgeryOptions>(o => o.Cookie.SecurePolicy = CookieSecurePolicy.None); services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => o.Cookie.SecurePolicy = CookieSecurePolicy.None); });
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connection,
            ["Authentication:DevelopmentLoginEnabled"] = "true",
            ["Authentication:Google:ClientId"] = "",
            ["Authentication:Google:ClientSecret"] = ""
        }));
    }

    public HttpClient CreateProductionClient()
    {
        _productionFactory = new ApiFactory();
        return _productionFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => { services.Configure<AntiforgeryOptions>(o => o.Cookie.SecurePolicy = CookieSecurePolicy.None); services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => o.Cookie.SecurePolicy = CookieSecurePolicy.None); });
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connection,
                ["Authentication:DevelopmentLoginEnabled"] = "true"
            }));
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    }

    public async Task<HttpClient> AuthenticatedClientAsync(string? identity = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        if (identity is not null) client.DefaultRequestHeaders.Add("X-Test-Identity", identity);
        var session = await client.GetFromJsonAsync<SessionDto>("/api/auth/session");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        var login = await client.SendAsync(request);
        if (!login.IsSuccessStatusCode) throw new InvalidOperationException($"Dev login returned {(int)login.StatusCode}");
        var refreshed = await client.GetFromJsonAsync<SessionDto>("/api/auth/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", refreshed!.CsrfToken);
        return client;
    }
    private sealed record SessionDto(string CsrfToken);
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { _productionFactory?.Dispose(); using var c = new NpgsqlConnection("Host=127.0.0.1;Port=55432;Database=enigmabudget;Username=enigma;Password=local-development-only"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE"; cmd.ExecuteNonQuery(); } }
}

public sealed class TestIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers["X-Test-Identity"].ToString();
        if (string.IsNullOrWhiteSpace(subject)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Name, subject)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
