using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EnigmaBudget.Api.Identity;

namespace EnigmaBudget.Api.Tests;

public sealed class GoogleAndDevelopmentIdentityTests
{
    [Fact]
    public async Task Fresh_verified_provider_profile_refreshes_email_without_changing_subject_identity()
    {
        using var factory = new ApiFactory();
        using var first = factory.IdentityClient("stable-google-subject");
        first.DefaultRequestHeaders.Add("X-Test-Email", "old@example.test");
        first.DefaultRequestHeaders.Add("X-Test-Email-Verified", "false");
        await first.GetAsync("/api/auth/session");
        var before = await first.GetFromJsonAsync<JsonElement>("/api/me");

        using var refreshed = factory.IdentityClient("stable-google-subject");
        refreshed.DefaultRequestHeaders.Add("X-Test-Email", "new@example.test");
        refreshed.DefaultRequestHeaders.Add("X-Test-Email-Verified", "true");
        await refreshed.GetAsync("/api/auth/session");
        var after = await refreshed.GetFromJsonAsync<JsonElement>("/api/me");

        Assert.Equal(before.GetProperty("id").GetGuid(), after.GetProperty("id").GetGuid());
        Assert.Equal("new@example.test", after.GetProperty("email").GetString());
        Assert.True(after.GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public void Google_email_verification_claim_comes_only_from_boolean_trusted_profile_field()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "google-subject")]);
        using var verified = JsonDocument.Parse("{\"email\":\"verified@example.test\",\"email_verified\":true}");
        GoogleIdentityClaims.EnrichFromTrustedProfile(verified.RootElement, identity);
        Assert.Equal("true", identity.FindFirst("email_verified")?.Value);
        Assert.Equal("verified@example.test", identity.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("google-subject", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);

        using var unverified = JsonDocument.Parse("{\"email\":\"unverified@example.test\",\"email_verified\":false}");
        GoogleIdentityClaims.EnrichFromTrustedProfile(unverified.RootElement, identity);
        Assert.Equal("false", identity.FindFirst("email_verified")?.Value);
        using var missing = JsonDocument.Parse("{\"email\":\"missing-flag@example.test\"}");
        GoogleIdentityClaims.EnrichFromTrustedProfile(missing.RootElement, identity);
        Assert.Null(identity.FindFirst("email_verified"));
        Assert.Equal("google-subject", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task Development_login_selects_only_configured_synthetic_users_and_remains_absent_in_production()
    {
        var config = new Dictionary<string, string?>
        {
            ["Authentication:DevelopmentUsers:Users:0:Key"] = "browser-reader",
            ["Authentication:DevelopmentUsers:Users:0:Subject"] = "synthetic-reader-subject",
            ["Authentication:DevelopmentUsers:Users:0:DisplayName"] = "Lector de prueba",
            ["Authentication:DevelopmentUsers:Users:0:Email"] = "reader@example.test",
        };
        using var development = new ApiFactory(extraConfiguration: config);
        using var client = development.CreateClient();
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var csrf = session.GetProperty("csrfToken").GetString();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { key = "browser-reader" }) };
        login.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("reader@example.test", me.GetProperty("email").GetString());
        Assert.True(me.GetProperty("emailVerified").GetBoolean());

        using var unlistedClient = development.CreateClient();
        var unlistedSession = await unlistedClient.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var unlistedCsrf = unlistedSession.GetProperty("csrfToken").GetString();
        using var unlisted = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { key = "not-allowlisted" }) };
        unlisted.Headers.Add("X-CSRF-TOKEN", unlistedCsrf);
        Assert.Equal(HttpStatusCode.NotFound, (await unlistedClient.SendAsync(unlisted)).StatusCode);
        using var spoof = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { subject = "forged-admin", email = "owner@example.test", emailVerified = true }) };
        spoof.Headers.Add("X-CSRF-TOKEN", unlistedCsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await unlistedClient.SendAsync(spoof)).StatusCode);
        var localMe = await unlistedClient.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("Usuario de desarrollo", localMe.GetProperty("displayName").GetString());
        Assert.Null(localMe.GetProperty("email").GetString());
        Assert.False(localMe.GetProperty("emailVerified").GetBoolean());

        using var production = new ApiFactory(environment: "Production", extraConfiguration: config);
        using var prodClient = production.CreateClient();
        var prodSession = await prodClient.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.False(prodSession.GetProperty("developmentLoginEnabled").GetBoolean());
        using var rejected = new HttpRequestMessage(HttpMethod.Post, "/api/auth/development") { Content = JsonContent.Create(new { key = "malicious" }) };
        rejected.Headers.Add("X-CSRF-TOKEN", prodSession.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await prodClient.SendAsync(rejected)).StatusCode);
    }
}
