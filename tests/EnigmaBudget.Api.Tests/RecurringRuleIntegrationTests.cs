using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EnigmaBudget.Api.Tests;

public sealed class RecurringRuleIntegrationTests
{
    [Fact]
    public async Task jan31_feb28_mar31_preserves_anchor_and_forecast_is_read_only_estimated()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-anchor");
        var workspaceId = await WorkspaceId(factory, "recurring-anchor");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        var create = await CreateRule(client, ruleId, categoryId, "2026-01-31", "Monthly");
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());

        var beforeRules = await CountRules(factory, workspaceId);
        var beforeOperations = await CountOperations(factory, workspaceId);
        var beforeActions = await CountActions(factory, workspaceId);
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-01-01&through=2026-03-31"));
        var dates = response.RootElement.GetProperty("items").EnumerateArray()
            .Where(x => x.GetProperty("ruleId").GetGuid() == ruleId)
            .Select(x => x.GetProperty("scheduledDate").GetString()!).ToArray();
        Assert.Equal(["2026-01-31", "2026-02-28", "2026-03-31"], dates);
        Assert.All(response.RootElement.GetProperty("items").EnumerateArray(), item => Assert.Equal("Estimated", item.GetProperty("status").GetString()));
        Assert.Equal(beforeRules, await CountRules(factory, workspaceId));
        Assert.Equal(beforeOperations, await CountOperations(factory, workspaceId));
        Assert.Equal(beforeActions, await CountActions(factory, workspaceId));
        using var sameQuery = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-01-01&through=2026-03-31"));
        Assert.Equal(response.RootElement.GetProperty("items")[0].GetProperty("occurrenceId").GetGuid(), sameQuery.RootElement.GetProperty("items")[0].GetProperty("occurrenceId").GetGuid());
    }

    [Fact]
    public async Task overdue_occurrences_are_all_returned_per_date_with_pagination()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-pages");
        var workspaceId = await WorkspaceId(factory, "recurring-pages");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, Guid.NewGuid(), categoryId, "2026-01-01", "Weekly")).StatusCode);
        using var first = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-01-01&through=2026-12-31"));
        var firstItems = first.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(200, firstItems.Length);
        Assert.All(firstItems, item => Assert.Equal("Estimated", item.GetProperty("status").GetString()));
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        var mismatchedBounds = await client.GetAsync("/api/recurrences/occurrences?from=2026-01-02&through=2026-12-31&cursor=" + Uri.EscapeDataString(cursor!));
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedBounds.StatusCode);
        using var second = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-01-01&through=2026-12-31&cursor=" + Uri.EscapeDataString(cursor!)));
        var secondItems = second.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.NotEmpty(secondItems);
        Assert.Empty(firstItems.Select(x => x.GetProperty("occurrenceId").GetGuid()).Intersect(secondItems.Select(x => x.GetProperty("occurrenceId").GetGuid())));
        Assert.Equal(4 * 53, firstItems.Length + secondItems.Length);
    }

    [Fact]
    public async Task older_occurrences_set_explicit_incomplete_warning()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-old");
        var workspaceId = await WorkspaceId(factory, "recurring-old");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, Guid.NewGuid(), categoryId, "2025-01-15", "Monthly")).StatusCode);
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-01-01&through=2026-03-31"));
        Assert.True(response.RootElement.GetProperty("olderOccurrencesIncomplete").GetBoolean());
        Assert.Equal("2025-01-15", response.RootElement.GetProperty("earliestOmittedScheduledDate").GetString());
    }

    [Fact]
    public async Task older_warning_uses_one_scalar_query_instead_of_loading_historical_rows()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-scalar-warning");
        var workspaceId = await WorkspaceId(factory, "recurring-scalar-warning");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2025-01-15", "Monthly")).StatusCode);
        Guid latestRevisionId;
        await using (var seed = Db(factory.TestConnection))
        {
            var rule = await seed.RecurringRules.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == ruleId);
            var original = await seed.RecurringRuleRevisions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
            for (var version = 2; version <= 20; version++)
            {
                var copy = CopyRevision(original, version);
                seed.RecurringRuleRevisions.Add(copy);
                latestRevisionId = copy.Id;
            }
            seed.RecurringOccurrenceActions.Add(new RecurringOccurrenceAction
            {
                Id = Guid.NewGuid(), WorkspaceId = workspaceId, RuleId = ruleId, RuleRevisionId = original.Id,
                ScheduledDate = new DateOnly(2025, 1, 15), Outcome = "Skipped", RequestId = Guid.NewGuid(), PayloadHash = new string('A', 64)
            });
            rule.CurrentVersion = 20;
            await seed.SaveChangesAsync();
            latestRevisionId = seed.RecurringRuleRevisions.Local.Single(x => x.RuleId == ruleId && x.Version == 20).Id;
        }
        var probe = new HistoricalWarningQueryProbe();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).AddInterceptors(probe).Options;
        await using var db = new AppDbContext(options);
        var result = await new RecurringCalendar(db).GetPageAsync(workspaceId, "2026-01-01", "2026-03-31", null, CancellationToken.None);
        Assert.Equal(200, result.Status);
        Assert.Equal("2025-02-15", result.Page!.EarliestOmittedScheduledDate);
        Assert.Single(probe.Queries);
        Assert.Equal(1, probe.FieldCounts.Single());
        Assert.Contains("WITH RECURSIVE", probe.Queries.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT EXISTS", probe.Queries.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIN(w.\"ScheduledDate\")", probe.Queries.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT action.\"", probe.Queries.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Single(probe.RevisionQueries);
        Assert.Contains("EffectiveFrom\" >=", probe.RevisionQueries.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Version\" >", probe.RevisionQueries.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.All(result.Page.Items, item => Assert.Equal(latestRevisionId, item.RevisionId));
    }

    [Fact]
    public async Task future_start_rule_is_not_started_in_today_list_state()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-future-start");
        var workspaceId = await WorkspaceId(factory, "recurring-future-start");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2027-01-01", "Monthly")).StatusCode);
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences"));
        var rule = list.RootElement.EnumerateArray().Single(x => x.GetProperty("rule").GetProperty("id").GetGuid() == ruleId);
        Assert.Equal("NotStarted", rule.GetProperty("stateForToday").GetString());
        Assert.Equal("Active", rule.GetProperty("rule").GetProperty("state").GetString());
    }

    [Fact]
    public void weekly_occurrence_resolver_uses_wide_interval_arithmetic()
    {
        var anchor = new DateOnly(2026, 1, 1);
        var revision = new RecurringRuleRevision
        {
            ScheduleAnchorDate = anchor,
            Frequency = "Weekly",
            Interval = 613566757
        };

        Assert.True(RecurringCalendar.IsOccurrenceDate(revision, anchor));
        Assert.False(RecurringCalendar.IsOccurrenceDate(revision, anchor.AddDays(3)));
    }

    [Fact]
    public async Task leap_day_annual_anchor_is_preserved()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-leap");
        var workspaceId = await WorkspaceId(factory, "recurring-leap");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2024-02-29", "Annual")).StatusCode);
        var dates = new List<string>();
        string? cursor = null;
        do
        {
            var query = "/api/recurrences/occurrences?from=2024-01-01&through=2028-12-31" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            using var response = JsonDocument.Parse(await client.GetStringAsync(query));
            dates.AddRange(response.RootElement.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("ruleId").GetGuid() == ruleId).Select(x => x.GetProperty("scheduledDate").GetString()!));
            cursor = response.RootElement.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(["2024-02-29", "2025-02-28", "2026-02-28", "2027-02-28", "2028-02-29"], dates);
    }

    [Fact]
    public async Task maximum_date_monthly_occurrence_is_returned_without_overflow()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-max-date");
        var workspaceId = await WorkspaceId(factory, "recurring-max-date");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "9999-12-31", "Monthly")).StatusCode);
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=9999-12-31&through=9999-12-31"));
        var item = Assert.Single(response.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("9999-12-31", item.GetProperty("scheduledDate").GetString());
    }

    [Fact]
    public async Task medical_rule_amount_mismatch_is_rejected_without_revision()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-medical-mismatch");
        var workspaceId = await WorkspaceId(factory, "recurring-medical-mismatch");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var request = new
        {
            id = Guid.NewGuid(), flow = "Expense", description = "Cobertura", categoryId, incomeSourceId = (Guid?)null,
            merchantId = (Guid?)null, amount = "900.00", amountMode = "Estimated", currency = "ARS", frequency = "Monthly",
            interval = 1, startDate = "2026-10-01", endDate = (string?)null, dueDay = (int?)null, preferredAccountId = (Guid?)null,
            preferredCardId = (Guid?)null,
            healthcareComponents = new[]
            {
                new { id = Guid.NewGuid(), kind = "ProviderCharge", amount = "1200.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = Guid.NewGuid(), kind = "ProviderDiscount", amount = "200.00", appliesToProviderBalance = false, participantId = (Guid?)null }
            }
        };
        var response = await client.PostAsJsonAsync("/api/recurrences", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = Db(factory.TestConnection);
        Assert.Empty(await db.RecurringRules.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
        Assert.Empty(await db.RecurringRuleRevisions.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
        Assert.Empty(await db.RecurringRuleCommands.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task medical_rule_forecast_uses_validated_gross_not_payroll_or_refund()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-medical-gross");
        var workspaceId = await WorkspaceId(factory, "recurring-medical-gross");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var request = new
        {
            id = Guid.NewGuid(), flow = "Expense", description = "Cobertura", categoryId, incomeSourceId = (Guid?)null,
            merchantId = (Guid?)null, amount = "1000.00", amountMode = "Estimated", currency = "ARS", frequency = "Monthly",
            interval = 1, startDate = "2026-10-01", endDate = (string?)null, dueDay = (int?)null, preferredAccountId = (Guid?)null,
            preferredCardId = (Guid?)null,
            healthcareComponents = new[]
            {
                new { id = Guid.NewGuid(), kind = "ProviderCharge", amount = "1200.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = Guid.NewGuid(), kind = "ProviderDiscount", amount = "200.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = Guid.NewGuid(), kind = "PayrollWithholding", amount = "300.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = Guid.NewGuid(), kind = "ExpectedRefund", amount = "100.00", appliesToProviderBalance = false, participantId = (Guid?)null }
            }
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/recurrences", request)).StatusCode);
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-10-01&through=2026-10-31"));
        Assert.Equal("1000.00", response.RootElement.GetProperty("items")[0].GetProperty("amount").GetString());
    }

    [Fact]
    public async Task effective_revision_applies_from_selected_date_and_keeps_previous_revision()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-revision");
        var workspaceId = await WorkspaceId(factory, "recurring-revision");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2026-01-31", "Monthly")).StatusCode);
        await using var db = Db(factory.TestConnection);
        var original = await db.RecurringRuleRevisions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var update = await client.PutAsJsonAsync($"/api/recurrences/{ruleId}", new
        {
            requestId = Guid.NewGuid(), version = 1, effectiveFrom = "2026-03-01", description = "Alquiler actualizado",
            categoryId, incomeSourceId = (Guid?)null, merchantId = (Guid?)null, amount = "1200.00", amountMode = "Fixed",
            currency = "ARS", frequency = "Monthly", interval = 1, scheduleAnchorDate = "2026-01-31", endDate = (string?)null,
            dueDay = (int?)null, preferredAccountId = (Guid?)null, preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var sameStartCorrection = await client.PutAsJsonAsync($"/api/recurrences/{ruleId}", new
        {
            requestId = Guid.NewGuid(), version = 2, effectiveFrom = "2026-03-01", description = "Alquiler corregido",
            categoryId, incomeSourceId = (Guid?)null, merchantId = (Guid?)null, amount = "1300.00", amountMode = "Fixed",
            currency = "ARS", frequency = "Monthly", interval = 1, scheduleAnchorDate = "2026-01-31", endDate = (string?)null,
            dueDay = (int?)null, preferredAccountId = (Guid?)null, preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, sameStartCorrection.StatusCode);
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-01-01&through=2026-03-31"));
        var rows = response.RootElement.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("ruleId").GetGuid() == ruleId).ToArray();
        Assert.Equal(["2026-01-31", "2026-02-28", "2026-03-31"], rows.Select(x => x.GetProperty("scheduledDate").GetString()!).ToArray());
        Assert.Equal("1000.00", rows[0].GetProperty("amount").GetString());
        Assert.Equal("1000.00", rows[1].GetProperty("amount").GetString());
        Assert.Equal("1300.00", rows[2].GetProperty("amount").GetString());
        Assert.Equal(original.Id, await db.RecurringRuleRevisions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId && x.Version == 1).Select(x => x.Id).SingleAsync());
        Assert.Equal(3, await db.RecurringRuleRevisions.CountAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId));
        Assert.Equal(1200m, await db.RecurringRuleRevisions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId && x.Version == 2).Select(x => x.Amount).SingleAsync());
    }

    [Fact]
    public async Task future_pause_preserves_earlier_active_occurrences()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-pause");
        var workspaceId = await WorkspaceId(factory, "recurring-pause");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2026-01-01", "Monthly")).StatusCode);
        var pause = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/state", new { requestId = Guid.NewGuid(), version = 1, effectiveFrom = "2026-03-01", state = "Paused" });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-01-01&through=2026-04-30"));
        var dates = response.RootElement.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("ruleId").GetGuid() == ruleId).Select(x => x.GetProperty("scheduledDate").GetString()!).ToArray();
        Assert.Equal(["2026-01-01", "2026-02-01"], dates);
    }

    [Fact]
    public async Task rule_command_replay_is_atomic_and_permission_checked()
    {
        using var factory = new ApiFactory();
        using var client = await factory.AuthenticatedClientAsync("recurring-replay");
        var workspaceId = await WorkspaceId(factory, "recurring-replay");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        var request = RuleRequest(ruleId, categoryId, "2026-01-01", "Monthly");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/recurrences", request)).StatusCode);
        var pause = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/state", new { requestId = Guid.NewGuid(), version = 1, effectiveFrom = "2026-02-01", state = "Paused" });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        using var replay = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/recurrences", request)).Content.ReadAsStringAsync());
        Assert.Equal(1, replay.RootElement.GetProperty("currentVersion").GetInt32());
        await using var db = Db(factory.TestConnection);
        Assert.Equal(2, await db.RecurringRuleCommands.CountAsync(x => x.WorkspaceId == workspaceId));
        Assert.Equal(2, await db.WorkspaceAuditEvents.CountAsync(x => x.WorkspaceId == workspaceId && x.ResourceType == "RecurringRule"));
        await db.WorkspaceMemberships.Where(x => x.WorkspaceId == workspaceId).ExecuteUpdateAsync(x => x.SetProperty(y => y.Role, "Reader"));
        var denied = await client.PostAsJsonAsync("/api/recurrences", request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(2, await db.RecurringRuleCommands.CountAsync(x => x.WorkspaceId == workspaceId));
    }

    [Theory]
    [InlineData("GET", "/api/recurrences", WorkspacePermissions.Read)]
    [InlineData("GET", "/api/recurrences/occurrences", WorkspacePermissions.Read)]
    [InlineData("POST", "/api/recurrences", WorkspacePermissions.ManageResources)]
    [InlineData("PUT", "/api/recurrences/00000000-0000-0000-0000-000000000001", WorkspacePermissions.ManageResources)]
    [InlineData("POST", "/api/recurrences/00000000-0000-0000-0000-000000000001/state", WorkspacePermissions.ManageResources)]
    public void recurring_routes_require_explicit_workspace_permission(string method, string path, string expected)
    {
        var classifier = typeof(FinanceWorkspaceFilter).GetMethod("RequiredPermission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, classifier.Invoke(null, [method, new Microsoft.AspNetCore.Http.PathString(path)]));
    }

    private static Task<HttpResponseMessage> CreateRule(HttpClient client, Guid id, Guid categoryId, string startDate, string frequency) => client.PostAsJsonAsync("/api/recurrences", RuleRequest(id, categoryId, startDate, frequency));

    private static RecurringRuleRevision CopyRevision(RecurringRuleRevision source, int version) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = source.WorkspaceId, RuleId = source.RuleId, Version = version,
        EffectiveFrom = source.EffectiveFrom, State = source.State, ScheduleAnchorDate = source.ScheduleAnchorDate,
        Flow = source.Flow, Description = source.Description, CategoryId = source.CategoryId, IncomeSourceId = source.IncomeSourceId,
        MerchantId = source.MerchantId, Amount = source.Amount, AmountMode = source.AmountMode, Currency = source.Currency,
        Frequency = source.Frequency, Interval = source.Interval, EndDate = source.EndDate, DueDay = source.DueDay,
        PreferredAccountId = source.PreferredAccountId, PreferredCardId = source.PreferredCardId
    };

    private static object RuleRequest(Guid id, Guid categoryId, string startDate, string frequency) => new
    {
        id, flow = "Expense", description = "Gasto recurrente", categoryId, incomeSourceId = (Guid?)null, merchantId = (Guid?)null,
        amount = "1000.00", amountMode = "Fixed", currency = "ARS", frequency, interval = 1, startDate, endDate = (string?)null,
        dueDay = (int?)null, preferredAccountId = (Guid?)null, preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
    };

    private static async Task<Guid> WorkspaceId(ApiFactory factory, string subject)
    {
        await using var db = Db(factory.TestConnection);
        return await db.Workspaces.Where(x => x.OwnerSubject == subject).Select(x => x.Id).SingleAsync();
    }

    private static async Task<Guid> DefaultCategoryId(ApiFactory factory, Guid workspaceId, string kind)
    {
        await using var db = Db(factory.TestConnection);
        return await db.FinanceCategories.Where(x => x.WorkspaceId == workspaceId && x.Kind == kind && x.IsDefault).Select(x => x.Id).SingleAsync();
    }

    private static async Task<int> CountRules(ApiFactory factory, Guid workspaceId)
    {
        await using var db = Db(factory.TestConnection);
        return await db.RecurringRules.CountAsync(x => x.WorkspaceId == workspaceId);
    }

    private static async Task<int> CountOperations(ApiFactory factory, Guid workspaceId)
    {
        await using var db = Db(factory.TestConnection);
        return await db.FinanceOperations.CountAsync(x => x.WorkspaceId == workspaceId);
    }

    private static async Task<int> CountActions(ApiFactory factory, Guid workspaceId)
    {
        await using var db = Db(factory.TestConnection);
        return await db.RecurringOccurrenceActions.CountAsync(x => x.WorkspaceId == workspaceId);
    }

    private static AppDbContext Db(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);

    private sealed class HistoricalWarningQueryProbe : DbCommandInterceptor
    {
        public List<string> Queries { get; } = [];
        public List<int> FieldCounts { get; } = [];
        public List<string> RevisionQueries { get; } = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase))
            {
                Queries.Add(command.CommandText);
                FieldCounts.Add(result.FieldCount);
            }
            else if (command.CommandText.Contains("FROM \"RecurringRuleRevisions\"", StringComparison.OrdinalIgnoreCase))
            {
                RevisionQueries.Add(command.CommandText);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
