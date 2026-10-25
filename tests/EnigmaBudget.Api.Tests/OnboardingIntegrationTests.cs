using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Data;

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
    public async Task Session_and_authenticated_api_responses_are_not_cached()
    {
        using var anonymous = _factory.CreateClient();
        var session = await anonymous.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Contains("no-store", session.Headers.CacheControl?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        using var authenticated = await _factory.AuthenticatedClientAsync();
        var onboarding = await authenticated.GetAsync("/api/onboarding");
        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);
        Assert.Contains("no-store", onboarding.Headers.CacheControl?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Development_http_session_works_without_secure_cookie_test_overrides()
    {
        using var factory = new ApiFactory(relaxSecureCookiesForTests: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });

        var sessionResponse = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        Assert.Contains(sessionResponse.Headers.GetValues("Set-Cookie"), value => value.StartsWith("EnigmaBudget.Csrf=", StringComparison.Ordinal) && !value.Contains("; secure", StringComparison.OrdinalIgnoreCase));
        var session = await sessionResponse.Content.ReadFromJsonAsync<Session>();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { }) };
        login.Headers.Add("X-CSRF-TOKEN", session!.CsrfToken);
        var loginResponse = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
        Assert.Contains(loginResponse.Headers.GetValues("Set-Cookie"), value => value.StartsWith("EnigmaBudget.Auth=", StringComparison.Ordinal) && !value.Contains("; secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Production_https_session_keeps_secure_antiforgery_and_auth_cookie_policies()
    {
        using var production = _factory.CreateProductionFactory();
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, value => value.StartsWith("EnigmaBudget.Csrf=", StringComparison.Ordinal) && value.Contains("; secure", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(CookieSecurePolicy.Always, production.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, production.Services.GetRequiredService<IOptionsMonitor<AntiforgeryOptions>>().CurrentValue.Cookie.SecurePolicy);
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
    public async Task Synthetic_development_picker_lists_only_configured_profiles_when_explicitly_enabled()
    {
        var config = new Dictionary<string, string?>
        {
            ["Authentication:DevelopmentLoginEnabled"] = "true",
            ["Authentication:DevelopmentUsers:Users:0:Key"] = "alice",
            ["Authentication:DevelopmentUsers:Users:0:Subject"] = "synthetic-alice-subject",
            ["Authentication:DevelopmentUsers:Users:0:DisplayName"] = "Ana Prueba",
            ["Authentication:DevelopmentUsers:Users:0:Email"] = "ana@example.test",
        };
        using var factory = new ApiFactory(extraConfiguration: config);
        using var client = factory.CreateClient();
        var sessionResponse = await client.GetAsync("/api/auth/session");
        var sessionText = await sessionResponse.Content.ReadAsStringAsync();
        using var sessionJson = JsonDocument.Parse(sessionText);
        Assert.True(sessionJson.RootElement.GetProperty("developmentLoginEnabled").GetBoolean());
        var developmentUsers = sessionJson.RootElement.GetProperty("developmentUsers");
        Assert.Equal("alice", developmentUsers[0].GetProperty("key").GetString());
        Assert.Equal("Ana Prueba", developmentUsers[0].GetProperty("displayName").GetString());
        Assert.DoesNotContain("synthetic-alice-subject", sessionText, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@example.test", sessionText, StringComparison.Ordinal);

        var csrf = (await sessionResponse.Content.ReadFromJsonAsync<Session>())!.CsrfToken;
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { key = "alice" }) };
        login.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        var authenticatedSession = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("Ana Prueba", authenticatedSession.GetProperty("displayName").GetString());
        csrf = authenticatedSession.GetProperty("csrfToken").GetString()!;

        var unknown = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { key = "not-configured" }) };
        unknown.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(unknown)).StatusCode);

        using var production = factory.CreateProductionFactory();
        using var productionClient = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var productionSession = await productionClient.GetAsync("/api/auth/session");
        var productionText = await productionSession.Content.ReadAsStringAsync();
        Assert.Contains("\"developmentUsers\":[]", productionText, StringComparison.Ordinal);
        var productionCsrf = (await productionSession.Content.ReadFromJsonAsync<Session>())!.CsrfToken;
        var productionLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { key = "alice" }) };
        productionLogin.Headers.Add("X-CSRF-TOKEN", productionCsrf);
        Assert.Equal(HttpStatusCode.NotFound, (await productionClient.SendAsync(productionLogin)).StatusCode);

        config["Authentication:DevelopmentLoginEnabled"] = "false";
        using var disabledFactory = new ApiFactory(extraConfiguration: config);
        using var disabledClient = disabledFactory.CreateClient();
        var disabledSession = await disabledClient.GetAsync("/api/auth/session");
        var disabledText = await disabledSession.Content.ReadAsStringAsync();
        Assert.Contains("\"developmentUsers\":[]", disabledText, StringComparison.Ordinal);
        var disabledCsrf = (await disabledSession.Content.ReadFromJsonAsync<Session>())!.CsrfToken;
        var disabledLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { key = "alice" }) };
        disabledLogin.Headers.Add("X-CSRF-TOKEN", disabledCsrf);
        Assert.Equal(HttpStatusCode.NotFound, (await disabledClient.SendAsync(disabledLogin)).StatusCode);
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
    public async Task Archived_personal_cannot_complete_a_saved_onboarding_draft()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var account = new AccountInput(Guid.NewGuid(), "Apertura archivada", "Banco", "ARS", "250.00", "2026-10-01");
        var saved = await client.PutAsJsonAsync("/api/onboarding", new SaveRequest(0, [account], []));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await saved.Content.ReadFromJsonAsync<State>())!.Version;

        await using (var db = new EnigmaBudget.Api.Data.AppDbContext(new DbContextOptionsBuilder<EnigmaBudget.Api.Data.AppDbContext>().UseNpgsql(_factory.TestConnection).Options))
        {
            var workspace = await db.Workspaces.SingleAsync();
            workspace.IsArchived = true;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/onboarding", new SaveRequest(version, [], []))).StatusCode);
        var response = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<AccountSummary[]>("/api/accounts") ?? []);
    }

    [Fact]
    public async Task Archived_personal_keeps_idempotent_replay_of_an_already_completed_draft()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var account = new AccountInput(Guid.NewGuid(), "Apertura confirmada", "Banco", "ARS", "250.00", "2026-10-01");
        var saved = await client.PutAsJsonAsync("/api/onboarding", new SaveRequest(0, [account], []));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await saved.Content.ReadFromJsonAsync<State>())!.Version;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/onboarding/complete", new { version })).StatusCode);

        await using (var db = new EnigmaBudget.Api.Data.AppDbContext(new DbContextOptionsBuilder<EnigmaBudget.Api.Data.AppDbContext>().UseNpgsql(_factory.TestConnection).Options))
        {
            var workspace = await db.Workspaces.SingleAsync();
            workspace.IsArchived = true;
            await db.SaveChangesAsync();
        }

        var replay = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<State>())!.Completed);
        var accounts = await client.GetFromJsonAsync<AccountSummary[]>("/api/accounts");
        Assert.Equal("250.00", Assert.Single(accounts!).OpeningBalance);
    }

    [Fact]
    public async Task Concurrent_first_workspace_requests_recover_the_owner_collision()
    {
        using var factory = new ApiFactory();
        var barrier = factory.SaveBarrier;
        barrier.ArmWorkspaceCreate("same-owner", arrivals: 2);
        using var first = factory.IdentityClient("same-owner");
        using var second = factory.IdentityClient("same-owner");
        var firstRequest = first.GetAsync("/api/onboarding");
        var secondRequest = second.GetAsync("/api/onboarding");

        var responses = await Task.WhenAll(firstRequest, secondRequest);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var states = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<State>()));
        Assert.All(states, state => Assert.NotNull(state));
        Assert.All(states, state => Assert.Equal(0, state!.Version));
        Assert.Equal(1, await factory.CountWorkspacesAsync("same-owner"));

        using var other = factory.IdentityClient("other-owner");
        var otherState = await other.GetFromJsonAsync<State>("/api/onboarding");
        Assert.NotNull(otherState);
        Assert.Empty(otherState.Accounts);
        Assert.Equal(1, await factory.CountWorkspacesAsync("same-owner"));
        Assert.Equal(1, await factory.CountWorkspacesAsync("other-owner"));
    }

    [Fact]
    public async Task Save_loaded_before_confirmation_conflicts_after_confirmation_wins()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync();
        var account = new AccountInput(Guid.NewGuid(), "Caja", "Banco", "ARS", "123.45", "2026-10-01");
        var card = new CardInput(Guid.NewGuid(), "Visa", "Banco", 10, 20, "12.34", "0.00", "2026-10-15");
        var initial = await client.PutAsJsonAsync("/api/onboarding", new SaveRequest(0, [account], [card]));
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var version = (await initial.Content.ReadFromJsonAsync<State>())!.Version;

        factory.SaveBarrier.ArmDraftSave("stale-save-marker");
        var staleSave = client.PutAsJsonAsync("/api/onboarding", new SaveRequest(version,
            [account with { Name = "stale-save-marker" }], [card with { StatementDebtArs = "98.76" }]));
        await factory.SaveBarrier.WaitUntilBlockedAsync();

        var confirmed = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmedState = await confirmed.Content.ReadFromJsonAsync<State>();
        Assert.NotNull(confirmedState);
        Assert.True(confirmedState.Completed);
        Assert.Equal(version + 1, confirmedState.Version);
        factory.SaveBarrier.Release();

        var staleResponse = await staleSave;
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var finalState = await client.GetFromJsonAsync<State>("/api/onboarding");
        Assert.NotNull(finalState);
        Assert.True(finalState.Completed);
        Assert.Equal(version + 1, finalState.Version);
        Assert.Equal("Caja", Assert.Single(finalState.Accounts).Name);
        Assert.Equal("12.34", Assert.Single(finalState.Cards).StatementDebtArs);
        var confirmedAccounts = await client.GetFromJsonAsync<AccountSummary[]>("/api/accounts");
        var confirmedCards = await client.GetFromJsonAsync<CardSummary[]>("/api/cards");
        Assert.Equal("123.45", Assert.Single(confirmedAccounts!).OpeningBalance);
        Assert.Equal("12.34", Assert.Single(confirmedCards!).StatementDebtArs);

        var repeatedConfirmation = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });
        Assert.Equal(HttpStatusCode.OK, repeatedConfirmation.StatusCode);
        Assert.Equal(version + 1, (await repeatedConfirmation.Content.ReadFromJsonAsync<State>())!.Version);
        Assert.Single((await client.GetFromJsonAsync<AccountSummary[]>("/api/accounts"))!);
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
    [InlineData("amount-min")]
    [InlineData("amount-high")]
    [InlineData("amount-subcent-long")]
    [InlineData("currency")]
    [InlineData("duplicate-id")]
    public async Task Invalid_completion_inputs_are_rejected(string invalidKind)
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        var currency = invalidKind == "currency" ? "EUR" : "ARS";
        var amount = invalidKind switch { "amount" => "1.001", "amount-min" => decimal.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture), "amount-high" => "10000000000000000", "amount-subcent-long" => "0.00000000000000000000000000001", _ => "1.00" };
        var date = invalidKind == "date" ? "2026-02-30" : "2026-02-28";
        var account = new AccountInput(id, "Cuenta", "Banco", currency, amount, date);
         CardInput[] cards = invalidKind == "duplicate-id" ? [new CardInput(id, "Tarjeta", "Banco", null, null, "0", "0", "2026-02-28")] : [];
        var saved = await client.PutAsJsonAsync("/api/onboarding", new SaveRequest(0, [account], cards));
        if (invalidKind == "duplicate-id") { Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode); return; }
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await saved.Content.ReadFromJsonAsync<State>())!.Version;
        var response = await client.PostAsJsonAsync("/api/onboarding/complete", new { version });
        if (invalidKind == "amount-min") Console.WriteLine(await response.Content.ReadAsStringAsync());
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
    private readonly bool _relaxSecureCookiesForTests;
    private readonly string _environment;
    private readonly TimeProvider? _timeProvider;
    private readonly IReadOnlyDictionary<string, string?>? _extraConfiguration;
    public QueuedGoogleValidator GoogleValidator { get; }
    public SaveChangesBarrierInterceptor SaveBarrier { get; } = new();
    public WorkspaceLockCommandBarrierInterceptor WorkspaceLockBarrier { get; } = new();
    public LedgerInsertBarrierInterceptor LedgerInsertBarrier { get; } = new();
    internal string TestConnection => _connection;
    public ApiFactory(bool relaxSecureCookiesForTests = true, string environment = "Development", TimeProvider? timeProvider = null, IReadOnlyDictionary<string, string?>? extraConfiguration = null, QueuedGoogleValidator? googleValidator = null)
    {
        _relaxSecureCookiesForTests = relaxSecureCookiesForTests;
        _environment = environment;
        _timeProvider = timeProvider;
        _extraConfiguration = extraConfiguration;
        GoogleValidator = googleValidator ?? new QueuedGoogleValidator();
        var testConnection = Environment.GetEnvironmentVariable("ENIGMABUDGET_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(testConnection)) throw new InvalidOperationException("Set ENIGMABUDGET_TEST_CONNECTION to an isolated PostgreSQL test database.");
        var b = new NpgsqlConnectionStringBuilder(testConnection);
        using var c = new NpgsqlConnection(b.ConnectionString); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = $"CREATE SCHEMA \"{_schema}\""; cmd.ExecuteNonQuery();
        b.SearchPath = _schema; _connection = b.ConnectionString;
    }
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureTestServices(services =>
        {
            if (_timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            }
            services.AddSingleton(SaveBarrier);
            services.AddDbContext<AppDbContext>((serviceProvider, options) => options.AddInterceptors(serviceProvider.GetRequiredService<SaveChangesBarrierInterceptor>(), WorkspaceLockBarrier, LedgerInsertBarrier));
            services.AddAuthentication(o => { o.DefaultScheme = "TestOrCookie"; o.DefaultAuthenticateScheme = "TestOrCookie"; o.DefaultChallengeScheme = "TestOrCookie"; }).AddPolicyScheme("TestOrCookie", "Test identity or cookie", o => o.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("Authorization") ? NativeBearerHandler.SchemeName : context.Request.Headers.ContainsKey("X-Test-Identity") ? "TestIdentity" : CookieAuthenticationDefaults.AuthenticationScheme).AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>("TestIdentity", _ => { });
            services.RemoveAll<IGoogleIdTokenValidator>();
            services.AddSingleton<IGoogleIdTokenValidator>(GoogleValidator);
            if (_relaxSecureCookiesForTests)
            {
                services.Configure<AntiforgeryOptions>(o => o.Cookie.SecurePolicy = CookieSecurePolicy.None);
                services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => o.Cookie.SecurePolicy = CookieSecurePolicy.None);
            }
        });
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
            ["ConnectionStrings:Default"] = _connection,
            ["Authentication:DevelopmentLoginEnabled"] = "true",
            ["Authentication:Google:ClientId"] = "",
            ["Authentication:Google:ClientSecret"] = ""
            };
            if (_extraConfiguration is not null)
                foreach (var item in _extraConfiguration) values[item.Key] = item.Value;
            config.AddInMemoryCollection(values);
        });
    }

    public HttpClient CreateProductionClient()
    {
        _productionFactory = CreateProductionFactory();
        return _productionFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    }

    public ApiFactory CreateProductionFactory() => new(relaxSecureCookiesForTests: false, environment: "Production");

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
    public HttpClient IdentityClient(string identity)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Identity", identity);
        return client;
    }
    public async Task<int> CountWorkspacesAsync(string owner)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);
        return await db.Workspaces.CountAsync(x => x.OwnerSubject == owner);
    }
    private sealed record SessionDto(string CsrfToken);
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { _productionFactory?.Dispose(); using var c = new NpgsqlConnection(Environment.GetEnvironmentVariable("ENIGMABUDGET_TEST_CONNECTION")!); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE"; cmd.ExecuteNonQuery(); using var isolated = new NpgsqlConnection(_connection); NpgsqlConnection.ClearPool(isolated); } }
}

public sealed class LedgerInsertBarrierInterceptor : DbCommandInterceptor
{
    private int _armed;
    private TaskCompletionSource _arrived = NewSignal();
    private TaskCompletionSource _release = NewSignal();
    private int _observeAccountLock;
    private TaskCompletionSource _accountLock = NewSignal();
    private int _observeWorkspaceLock;
    private TaskCompletionSource _workspaceLock = NewSignal();
    public void Arm() { _arrived = NewSignal(); _release = NewSignal(); Volatile.Write(ref _armed, 1); }
    public void ObserveNextAccountLock() { _accountLock = NewSignal(); Volatile.Write(ref _observeAccountLock, 1); }
    public void ObserveNextWorkspaceLock() { _workspaceLock = NewSignal(); Volatile.Write(ref _observeWorkspaceLock, 1); }
    public Task WaitUntilBlockedAsync() => _arrived.Task;
    public Task WaitForAccountLockAsync() => _accountLock.Task;
    public Task WaitForWorkspaceLockAsync() => _workspaceLock.Task;
    public void Release() => _release.TrySetResult();
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"Accounts\"", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _observeAccountLock, 0) == 1)
            _accountLock.TrySetResult();
        if (command.CommandText.Contains("\"Workspaces\"", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _observeWorkspaceLock, 0) == 1)
            _workspaceLock.TrySetResult();
        if (command.CommandText.Contains("INSERT INTO \"LedgerEntries\"", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _armed, 0) == 1)
            return PauseReaderAsync(command, eventData, result, cancellationToken);
        return ValueTask.FromResult(result);
    }
    private async ValueTask<InterceptionResult<DbDataReader>> PauseReaderAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken)
    {
        _arrived.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return result;
    }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"Accounts\"", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _observeAccountLock, 0) == 1)
            _accountLock.TrySetResult();
        if (command.CommandText.Contains("INSERT INTO \"LedgerEntries\"", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            _arrived.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class SaveChangesBarrierInterceptor : SaveChangesInterceptor
{
    private string? _workspaceOwner;
    private int _workspaceArrivals;
    private TaskCompletionSource _workspaceGate = NewSignal();
    private string? _draftMarker;
    private TaskCompletionSource _draftBlocked = NewSignal();
    private TaskCompletionSource _draftRelease = NewSignal();

    public void ArmWorkspaceCreate(string owner, int arrivals)
    {
        _workspaceOwner = owner;
        _workspaceArrivals = arrivals;
        _workspaceGate = NewSignal();
    }

    public void ArmDraftSave(string marker)
    {
        _draftMarker = marker;
        _draftBlocked = NewSignal();
        _draftRelease = NewSignal();
    }

    public Task WaitUntilBlockedAsync() => _draftBlocked.Task;
    public void Release() => _draftRelease.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return result;
        var addedOwner = context.ChangeTracker.Entries<Workspace>().FirstOrDefault(entry => entry.State == EntityState.Added)?.Entity.OwnerSubject;
        if (_workspaceOwner is not null && addedOwner == _workspaceOwner)
        {
            if (Interlocked.Decrement(ref _workspaceArrivals) == 0)
            {
                _workspaceOwner = null;
                _workspaceGate.TrySetResult();
            }
            await _workspaceGate.Task.WaitAsync(cancellationToken);
        }

        if (_draftMarker is not null && context.ChangeTracker.Entries<OnboardingDraft>().Any(entry => entry.State == EntityState.Modified && entry.Entity.AccountsJson.Contains(_draftMarker, StringComparison.Ordinal)))
        {
            _draftMarker = null;
            _draftBlocked.TrySetResult();
            await _draftRelease.Task.WaitAsync(cancellationToken);
        }
        return result;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class WorkspaceLockCommandBarrierInterceptor : DbCommandInterceptor
{
    private int _armed;
    private TaskCompletionSource _arrived = NewSignal();
    private TaskCompletionSource _release = NewSignal();

    public void Arm()
    {
        _arrived = NewSignal();
        _release = NewSignal();
        Volatile.Write(ref _armed, 1);
    }

    public Task WaitUntilBlockedAsync() => _arrived.Task;
    public void Release() => _release.TrySetResult();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"Workspaces\"", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
            && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            _arrived.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class TestIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers["X-Test-Identity"].ToString();
        if (string.IsNullOrWhiteSpace(subject)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, subject) };
        var email = Request.Headers["X-Test-Email"].ToString();
        if (!string.IsNullOrWhiteSpace(email)) claims.Add(new Claim(ClaimTypes.Email, email));
        var verified = Request.Headers["X-Test-Email-Verified"].ToString();
        if (!string.IsNullOrWhiteSpace(verified)) claims.Add(new Claim("email_verified", verified));
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
