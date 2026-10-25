using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.ExchangeRates;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class ExchangeRateIntegrationTests
{
    private const string ValidQuote = """[{"moneda":"USD","casa":"oficial","nombre":"Oficial","compra":1000.12345678,"venta":1010.87654321,"fechaActualizacion":"2026-10-25T12:00:00.000Z"}]""";
    [Fact]
    public async Task Parses_valid_usd_buy_and_sell_values_with_eight_decimal_precision()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"moneda":"USD","casa":"oficial","nombre":"Oficial","compra":1000.12345678,"venta":1010.87654321,"fechaActualizacion":"2026-10-25T12:00:00.000Z"}]""", Encoding.UTF8, "application/json")
        });
        var client = new DolarApiClient(new HttpClient(handler));
        var result = await client.GetDollarsAsync(CancellationToken.None);
        var quote = Assert.Single(result);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal("oficial", quote.House);
        Assert.Equal("Oficial", quote.Name);
        Assert.Equal(1000.12345678m, quote.Buy);
        Assert.Equal(1010.87654321m, quote.Sell);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T12:00:00Z"), quote.ProviderUpdatedAt);
    }

    [Fact]
    public async Task Parses_a_nullable_conversion_side_when_the_other_side_is_positive()
    {
        var json = """[{"moneda":"USD","casa":"oficial","nombre":"Oficial","compra":null,"venta":1010.25,"fechaActualizacion":"2026-10-25T12:00:00Z"}]""";
        var client = new DolarApiClient(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, json))));
        var quote = Assert.Single(await client.GetDollarsAsync(CancellationToken.None));
        Assert.Null(quote.Buy);
        Assert.Equal(1010.25m, quote.Sell);
    }

    [Theory]
    [InlineData("[{\"moneda\":\"EUR\",\"casa\":\"oficial\",\"nombre\":\"Euro\",\"compra\":10,\"venta\":11,\"fechaActualizacion\":\"2026-10-25T12:00:00Z\"}]")]
    [InlineData("[{\"moneda\":\"USD\",\"casa\":\" \",\"nombre\":\"Oficial\",\"compra\":10,\"venta\":11,\"fechaActualizacion\":\"2026-10-25T12:00:00Z\"}]")]
    [InlineData("[{\"moneda\":\"USD\",\"casa\":\"oficial\",\"nombre\":\"Oficial\",\"compra\":0,\"venta\":11,\"fechaActualizacion\":\"not-a-date\"}]")]
    public async Task Rejects_non_usd_empty_house_invalid_date_or_nonpositive_conversion_side(string json)
    {
        var client = new DolarApiClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") })));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetDollarsAsync(CancellationToken.None));
    }

    [Fact]
    public void Converts_using_explicit_side_without_mutating_source()
    {
        Assert.Equal(1000.00m, ExchangeRateMath.Convert(1m, "USD", "ARS", 1000.00000000m, "Venta"));
        Assert.Equal(1.00m, ExchangeRateMath.Convert(1000m, "ARS", "USD", 1000.00000000m, "Compra"));
        Assert.Throws<ArgumentException>(() => ExchangeRateMath.Convert(1m, "USD", "ARS", 1000m, ""));
    }

    [Theory]
    [InlineData("POST", "/api/exchange-rates/refresh", WorkspacePermissions.Read)]
    [InlineData("PUT", "/api/savings/goals/00000000-0000-0000-0000-000000000001/rate", WorkspacePermissions.ManageResources)]
    public void Exchange_refresh_and_goal_rate_choice_use_the_required_workspace_permissions(string method, string path, string expected)
    {
        var classifier = typeof(FinanceWorkspaceFilter).GetMethod("RequiredPermission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, classifier.Invoke(null, [method, new Microsoft.AspNetCore.Http.PathString(path)]));
    }

    [Fact]
    public async Task Shares_refresh_within_five_minute_cache_and_replays_identical_manual_refresh()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z"));
        var calls = 0;
        using var factory = new ApiFactory(timeProvider: time, exchangeRateHandlerFactory: () => new StubHandler(request =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal(DolarApiClient.Endpoint, request.RequestUri!.AbsoluteUri);
            return Json(HttpStatusCode.OK, ValidQuote);
        }));
        using var client = await factory.AuthenticatedClientAsync();
        var firstId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, firstId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, firstId)).StatusCode);
        Assert.Equal(1, calls);
        using var list = JsonDocument.Parse(await (await client.GetAsync("/api/exchange-rates")).Content.ReadAsStringAsync());
        Assert.Single(list.RootElement.GetProperty("snapshots").EnumerateArray());
    }

    [Fact]
    public async Task Retains_last_snapshot_and_marks_stale_after_provider_failure()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z"));
        var calls = 0;
        using var factory = new ApiFactory(timeProvider: time, exchangeRateHandlerFactory: () => new StubHandler(_ =>
            Interlocked.Increment(ref calls) == 1 ? Json(HttpStatusCode.OK, ValidQuote) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = await factory.AuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, Guid.NewGuid())).StatusCode);
        time.Advance(TimeSpan.FromMinutes(6));
        using var failed = JsonDocument.Parse(await (await Refresh(client, Guid.NewGuid())).Content.ReadAsStringAsync());
        Assert.True(failed.RootElement.GetProperty("stale").GetBoolean());
        Assert.NotNull(failed.RootElement.GetProperty("error").GetString());
        Assert.Single(failed.RootElement.GetProperty("snapshots").EnumerateArray());
    }

    [Fact]
    public async Task Http_timeout_preserves_the_last_good_snapshot()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z"));
        var calls = 0;
        using var factory = new ApiFactory(timeProvider: time, exchangeRateHandlerFactory: () => new StubHandler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1) return Json(HttpStatusCode.OK, ValidQuote);
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
            return Json(HttpStatusCode.OK, ValidQuote);
        }));
        using var client = await factory.AuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, Guid.NewGuid())).StatusCode);
        time.Advance(TimeSpan.FromMinutes(6));
        using var stale = JsonDocument.Parse(await (await Refresh(client, Guid.NewGuid())).Content.ReadAsStringAsync());
        Assert.True(stale.RootElement.GetProperty("stale").GetBoolean());
        Assert.Single(stale.RootElement.GetProperty("snapshots").EnumerateArray());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Deduplicates_same_house_provider_timestamp_and_values_without_refreshing_snapshot_age()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z"));
        using var factory = new ApiFactory(timeProvider: time, exchangeRateHandlerFactory: () => new StubHandler(_ => Json(HttpStatusCode.OK, ValidQuote)));
        using var client = await factory.AuthenticatedClientAsync();
        await Refresh(client, Guid.NewGuid());
        using var initial = JsonDocument.Parse(await (await client.GetAsync("/api/exchange-rates")).Content.ReadAsStringAsync());
        var fetchedAt = initial.RootElement.GetProperty("snapshots")[0].GetProperty("fetchedAt").GetString();
        time.Advance(TimeSpan.FromMinutes(6));
        await Refresh(client, Guid.NewGuid());
        using var refreshed = JsonDocument.Parse(await (await client.GetAsync("/api/exchange-rates")).Content.ReadAsStringAsync());
        Assert.Single(refreshed.RootElement.GetProperty("snapshots").EnumerateArray());
        Assert.Equal(fetchedAt, refreshed.RootElement.GetProperty("snapshots")[0].GetProperty("fetchedAt").GetString());
        Assert.NotEqual(fetchedAt, refreshed.RootElement.GetProperty("fetchedAt").GetString());
    }

    [Fact]
    public async Task Cancelled_refresh_waiter_does_not_cancel_shared_refresh_or_its_database_scope()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new ApiFactory(exchangeRateHandlerFactory: () => new StubHandler(async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Json(HttpStatusCode.OK, ValidQuote);
        }));
        using var client = await factory.AuthenticatedClientAsync();
        using var cancellation = new CancellationTokenSource();
        var pending = client.PostAsJsonAsync("/api/exchange-rates/refresh", new ExchangeRateRefreshRequest(Guid.NewGuid()), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        release.TrySetResult();
        ExchangeRateListDto? list = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await Task.Delay(20);
            list = await client.GetFromJsonAsync<ExchangeRateListDto>("/api/exchange-rates");
            if (list!.Snapshots.Count > 0) break;
        }
        Assert.Single(list!.Snapshots);
    }

    [Fact]
    public async Task Goal_get_converts_only_from_saved_manual_choice_and_preserves_original_backing_amounts()
    {
        using var factory = new ApiFactory(timeProvider: new MutableTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync();
        var goalId = Guid.NewGuid();
        var created = await client.PostAsJsonAsync("/api/savings/goals", new SavingsGoalCreate(goalId, "Viaje", "USD", "5.00", null, null, null));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var goalVersion = createdJson.RootElement.GetProperty("version").GetInt32();
        await using (var db = Db(factory))
        {
            var workspaceId = await db.SavingsGoals.Where(x => x.Id == goalId).Select(x => x.WorkspaceId).SingleAsync();
            var accountId = Guid.NewGuid();
            db.Accounts.Add(new BankAccount { Id = accountId, WorkspaceId = workspaceId, Name = "Caja", BankName = "Banco", Currency = "ARS", OpeningBalance = 1000m, OpeningDate = new DateOnly(2026, 10, 1) });
            db.SavingsAllocations.Add(new SavingsAllocation { Id = Guid.NewGuid(), WorkspaceId = workspaceId, GoalId = goalId, AccountId = accountId, Currency = "ARS", ReservedAmount = 1000m });
            await db.SaveChangesAsync();
        }
        using var before = JsonDocument.Parse(await (await client.GetAsync("/api/savings/goals")).Content.ReadAsStringAsync());
        var beforeGoal = before.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == goalId);
        Assert.Equal("RateRequired", beforeGoal.GetProperty("estimateStatus").GetString());
        Assert.Equal(JsonValueKind.Null, beforeGoal.GetProperty("savedAmount").ValueKind);
        var choice = new SavingsGoalRateChoice(Guid.NewGuid(), goalVersion, "Manual", null, null, null, "1000.00000000", "Cambio acordado");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/savings/goals/{goalId}/rate", choice)).StatusCode);
        using var after = JsonDocument.Parse(await (await client.GetAsync("/api/savings/goals")).Content.ReadAsStringAsync());
        var afterGoal = after.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == goalId);
        Assert.Equal("1.00", afterGoal.GetProperty("savedAmount").GetString());
        Assert.Equal("4.00", afterGoal.GetProperty("remainingAmount").GetString());
        Assert.Equal("1000.00", afterGoal.GetProperty("backingAmounts")[0].GetProperty("amount").GetString());
        Assert.Equal("1000.00000000", afterGoal.GetProperty("rateChoice").GetProperty("rate").GetString());
        var updatedVersion = afterGoal.GetProperty("version").GetInt32();
        var replacement = choice with { RequestId = Guid.NewGuid(), GoalVersion = updatedVersion, ManualArsPerUsd = "2000.00000000" };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/savings/goals/{goalId}/rate", replacement)).StatusCode);
        using var changed = JsonDocument.Parse(await (await client.GetAsync("/api/savings/goals")).Content.ReadAsStringAsync());
        var changedGoal = changed.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == goalId);
        Assert.Equal("0.50", changedGoal.GetProperty("savedAmount").GetString());
        Assert.Equal("1000.00", changedGoal.GetProperty("backingAmounts")[0].GetProperty("amount").GetString());
        await using var verify = Db(factory);
        Assert.Equal(1000m, await verify.SavingsAllocations.Where(x => x.GoalId == goalId).SumAsync(x => x.ReservedAmount));
        Assert.Equal(0, await verify.LedgerEntries.CountAsync(x => x.AccountId == verify.SavingsAllocations.Where(a => a.GoalId == goalId).Select(a => a.AccountId).Single()));
    }

    [Theory]
    [InlineData("0", "razón válida")]
    [InlineData("1000.000000001", "razón válida")]
    [InlineData("1000", "")]
    public async Task Manual_goal_choice_requires_positive_eight_decimal_rate_and_reason(string rate, string reason)
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/savings/goals", new SavingsGoalCreate(Guid.NewGuid(), "Meta", "ARS", "10.00", null, null, null));
        using var goal = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = goal.RootElement.GetProperty("id").GetGuid();
        var version = goal.RootElement.GetProperty("version").GetInt32();
        var request = new SavingsGoalRateChoice(Guid.NewGuid(), version, "Manual", null, null, null, rate, reason);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/savings/goals/{id}/rate", request)).StatusCode);
    }

    [Fact]
    public async Task Authorized_goal_rate_replay_is_idempotent_and_does_not_increment_goal_version()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/savings/goals", new SavingsGoalCreate(Guid.NewGuid(), "Meta", "ARS", "10.00", null, null, null));
        using var goal = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var goalId = goal.RootElement.GetProperty("id").GetGuid();
        var request = new SavingsGoalRateChoice(Guid.NewGuid(), goal.RootElement.GetProperty("version").GetInt32(), "Manual", null, null, null, "1000.00", "Referencia acordada");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/savings/goals/{goalId}/rate", request)).StatusCode);
        var replay = await client.PutAsJsonAsync($"/api/savings/goals/{goalId}/rate", request);
        var changedPayload = await client.PutAsJsonAsync($"/api/savings/goals/{goalId}/rate", request with { ManualReason = "Otra referencia" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);
        using var latest = JsonDocument.Parse(await (await client.GetAsync("/api/savings/goals")).Content.ReadAsStringAsync());
        Assert.Equal(2, latest.RootElement[0].GetProperty("version").GetInt32());
        await using var db = Db(factory);
        Assert.Equal(1, await db.SavingsCommands.CountAsync(x => x.RequestId == request.RequestId));
        Assert.Equal(1, await db.SavingsGoalRateChoices.CountAsync(x => x.GoalId == goalId));
    }

    [Fact]
    public async Task Goal_rate_replay_revalidates_membership_after_filter_preflight_before_workspace_lock()
    {
        const string ownerSubject = "rate-replay-race-owner";
        const string memberSubject = "rate-replay-race-member";
        using var factory = new ApiFactory();
        using var owner = await factory.AuthenticatedClientAsync(ownerSubject);
        using var member = await factory.AuthenticatedClientAsync(memberSubject);
        var created = await owner.PostAsJsonAsync("/api/savings/goals", new SavingsGoalCreate(Guid.NewGuid(), "Meta", "ARS", "10.00", null, null, null));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var goal = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var goalId = goal.RootElement.GetProperty("id").GetGuid();
        var workspaceId = Guid.Empty;
        await using (var setup = Db(factory))
        {
            workspaceId = await setup.SavingsGoals.Where(x => x.Id == goalId).Select(x => x.WorkspaceId).SingleAsync();
            var memberUserId = await setup.ApplicationUsers.Where(x => x.Subject == memberSubject).Select(x => x.Id).SingleAsync();
            setup.WorkspaceMemberships.Add(new EnigmaBudget.Api.Workspaces.WorkspaceMembership
            {
                WorkspaceId = workspaceId, UserId = memberUserId, Role = "Admin", JoinedAt = DateTimeOffset.UtcNow
            });
            await setup.SaveChangesAsync();
        }
        member.DefaultRequestHeaders.Add("X-Workspace-Id", workspaceId.ToString());
        var request = new SavingsGoalRateChoice(Guid.NewGuid(), goal.RootElement.GetProperty("version").GetInt32(), "Manual", null, null, null, "1000.00", "Referencia acordada");
        Assert.Equal(HttpStatusCode.OK, (await member.PutAsJsonAsync($"/api/savings/goals/{goalId}/rate", request)).StatusCode);

        factory.WorkspaceLockBarrier.Arm();
        var replayTask = member.PutAsJsonAsync($"/api/savings/goals/{goalId}/rate", request);
        var lockWait = factory.WorkspaceLockBarrier.WaitUntilBlockedAsync();
        try
        {
            var first = await Task.WhenAny(replayTask, lockWait).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Same(lockWait, first);
            await using var revoke = Db(factory);
            var memberUserId = await revoke.ApplicationUsers.Where(x => x.Subject == memberSubject).Select(x => x.Id).SingleAsync();
            var membership = await revoke.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspaceId && x.UserId == memberUserId);
            membership.Active = false;
            membership.Version++;
            await revoke.SaveChangesAsync();
        }
        finally { factory.WorkspaceLockBarrier.Release(); }

        var replayResponse = await replayTask;
        Assert.Equal(HttpStatusCode.NotFound, replayResponse.StatusCode);
        using var responseBody = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync());
        Assert.False(responseBody.RootElement.TryGetProperty("rate", out _));
        await using var verify = Db(factory);
        Assert.Equal(1, await verify.SavingsCommands.CountAsync(x => x.RequestId == request.RequestId));
        Assert.Equal(2, await verify.SavingsGoals.Where(x => x.Id == goalId).Select(x => x.Version).SingleAsync());
        var choice = await verify.SavingsGoalRateChoices.SingleAsync(x => x.GoalId == goalId);
        Assert.Equal(1, choice.Version);
        Assert.Equal(1000m, choice.ManualArsPerUsd);
    }

    [Fact]
    public async Task Snapshot_goal_choice_requires_matching_house_and_side()
    {
        using var factory = new ApiFactory(exchangeRateHandlerFactory: () => new StubHandler(_ => Json(HttpStatusCode.OK, ValidQuote)));
        using var client = await factory.AuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, Guid.NewGuid())).StatusCode);
        using var rates = JsonDocument.Parse(await (await client.GetAsync("/api/exchange-rates")).Content.ReadAsStringAsync());
        var snapshotId = rates.RootElement.GetProperty("snapshots")[0].GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync("/api/savings/goals", new SavingsGoalCreate(Guid.NewGuid(), "Meta", "ARS", "10.00", null, null, null));
        using var goal = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = goal.RootElement.GetProperty("id").GetGuid();
        var version = goal.RootElement.GetProperty("version").GetInt32();
        var wrongHouse = new SavingsGoalRateChoice(Guid.NewGuid(), version, "Snapshot", snapshotId, "blue", "Venta", null, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/savings/goals/{id}/rate", wrongHouse)).StatusCode);
    }

    [Fact]
    public async Task Snapshot_choice_uses_the_selected_side_and_quote_remains_explicit()
    {
        using var factory = new ApiFactory(exchangeRateHandlerFactory: () => new StubHandler(_ => Json(HttpStatusCode.OK, ValidQuote)));
        using var client = await factory.AuthenticatedClientAsync();
        await Refresh(client, Guid.NewGuid());
        var created = await client.PostAsJsonAsync("/api/savings/goals", new SavingsGoalCreate(Guid.NewGuid(), "Meta", "ARS", "2000.00", null, null, null));
        using var goal = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = goal.RootElement.GetProperty("id").GetGuid();
        var version = goal.RootElement.GetProperty("version").GetInt32();
        await using (var db = Db(factory))
        {
            var workspaceId = await db.SavingsGoals.Where(x => x.Id == id).Select(x => x.WorkspaceId).SingleAsync();
            var accountId = Guid.NewGuid();
            db.Accounts.Add(new BankAccount { Id = accountId, WorkspaceId = workspaceId, Name = "Caja USD", BankName = "Banco", Currency = "USD", OpeningBalance = 1m, OpeningDate = new DateOnly(2026, 10, 1) });
            db.SavingsAllocations.Add(new SavingsAllocation { Id = Guid.NewGuid(), WorkspaceId = workspaceId, GoalId = id, AccountId = accountId, Currency = "USD", ReservedAmount = 1m });
            await db.SaveChangesAsync();
        }
        var snapshotId = (await client.GetFromJsonAsync<ExchangeRateListDto>("/api/exchange-rates"))!.Snapshots.Single().Id;
        var choice = new SavingsGoalRateChoice(Guid.NewGuid(), version, "Snapshot", snapshotId, "oficial", "Compra", null, null);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/savings/goals/{id}/rate", choice)).StatusCode);
        var updated = (await client.GetFromJsonAsync<SavingsGoalDto[]>("/api/savings/goals"))!.Single(x => x.Id == id);
        Assert.Equal("oficial", updated.RateChoice!.House);
        Assert.Equal("Compra", updated.RateChoice.Side);
        Assert.Equal("1000.12345678", updated.RateChoice.Rate);
        Assert.Equal("1000.12", updated.SavedAmount);
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, Guid id) => client.PostAsJsonAsync("/api/exchange-rates/refresh", new ExchangeRateRefreshRequest(id));
    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static AppDbContext Db(ApiFactory factory) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => respond = (request, _) => Task.FromResult(response(request));
        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) => respond = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
