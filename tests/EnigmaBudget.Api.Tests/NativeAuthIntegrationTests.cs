using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EnigmaBudget.Api.Tests;

public sealed class NativeAuthIntegrationTests
{
    [Fact]
    public async Task Challenge_is_single_use_and_expires_after_two_minutes()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z"));
        using var factory = NativeFactory(clock);
        using var client = HttpsClient(factory);

        using var challenge = await client.PostAsJsonAsync("/api/auth/native/challenge", new { platform = "android" });
        Assert.Equal(HttpStatusCode.OK, challenge.StatusCode);
        var value = await challenge.Content.ReadFromJsonAsync<ChallengeDto>();
        Assert.NotNull(value);
        Assert.Equal(clock.GetUtcNow().AddMinutes(2), value.ExpiresAt);

        factory.GoogleValidator.Enqueue(value.Nonce, "native-user-one");
        using var exchange = await client.PostAsJsonAsync("/api/auth/native/exchange", new { challengeId = value.ChallengeId, idToken = "valid-one", deviceName = "Teléfono" });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var tokens = await exchange.Content.ReadFromJsonAsync<TokenDto>();
        Assert.Equal(clock.GetUtcNow().AddMinutes(5), tokens!.AccessExpiresAt);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        clock.Advance(TimeSpan.FromMinutes(5));
        using var expiredAccess = await client.GetAsync("/api/auth/native/session");
        Assert.Equal(HttpStatusCode.Unauthorized, expiredAccess.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", expiredAccess.Headers.WwwAuthenticate.Single().ToString());
        client.DefaultRequestHeaders.Authorization = null;

        factory.GoogleValidator.Enqueue(value.Nonce, "native-user-one");
        using var replay = await client.PostAsJsonAsync("/api/auth/native/exchange", new { challengeId = value.ChallengeId, idToken = "valid-replay", deviceName = "Teléfono" });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using var expired = await client.PostAsJsonAsync("/api/auth/native/challenge", new { platform = "android" });
        var second = await expired.Content.ReadFromJsonAsync<ChallengeDto>();
        clock.Advance(TimeSpan.FromMinutes(2));
        factory.GoogleValidator.Enqueue(second!.Nonce, "native-user-two");
        using var afterExpiry = await client.PostAsJsonAsync("/api/auth/native/exchange", new { challengeId = second.ChallengeId, idToken = "valid-expired", deviceName = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry.StatusCode);
    }

    [Fact]
    public async Task Exchange_rejects_nonce_mismatch_and_unconfigured_google()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")), googleAudience: "");
        using var client = HttpsClient(factory);
        using var missingConfig = await client.PostAsJsonAsync("/api/auth/native/exchange", new { challengeId = Guid.NewGuid(), idToken = "anything", deviceName = (string?)null });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingConfig.StatusCode);
        using var options = JsonDocument.Parse(await (await client.GetAsync("/api/auth/native/options")).Content.ReadAsStringAsync());
        Assert.False(options.RootElement.GetProperty("googleConfigured").GetBoolean());
        Assert.True(options.RootElement.GetProperty("developmentLoginEnabled").GetBoolean());
        Assert.False(options.RootElement.TryGetProperty("csrfToken", out _));
        Assert.False(options.RootElement.TryGetProperty("clientSecret", out _));

        using var configuredFactory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var configuredClient = HttpsClient(configuredFactory);
        using var challenge = await configuredClient.PostAsJsonAsync("/api/auth/native/challenge", new { platform = "android" });
        var value = await challenge.Content.ReadFromJsonAsync<ChallengeDto>();
        configuredFactory.GoogleValidator.Enqueue("different-nonce", "native-user");
        using var mismatch = await configuredClient.PostAsJsonAsync("/api/auth/native/exchange", new { challengeId = value!.ChallengeId, idToken = "valid-mismatch", deviceName = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, mismatch.StatusCode);
        Assert.Empty(mismatch.Headers.WwwAuthenticate);
        await using var db = Db(configuredFactory);
        Assert.Empty(await db.NativeSessions.ToArrayAsync());
    }

    [Fact]
    public async Task Exchange_uses_raw_verified_sub_without_email_merge()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = HttpsClient(factory);
        await client.GetAsync("/api/auth/native/options");
        await using (var existing = Db(factory))
        {
            existing.ApplicationUsers.Add(new EnigmaBudget.Api.Workspaces.ApplicationUser
            {
                Subject = "other-google-sub", DisplayName = "Otra cuenta", Email = "same@example.test", EmailVerified = true,
                CreatedAt = DateTimeOffset.Parse("2026-10-25T11:00:00Z"),
            });
            await existing.SaveChangesAsync();
        }
        using var challenge = await client.PostAsJsonAsync("/api/auth/native/challenge", new { platform = "android" });
        var value = await challenge.Content.ReadFromJsonAsync<ChallengeDto>();
        factory.GoogleValidator.Enqueue(value!.Nonce, "opaque-google-sub-987", "same@example.test");
        using var response = await client.PostAsJsonAsync("/api/auth/native/exchange", new { challengeId = value.ChallengeId, idToken = "verified-token", deviceName = "Tablet" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.NotNull(tokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var session = await client.GetAsync("/api/auth/native/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var info = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.True(info.RootElement.GetProperty("authenticated").GetBoolean());
        var sessionUserId = info.RootElement.GetProperty("userId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionUserId));
        using var me = JsonDocument.Parse(await (await client.GetAsync("/api/me")).Content.ReadAsStringAsync());
        Assert.Equal(sessionUserId, me.RootElement.GetProperty("id").GetGuid().ToString());
        await using var db = Db(factory);
        Assert.Equal(1, await db.ApplicationUsers.CountAsync(x => x.Subject == "opaque-google-sub-987"));
        Assert.Equal(2, await db.ApplicationUsers.CountAsync(x => x.Email == "same@example.test"));
        Assert.Equal("same@example.test", await db.ApplicationUsers.Where(x => x.Subject == "opaque-google-sub-987").Select(x => x.Email).SingleAsync());
        Assert.Equal("opaque-google-sub-987", await db.NativeSessions.Select(x => x.Subject).SingleAsync());
    }

    [Fact]
    public async Task Native_verified_profile_can_preview_and_accept_existing_invitation()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var owner = factory.IdentityClient("native-invite-owner");
        owner.DefaultRequestHeaders.Add("X-Test-Email", "owner-invite@example.test");
        owner.DefaultRequestHeaders.Add("X-Test-Email-Verified", "true");
        var webSession = await owner.GetFromJsonAsync<JsonElement>("/api/auth/session");
        owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN", webSession.GetProperty("csrfToken").GetString());
        using var householdResponse = await owner.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar de invitación" });
        Assert.Equal(HttpStatusCode.Created, householdResponse.StatusCode);
        using var householdJson = JsonDocument.Parse(await householdResponse.Content.ReadAsStringAsync());
        var householdId = householdJson.RootElement.GetProperty("id").GetGuid();
        using var invitationResponse = await owner.PostAsJsonAsync($"/api/workspaces/{householdId}/invitations", new { id = Guid.NewGuid(), email = "native-invitee@example.test", role = "Reader" });
        Assert.Equal(HttpStatusCode.OK, invitationResponse.StatusCode);
        using var invitation = JsonDocument.Parse(await invitationResponse.Content.ReadAsStringAsync());
        var shareUrl = invitation.RootElement.GetProperty("shareUrl").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(new Uri("https://localhost"), shareUrl).Fragment.TrimStart('#').Split('=', 2).Last());

        using var native = HttpsClient(factory);
        using var challenge = await native.PostAsJsonAsync("/api/auth/native/challenge", new { platform = "android" });
        var challengeValue = await challenge.Content.ReadFromJsonAsync<ChallengeDto>();
        factory.GoogleValidator.Enqueue(challengeValue!.Nonce, "native-invitee-sub", "native-invitee@example.test");
        using var exchange = await native.PostAsJsonAsync("/api/auth/native/exchange", new { challengeId = challengeValue.ChallengeId, idToken = "verified-native-invitee", deviceName = "Teléfono" });
        var sessionTokens = await exchange.Content.ReadFromJsonAsync<TokenDto>();
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        native.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionTokens!.AccessToken);

        using var preview = await native.PostAsJsonAsync("/api/invitations/preview", new { token });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var accepted = await native.PostAsJsonAsync("/api/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Native_auth_rejects_oversized_body_and_rate_limits()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = HttpsClient(factory);
        using var oversized = new StringContent("{\"platform\":\"android\",\"padding\":\"" + new string('x', 17_000) + "\"}", System.Text.Encoding.UTF8, "application/json");
        using var tooLarge = await client.PostAsync("/api/auth/native/challenge", oversized);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 11; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/native/challenge", new { platform = "android" });
            last = response.StatusCode;
        }
        Assert.Equal((HttpStatusCode)429, last);
    }

    [Fact]
    public async Task Native_auth_requires_https()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        var requests = new (string Path, object Body)[]
        {
            ("/api/auth/native/challenge", new { platform = "android" }),
            ("/api/auth/native/exchange", new { challengeId = Guid.NewGuid(), idToken = "x", deviceName = (string?)null }),
            ("/api/auth/native/refresh", new { refreshToken = "x" }),
            ("/api/auth/native/development", new { key = (string?)null, deviceName = (string?)null }),
            ("/api/auth/native/logout", new { refreshToken = "x" }),
        };
        foreach (var (path, body) in requests)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            Assert.Equal((HttpStatusCode)426, response.StatusCode);
        }
    }

    [Fact]
    public async Task Google_validation_deadline_and_inflight_cap_are_fail_closed()
    {
        var gate = new BoundedGoogleValidation<string>(2, TimeSpan.FromSeconds(5));
        using (var preCancelled = new CancellationTokenSource())
        {
            preCancelled.Cancel();
            var called = false;
            Assert.Null(await gate.ValidateAsync(() => { called = true; return Task.FromResult("must-not-run"); }, preCancelled.Token));
            Assert.False(called);
        }
        var firstCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = new CancellationTokenSource();
        var firstWait = gate.ValidateAsync(() =>
        {
            firstStarted.TrySetResult();
            return firstCompletion.Task;
        }, cancelled.Token);
        await firstStarted.Task;
        cancelled.Cancel();
        Assert.Null(await firstWait);

        var secondCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWait = gate.ValidateAsync(() => secondCompletion.Task, CancellationToken.None);
        Assert.Null(await gate.ValidateAsync(() => Task.FromResult("excess"), CancellationToken.None));
        firstCompletion.SetResult("finished-underlying-first");
        string? allowed = null;
        for (var attempt = 0; attempt < 100 && allowed is null; attempt++)
        {
            allowed = await gate.ValidateAsync(() => Task.FromResult("allowed-after-underlying-finished"), CancellationToken.None);
            if (allowed is null) await Task.Delay(1);
        }
        Assert.Equal("allowed-after-underlying-finished", allowed);
        secondCompletion.SetResult("finished-underlying-second");
        Assert.Equal("finished-underlying-second", await secondWait);

        var timeoutGate = new BoundedGoogleValidation<string>(1, TimeSpan.FromMilliseconds(20));
        var timedOutOperation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Null(await timeoutGate.ValidateAsync(() => timedOutOperation.Task, CancellationToken.None));
        Assert.Null(await timeoutGate.ValidateAsync(() => Task.FromResult("still-full"), CancellationToken.None));
        timedOutOperation.SetResult("underlying-finished");
        string? available = null;
        for (var attempt = 0; attempt < 100 && available is null; attempt++)
        {
            available = await timeoutGate.ValidateAsync(() => Task.FromResult("available"), CancellationToken.None);
            if (available is null) await Task.Delay(1);
        }
        Assert.Equal("available", available);

        var faultGate = new BoundedGoogleValidation<string>(1, TimeSpan.FromMilliseconds(20));
        var faultedOperation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Null(await faultGate.ValidateAsync(() => faultedOperation.Task, CancellationToken.None));
        faultedOperation.SetException(new InvalidOperationException("controllable verifier failure"));
        string? afterFault = null;
        for (var attempt = 0; attempt < 100 && afterFault is null; attempt++)
        {
            afterFault = await faultGate.ValidateAsync(() => Task.FromResult("available-after-fault"), CancellationToken.None);
            if (afterFault is null) await Task.Delay(1);
        }
        Assert.Equal("available-after-fault", afterFault);
    }

    [Fact]
    public async Task Refresh_rotation_rejects_replay_and_revokes_family()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = HttpsClient(factory);
        var first = await DevelopmentLoginAsync(client);
        using var rotated = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = await rotated.Content.ReadFromJsonAsync<TokenDto>();
        using var replay = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var familyRevoked = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = second!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, familyRevoked.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        using var access = await client.GetAsync("/api/auth/native/session");
        Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_has_one_winner_and_replay_revokes_family_in_postgres()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = HttpsClient(factory);
        var first = await DevelopmentLoginAsync(client);
        using var a = new HttpRequestMessage(HttpMethod.Post, "/api/auth/native/refresh") { Content = JsonContent.Create(new { refreshToken = first.RefreshToken }) };
        using var b = new HttpRequestMessage(HttpMethod.Post, "/api/auth/native/refresh") { Content = JsonContent.Create(new { refreshToken = first.RefreshToken }) };
        var responses = await Task.WhenAll(client.SendAsync(a), client.SendAsync(b));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var winner = await responses.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<TokenDto>();
        using var familyRevoked = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = winner!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, familyRevoked.StatusCode);
    }

    [Fact]
    public async Task Refresh_does_not_extend_thirty_day_absolute_expiry()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z"));
        using var factory = NativeFactory(clock);
        using var client = HttpsClient(factory);
        var tokens = await DevelopmentLoginAsync(client);
        Assert.Equal(clock.GetUtcNow().AddDays(30), tokens.SessionExpiresAt);
        clock.Advance(TimeSpan.FromDays(29));
        using var refresh = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await refresh.Content.ReadFromJsonAsync<TokenDto>();
        Assert.Equal(tokens.SessionExpiresAt, rotated!.SessionExpiresAt);
        clock.Advance(TimeSpan.FromDays(1));
        using var expired = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Native_logout_revokes_access_and_refresh()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = HttpsClient(factory);
        var tokens = await DevelopmentLoginAsync(client);
        using var logout = await client.PostAsJsonAsync("/api/auth/native/logout", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var access = await client.GetAsync("/api/auth/native/session");
        Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using var refresh = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Development_native_session_can_read_and_write_only_its_workspace_then_refresh_replay_and_logout_revoke_it()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = HttpsClient(factory);
        var first = await DevelopmentLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);

        using var me = JsonDocument.Parse(await (await client.GetAsync("/api/me")).Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(me.RootElement.GetProperty("id").GetString()));
        using var workspaceList = JsonDocument.Parse(await (await client.GetAsync("/api/workspaces")).Content.ReadAsStringAsync());
        var personalWorkspaceId = workspaceList.RootElement.EnumerateArray().Single().GetProperty("id").GetGuid();
        client.DefaultRequestHeaders.Add("X-Workspace-Id", personalWorkspaceId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounts")).StatusCode);

        var accountId = Guid.NewGuid();
        using var account = await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja de aceptación", bankName = "Banco", currency = "ARS",
            openingBalance = "100.00", openingDate = "2026-10-01",
        });
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        using var operation = await client.PostAsJsonAsync("/api/operations", new
        {
            id = Guid.NewGuid(), kind = "Expense", accountId, date = "2026-10-11",
            description = "Compra de prueba", category = "Comida", amount = "12.50", paid = true,
        });
        Assert.Equal(HttpStatusCode.OK, operation.StatusCode);
        using var finance = await client.GetAsync("/api/finance?period=2026-10");
        Assert.Equal(HttpStatusCode.OK, finance.StatusCode);

        using var stranger = HttpsClient(factory);
        var strangerTokens = await DevelopmentLoginAsync(stranger, "native-stranger-key");
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", strangerTokens.AccessToken);
        var strangerWorkspaces = await stranger.GetFromJsonAsync<JsonElement[]>("/api/workspaces");
        using var unauthorizedWorkspace = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        unauthorizedWorkspace.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        unauthorizedWorkspace.Headers.Add("X-Workspace-Id", strangerWorkspaces!.Single().GetProperty("id").GetGuid().ToString());
        using var denied = await client.SendAsync(unauthorizedWorkspace);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var rotated = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = await rotated.Content.ReadFromJsonAsync<TokenDto>();
        using var replay = await client.PostAsJsonAsync("/api/auth/native/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second!.AccessToken);
        using var revokedAfterReplay = await client.GetAsync("/api/auth/native/session");
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAfterReplay.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Workspace-Id");
        var logoutTokens = await DevelopmentLoginAsync(client);
        using var logout = await client.PostAsJsonAsync("/api/auth/native/logout", new { refreshToken = logoutTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", logoutTokens.AccessToken);
        using var afterLogout = await client.GetAsync("/api/auth/native/session");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Native_development_requires_both_opt_ins_and_is_404_in_production()
    {
        using var noOptIn = new ApiFactory(extraConfiguration: new Dictionary<string, string?> { ["Authentication:DevelopmentLoginEnabled"] = "false", ["Authentication:Native:DevelopmentLoginEnabled"] = "true" });
        using var noOptInClient = HttpsClient(noOptIn);
        using var disabled = await noOptInClient.PostAsJsonAsync("/api/auth/native/development", new { key = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);

        using var production = new ApiFactory(environment: "Production", extraConfiguration: new Dictionary<string, string?> { ["Authentication:DevelopmentLoginEnabled"] = "true", ["Authentication:Native:DevelopmentLoginEnabled"] = "true" });
        using var productionClient = HttpsClient(production);
        using var hidden = await productionClient.PostAsJsonAsync("/api/auth/native/development", new { key = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task Native_development_key_uses_the_configured_verified_server_profile()
    {
        using var factory = new ApiFactory(timeProvider: new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")), extraConfiguration: new Dictionary<string, string?>
        {
            ["Authentication:Native:DevelopmentLoginEnabled"] = "true",
            ["Authentication:DevelopmentUsers:Users:0:Key"] = "local-test-key",
            ["Authentication:DevelopmentUsers:Users:0:Subject"] = "configured-native-development-sub",
            ["Authentication:DevelopmentUsers:Users:0:DisplayName"] = "Perfil local verificado",
            ["Authentication:DevelopmentUsers:Users:0:Email"] = "configured-native@example.test",
        });
        using var client = HttpsClient(factory);
        using var response = await client.PostAsJsonAsync("/api/auth/native/development", new { key = "local-test-key", deviceName = "Emulador de pruebas" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<TokenDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        using var nativeSession = await client.GetAsync("/api/auth/native/session");
        Assert.Equal(HttpStatusCode.OK, nativeSession.StatusCode);
        using var me = JsonDocument.Parse(await (await client.GetAsync("/api/me")).Content.ReadAsStringAsync());
        Assert.Equal("Perfil local verificado", me.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("configured-native@example.test", me.RootElement.GetProperty("email").GetString());
        Assert.True(me.RootElement.GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task Invalid_bearer_never_bypasses_cookie_csrf()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var cookie = await factory.AuthenticatedClientAsync();
        cookie.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var cookieWrite = await cookie.PostAsJsonAsync("/api/accounts", new { id = Guid.NewGuid(), name = "Cuenta", bankName = "Banco", currency = "ARS", openingBalance = "1.00", openingDate = "2026-10-25" });
        Assert.Equal(HttpStatusCode.BadRequest, cookieWrite.StatusCode);

        using var native = HttpsClient(factory);
        var tokens = await DevelopmentLoginAsync(native);
        native.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        await native.GetAsync("/api/auth/native/session");
        using var nativeWrite = await native.PostAsJsonAsync("/api/accounts", new { id = Guid.NewGuid(), name = "Cuenta nativa", bankName = "Banco", currency = "ARS", openingBalance = "1.00", openingDate = "2026-10-25" });
        Assert.Equal(HttpStatusCode.OK, nativeWrite.StatusCode);

        using var validCookie = await factory.AuthenticatedClientAsync("cookie-csrf-user");
        validCookie.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-native-access");
        using var write = await validCookie.PostAsJsonAsync("/api/accounts", new { id = Guid.NewGuid(), name = "Cuenta sin fallback", bankName = "Banco", currency = "ARS", openingBalance = "1.00", openingDate = "2026-10-25" });
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", write.Headers.WwwAuthenticate.Single().ToString());
    }

    [Fact]
    public async Task Invalid_bearer_with_valid_cookie_fails_closed_and_does_not_fallback()
    {
        using var factory = NativeFactory(new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var cookie = await factory.AuthenticatedClientAsync("cookie-fallback-user");
        Assert.Equal(HttpStatusCode.OK, (await cookie.GetAsync("/api/workspaces")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookie.GetAsync("/api/auth/native/session")).StatusCode);
        cookie.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-native-access");
        using var session = await cookie.GetAsync("/api/workspaces");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", session.Headers.WwwAuthenticate.Single().ToString());
    }

    private static ApiFactory NativeFactory(MutableTimeProvider clock, string googleAudience = "native-test-audience") => new(
        timeProvider: clock,
        googleValidator: null,
        extraConfiguration: new Dictionary<string, string?>
        {
            ["Authentication:Native:DevelopmentLoginEnabled"] = "true",
            ["Authentication:Native:Google:Audiences:0"] = googleAudience,
            ["Authentication:DevelopmentUsers:Users:0:Key"] = "native-stranger-key",
            ["Authentication:DevelopmentUsers:Users:0:Subject"] = "native-stranger-subject",
            ["Authentication:DevelopmentUsers:Users:0:DisplayName"] = "Usuario nativo distinto",
            ["Authentication:DevelopmentUsers:Users:0:Email"] = "native-stranger@example.test",
        });
    private static HttpClient HttpsClient(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static AppDbContext Db(ApiFactory factory) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);

    private static async Task<TokenDto> DevelopmentLoginAsync(HttpClient client, string? key = null)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/native/development", new { key });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenDto>())!;
    }

    private sealed record ChallengeDto(Guid ChallengeId, string Nonce, DateTimeOffset ExpiresAt);
    private sealed record TokenDto(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset SessionExpiresAt);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}

public sealed class QueuedGoogleValidator : IGoogleIdTokenValidator
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<ValidatedGoogleIdentity?> _identities = new();
    public void Enqueue(string nonce, string subject, string? email = "verified@example.test") =>
        _identities.Enqueue(new ValidatedGoogleIdentity(subject, nonce, "Usuario Google", email, email is not null));
    public Task<ValidatedGoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken) =>
        Task.FromResult(_identities.TryDequeue(out var identity) ? identity : null);
}
