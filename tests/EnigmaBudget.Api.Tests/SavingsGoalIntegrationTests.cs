using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Savings;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class SavingsGoalIntegrationTests
{
    private readonly ApiFactory _factory = new(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));

    [Fact]
    public async Task Create_goal_requires_manage_resources_and_returns_decimal_strings()
    {
        using var reader = await _factory.AuthenticatedClientAsync("test-reader");
        // The personal owner grants no implicit mutation rights to another workspace; a Reader's own personal space is read-only only when granted.
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("1234.50", json.RootElement.GetProperty("targetAmount").GetString());
        Assert.Equal("0.00", json.RootElement.GetProperty("savedAmount").GetString());
        Assert.True(json.RootElement.GetProperty("reached").ValueKind == JsonValueKind.False);
        Assert.Equal("Active", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("rateChoice").ValueKind);
        Assert.Equal("75.00", json.RootElement.GetProperty("monthlyContribution").GetString());
        Assert.Contains("estimateExplanation", json.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("EUR", "10.00")]
    [InlineData("ARS", "0.00")]
    [InlineData("ARS", "1.001")]
    public async Task Goal_target_must_be_positive_and_currency_ARS_or_USD(string currency, string amount)
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { TargetCurrency = currency, TargetAmount = amount });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Monthly_contribution_requires_next_date_and_date_is_forbidden_without_amount()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var noDate = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { NextContributionDate = null });
        var noAmount = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { MonthlyContribution = null });
        Assert.Equal(HttpStatusCode.BadRequest, noDate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noAmount.StatusCode);
    }

    [Fact]
    public async Task Goal_edit_requires_version_and_idempotent_payload_replay()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        var created = await client.PostAsJsonAsync("/api/savings/goals", Create(id));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<GoalDto>();
        Assert.NotNull(dto);
        var update = new { requestId = Guid.NewGuid(), version = dto!.Version, name = "Vacaciones", targetCurrency = "ARS", targetAmount = "1500.00", desiredDate = (string?)null, state = "Active", monthlyContribution = (string?)null, nextContributionDate = (string?)null };
        var first = await client.PutAsJsonAsync($"/api/savings/goals/{id}", update);
        var replay = await client.PutAsJsonAsync($"/api/savings/goals/{id}", update);
        var changed = await client.PutAsJsonAsync($"/api/savings/goals/{id}", update with { name = "Otro" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var stale = await client.PutAsJsonAsync($"/api/savings/goals/{id}", update with { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Goal_close_false_confirmation_changes_nothing_then_true_confirmation_releases_all_balances_atomically()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        var created = await client.PostAsJsonAsync("/api/savings/goals", Create(id) with { MonthlyContribution = null, NextContributionDate = null });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<GoalDto>();
        await AddBackingAsync(id, "ARS", "50.00");
        var falseClose = await client.PostAsJsonAsync($"/api/savings/goals/{id}/close", new { requestId = Guid.NewGuid(), version = dto!.Version, confirmRelease = false });
        Assert.Equal(HttpStatusCode.BadRequest, falseClose.StatusCode);
        await using (var unchanged = Db())
        {
            var goalBeforeClose = await unchanged.SavingsGoals.SingleAsync(x => x.Id == id);
            Assert.Equal("Active", goalBeforeClose.State);
            Assert.Equal(dto.Version, goalBeforeClose.Version);
            var allocationBeforeClose = await unchanged.SavingsAllocations.SingleAsync(x => x.GoalId == id);
            Assert.Equal(50m, allocationBeforeClose.ReservedAmount);
            Assert.Empty(await unchanged.SavingsMovements.Where(x => x.AllocationId == allocationBeforeClose.Id).ToArrayAsync());
        }
        var trueRequest = new { requestId = Guid.NewGuid(), version = dto.Version, confirmRelease = true };
        var close = await client.PostAsJsonAsync($"/api/savings/goals/{id}/close", trueRequest);
        var replay = await client.PostAsJsonAsync($"/api/savings/goals/{id}/close", trueRequest);
        Assert.True(close.StatusCode == HttpStatusCode.OK, $"Close failed with {(int)close.StatusCode}: {await close.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await using var db = Db();
        Assert.Equal("Closed", (await db.SavingsGoals.SingleAsync(x => x.Id == id)).State);
        var workspaceId = await db.SavingsGoals.Where(g => g.Id == id).Select(g => g.WorkspaceId).SingleAsync();
        var allocation = await db.SavingsAllocations.SingleAsync(x => x.WorkspaceId == workspaceId && x.GoalId == id);
        Assert.Equal(0m, allocation.ReservedAmount);
        var movement = Assert.Single(await db.SavingsMovements.Where(x => x.WorkspaceId == workspaceId && x.AllocationId == allocation.Id).ToArrayAsync());
        Assert.Equal("Release", movement.Kind);
        Assert.Equal(50m, movement.Amount);
    }

    [Fact]
    public async Task Close_identical_request_replays_and_changed_payload_returns_409()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        var created = await client.PostAsJsonAsync("/api/savings/goals", Create(id) with { MonthlyContribution = null, NextContributionDate = null });
        var dto = await created.Content.ReadFromJsonAsync<GoalDto>();
        var request = new { requestId = Guid.NewGuid(), version = dto!.Version, confirmRelease = true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/savings/goals/{id}/close", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/savings/goals/{id}/close", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/savings/goals/{id}/close", request with { confirmRelease = false })).StatusCode);
    }

    [Fact]
    public async Task Desired_date_requires_187_50_for_750_remaining_over_four_monthly_occurrences()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { TargetAmount = "1000.00", DesiredDate = "2027-02-10", MonthlyContribution = "75.00", NextContributionDate = "2026-11-10" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GoalDto>();
        await AddBackingAsync(dto!.Id, "ARS", "250.00");
        var goalId = dto.Id;
        dto = (await client.GetFromJsonAsync<GoalDto[]>("/api/savings/goals"))!.Single(x => x.Id == goalId);
        Assert.Equal("187.50", dto!.RequiredMonthlyContribution);
        Assert.Equal(4, dto.RequiredContributionCount);
    }

    [Theory]
    [InlineData("2026-10-24", "DesiredDatePassed")]
    [InlineData("2026-11-09", "NoContributionDateBeforeGoal")]
    public async Task Desired_date_without_eligible_occurrences_has_explanation(string desiredDate, string status)
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { DesiredDate = desiredDate, MonthlyContribution = null, NextContributionDate = null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GoalDto>();
        Assert.Equal(status, dto!.EstimateStatus);
        Assert.Null(dto.RequiredMonthlyContribution);
        Assert.False(string.IsNullOrWhiteSpace(dto.EstimateExplanation));
    }

    [Fact]
    public async Task Monthly_estimate_uses_calendar_occurrences_and_skips_stale_dates()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { TargetAmount = "200.00", DesiredDate = "2026-12-31", MonthlyContribution = "75.00", NextContributionDate = "2026-08-31" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GoalDto>();
        Assert.Equal(3, dto!.RequiredContributionCount);
        Assert.Equal("2026-12-31", dto.EstimatedCompletionDate);
        Assert.Equal("66.67", dto.RequiredMonthlyContribution);
    }

    [Fact]
    public async Task Monthly_estimate_is_null_without_positive_contribution()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { MonthlyContribution = null, NextContributionDate = null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GoalDto>();
        Assert.Null(dto!.EstimatedCompletionDate);
        Assert.Null(dto.RequiredContributionCount);
        Assert.Equal("NoMonthlyContribution", dto.EstimateStatus);
    }

    [Fact]
    public async Task Goal_list_is_limited_to_current_workspace()
    {
        using var first = await _factory.AuthenticatedClientAsync("savings-user-a");
        using var second = await _factory.AuthenticatedClientAsync("savings-user-b");
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()))).StatusCode);
        Assert.Empty(await second.GetFromJsonAsync<GoalDto[]>("/api/savings/goals") ?? []);
    }

    [Fact]
    public async Task Paused_goal_has_no_automatic_completion_estimate()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(id) with { TargetAmount = "1.00" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GoalDto>();
        var pause = await client.PutAsJsonAsync($"/api/savings/goals/{id}", new { requestId = Guid.NewGuid(), version = dto!.Version, name = dto.Name, targetCurrency = dto.TargetCurrency, targetAmount = dto.TargetAmount, desiredDate = dto.DesiredDate, state = "Paused", monthlyContribution = dto.MonthlyContribution, nextContributionDate = dto.NextContributionDate });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        dto = await pause.Content.ReadFromJsonAsync<GoalDto>();
        Assert.False(dto!.Reached);
        Assert.Null(dto.EstimatedCompletionDate);
    }

    [Fact]
    public async Task Foreign_currency_backing_without_rate_returns_null_saved_and_remaining_amounts()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()));
        var goal = await created.Content.ReadFromJsonAsync<GoalDto>();
        await AddBackingAsync(goal!.Id, "USD", "12.34");
        var dto = (await client.GetFromJsonAsync<GoalDto[]>("/api/savings/goals"))!.Single(x => x.Id == goal.Id);
        Assert.Equal("RateRequired", dto.EstimateStatus);
        Assert.Null(dto.SavedAmount);
        Assert.Null(dto.RemainingAmount);
        Assert.Null(dto.RateChoice);
        Assert.Equal(new BackingAmountDto("USD", "12.34"), Assert.Single(dto.BackingAmounts));
    }

    [Fact]
    public async Task Zero_balance_foreign_allocation_does_not_require_a_rate()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()));
        var goal = await created.Content.ReadFromJsonAsync<GoalDto>();
        await AddBackingAsync(goal!.Id, "USD", "0.00");
        var dto = (await client.GetFromJsonAsync<GoalDto[]>("/api/savings/goals"))!.Single(x => x.Id == goal.Id);
        Assert.NotEqual("RateRequired", dto.EstimateStatus);
        Assert.Equal("0.00", dto.SavedAmount);
        Assert.Equal("1234.50", dto.RemainingAmount);
    }

    [Fact]
    public async Task Reached_is_derived_from_same_currency_backing_amount()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var goalResponse = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { TargetAmount = "1.00", MonthlyContribution = null, NextContributionDate = null });
        var goal = await goalResponse.Content.ReadFromJsonAsync<GoalDto>();
        await AddBackingAsync(goal!.Id, "ARS", "1.00");
        var dto = (await client.GetFromJsonAsync<GoalDto[]>("/api/savings/goals"))!.Single(x => x.Id == goal.Id);
        Assert.True(dto.Reached);
        Assert.Equal("0.00", dto.RemainingAmount);
        Assert.Equal(0, dto.RequiredContributionCount);
        Assert.Equal("2026-10-25", dto.EstimatedCompletionDate);
    }

    [Theory]
    [InlineData("9999999999999999.99", "0.01", "2026-10-25")]
    [InlineData("10.00", "1.00", "9999-12-31")]
    public async Task Estimates_beyond_date_count_or_calendar_range_are_unavailable_not_server_errors(string target, string monthly, string next)
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()) with { TargetAmount = target, MonthlyContribution = monthly, NextContributionDate = next });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GoalDto>();
        Assert.Equal("BeyondCalendarRange", dto!.EstimateStatus);
        Assert.Equal(target, dto.RemainingAmount);
        Assert.Null(dto.RequiredContributionCount);
        Assert.Null(dto.EstimatedCompletionDate);
        Assert.Contains("rango de fechas", dto.EstimateExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Goal_mutations_require_csrf_validation()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var response = await client.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Personal_read_grant_can_read_personal_goals_but_not_mutate_or_read_groups()
    {
        using var owner = _factory.IdentityClient("savings-owner");
        using var reader = _factory.IdentityClient("savings-reader");
        owner.DefaultRequestHeaders.Add("X-Test-Email", "savings-owner@example.test");
        owner.DefaultRequestHeaders.Add("X-Test-Email-Verified", "true");
        reader.DefaultRequestHeaders.Add("X-Test-Email", "savings-reader@example.test");
        reader.DefaultRequestHeaders.Add("X-Test-Email-Verified", "true");
        var ownerSession = await owner.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var readerSession = await reader.GetFromJsonAsync<JsonElement>("/api/auth/session");
        owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN", ownerSession.GetProperty("csrfToken").GetString());
        reader.DefaultRequestHeaders.Add("X-CSRF-TOKEN", readerSession.GetProperty("csrfToken").GetString());
        var personal = Assert.Single(await owner.GetFromJsonAsync<JsonElement[]>("/api/workspaces") ?? []);
        var personalId = personal.GetProperty("id").GetGuid();
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", personalId.ToString());
        var goal = await owner.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, goal.StatusCode);

        var householdResponse = await owner.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar" });
        Assert.Equal(HttpStatusCode.Created, householdResponse.StatusCode);
        var household = await householdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var householdId = household.GetProperty("id").GetGuid();
        var invitationResponse = await owner.PostAsJsonAsync($"/api/workspaces/{householdId}/invitations", new { id = Guid.NewGuid(), email = "savings-reader@example.test", role = "Reader" });
        Assert.Equal(HttpStatusCode.OK, invitationResponse.StatusCode);
        var invitation = await invitationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shareUrl = invitation.GetProperty("shareUrl").GetString()!;
        var token = Uri.UnescapeDataString(new Uri("https://localhost" + shareUrl).Fragment.TrimStart('#').Split('=', 2).Last());
        var accept = await reader.PostAsJsonAsync("/api/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var grant = await owner.PutAsJsonAsync($"/api/personal-sharing/{householdId}", new { version = 1, enabled = true });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        reader.DefaultRequestHeaders.Add("X-Workspace-Id", personalId.ToString());
        var goals = await reader.GetAsync("/api/savings/goals");
        Assert.Equal(HttpStatusCode.OK, goals.StatusCode);
        using var document = JsonDocument.Parse(await goals.Content.ReadAsStringAsync());
        Assert.Single(document.RootElement.EnumerateArray());
        var raw = document.RootElement.GetRawText();
        Assert.DoesNotContain("group", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("participant", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/savings/goals", Create(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/groups")).StatusCode);
    }

    private async Task AddBackingAsync(Guid goalId, string currency, string amount)
    {
        await using var db = Db();
        var goal = await db.SavingsGoals.SingleAsync(x => x.Id == goalId);
        var accountId = Guid.NewGuid();
        db.Accounts.Add(new BankAccount { Id = accountId, WorkspaceId = goal.WorkspaceId, Name = "Caja de prueba", BankName = "Banco de prueba", Currency = currency, OpeningBalance = 0m, OpeningDate = new DateOnly(2026, 10, 1) });
        db.SavingsAllocations.Add(new SavingsAllocation { Id = Guid.NewGuid(), WorkspaceId = goal.WorkspaceId, GoalId = goalId, AccountId = accountId, Currency = currency, ReservedAmount = decimal.Parse(amount, CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync();
    }

    private SavingsGoalCreate Create(Guid id) => new(id, "Meta", "ARS", "1234.50", null, "75.00", "2026-11-10");
    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
    private sealed record BackingAmountDto(string Currency, string Amount);
    private sealed record GoalDto(Guid Id, string Name, string TargetCurrency, string TargetAmount, string? DesiredDate, string State, string? MonthlyContribution, string? NextContributionDate, bool Reached, IReadOnlyList<BackingAmountDto> BackingAmounts, GoalRateChoiceDto? RateChoice, string? SavedAmount, string? RemainingAmount, string? RequiredMonthlyContribution, int? RequiredContributionCount, string? EstimatedCompletionDate, string EstimateStatus, string EstimateExplanation, int Version);
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider { public override DateTimeOffset GetUtcNow() => utcNow; }
}
