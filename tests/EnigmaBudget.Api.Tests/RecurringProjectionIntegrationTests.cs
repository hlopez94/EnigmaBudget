using System.Net;
using System.Net.Http.Json;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Projection;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EnigmaBudget.Api.Tests;

public sealed class RecurringProjectionIntegrationTests
{
    private static readonly DateTimeOffset TestInstant = DateTimeOffset.Parse("2026-10-25T15:00:00Z");

    [Fact]
    public async Task estimated_projection_keeps_each_overdue_occurrence_separate()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-overdue");
        var (workspaceId, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-overdue");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2026-08-01")).StatusCode);
        using (var calendar = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2023-10-25&through=2026-10-31")))
            Assert.Equal(3, calendar.RootElement.GetProperty("items").EnumerateArray().Count(x => x.GetProperty("ruleId").GetGuid() == ruleId));
        using (var confirmed = JsonDocument.Parse(await client.GetStringAsync("/api/projection?through=2026-10-31&scenario=Confirmed")))
            Assert.DoesNotContain(confirmed.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence");

        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2026-10-31", scenario = "Estimated", assumptions = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Where(x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence").ToArray();
        Assert.True(rows.Length == 3, doc.RootElement.GetProperty("rows").GetRawText());
        Assert.Equal(new[] { "2026-08-01", "2026-09-01", "2026-10-01" }, rows.Select(x => x.GetProperty("date").GetString()!).ToArray());
        Assert.Equal(3, rows.Select(x => x.GetProperty("sourceId").GetGuid()).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.Equal("Estimated", row.GetProperty("certainty").GetString());
            Assert.Equal("Estimated", row.GetProperty("dateCertainty").GetString());
            Assert.Equal("ARS", row.GetProperty("currency").GetString());
            Assert.Equal("Outgoing", row.GetProperty("direction").GetString());
            Assert.Null(row.GetProperty("componentId").GetString());
            Assert.Null(row.GetProperty("summaryId").GetString());
            Assert.StartsWith("Estimación recurrente:", row.GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.Contains(row.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()!), x => x.Contains("Vencimiento atrasado", StringComparison.Ordinal));
        });
        await using var db = Db(factory.TestConnection);
        var expected = rows.Select(x => RecurringCalendar.StableOccurrenceId(workspaceId, ruleId,
            DateOnly.Parse(x.GetProperty("date").GetString()!))).ToArray();
        Assert.Equal(expected.Order().ToArray(), rows.Select(x => x.GetProperty("sourceId").GetGuid()).Order().ToArray());
        Assert.Empty(await db.FinanceOperations.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task overdue_dates_keep_stable_source_ids()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-stable");
        var (_, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-stable");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2026-08-01")).StatusCode);

        async Task<Guid[]> ReadIds()
        {
            using var response = await client.PostAsJsonAsync("/api/projection/calculate", new
            {
                through = "2026-10-31", scenario = "Estimated", assumptions = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("rows").EnumerateArray()
                .Where(x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence")
                .Select(x => x.GetProperty("sourceId").GetGuid()).Order().ToArray();
        }

        Assert.Equal(await ReadIds(), await ReadIds());
    }

    [Fact]
    public async Task calendar_cursor_keeps_all_bounded_recurring_estimates()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-cursor");
        var (_, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-cursor");
        var ruleId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync("/api/recurrences", new
        {
            id = ruleId, flow = "Expense", description = "Gasto semanal", categoryId, incomeSourceId = (Guid?)null, merchantId = (Guid?)null,
            amount = "10.00", amountMode = "Fixed", currency = "ARS", frequency = "Weekly", interval = 1,
            startDate = "2023-10-25", endDate = (string?)null, dueDay = (int?)null, preferredAccountId = (Guid?)null,
            preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2029-10-25", scenario = "Estimated", assumptions = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Where(x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence").ToArray();
        Assert.Equal(314, rows.Length);
        Assert.Equal(314, rows.Select(x => x.GetProperty("sourceId").GetGuid()).Distinct().Count());
        Assert.Equal("2023-10-25", rows[0].GetProperty("date").GetString());
        Assert.Equal("2029-10-24", rows[^1].GetProperty("date").GetString());
    }

    [Fact]
    public async Task projection_older_history_cap_emits_warning()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-older");
        var (_, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-older");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2023-09-01")).StatusCode);

        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2026-10-31", scenario = "Estimated", assumptions = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(doc.RootElement.GetProperty("alerts").EnumerateArray(), x => x.GetProperty("code").GetString() == "RecurringHistoryIncomplete");
        using var calendar = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2023-10-25&through=2026-10-31"));
        Assert.True(calendar.RootElement.GetProperty("olderOccurrencesIncomplete").GetBoolean());
    }

    [Fact]
    public async Task scenario_can_adjust_recurring_occurrence_by_stable_source_identity()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-scenario");
        var (workspaceId, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-scenario");
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "100.00", openingDate = "2026-10-25"
        })).StatusCode);
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2026-10-01", accountId)).StatusCode);
        var occurrenceId = RecurringCalendar.StableOccurrenceId(workspaceId, ruleId, new DateOnly(2026, 10, 1));

        var assumptions = new[]
        {
            new { id = Guid.NewGuid(), kind = "AmountChange", date = (string?)null, currency = "ARS", amount = "25.00", description = "Ajuste", sourceId = (Guid?)occurrenceId, sourceKind = "RecurringOccurrence", componentId = (string?)null }
        };
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2026-10-31", scenario = "Estimated", assumptions
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence");
        Assert.Equal(occurrenceId, row.GetProperty("sourceId").GetGuid());
        Assert.Equal("1025.00", row.GetProperty("amount").GetString());
        Assert.Equal("-925.00", Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS")
            .GetProperty("minimumFreeBalance").GetString());
        await using var db = Db(factory.TestConnection);
        var revision = await db.RecurringRuleRevisions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        Assert.Equal(1000m, revision.Amount);
        Assert.Empty(await db.RecurringOccurrenceActions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).ToArrayAsync());
        Assert.Empty(await db.NonCashSettlements.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
        Assert.Empty(await db.FinanceOperations.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task recorded_date_is_suppressed_without_suppressing_later_months()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-recorded");
        var (_, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-recorded");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2026-10-01")).StatusCode);
        var record = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", new
        {
            requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-25", actualAmount = "1000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true
        });
        Assert.Equal(HttpStatusCode.OK, record.StatusCode);
        var workspaceId = await WorkspaceId(factory, "recurring-projection-recorded");
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2026-12-31", scenario = "Estimated", assumptions = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var expectedIds = new[]
        {
            RecurringCalendar.StableOccurrenceId(workspaceId, ruleId, new DateOnly(2026, 11, 1)),
            RecurringCalendar.StableOccurrenceId(workspaceId, ruleId, new DateOnly(2026, 12, 1))
        };
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Where(x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence" && expectedIds.Contains(x.GetProperty("sourceId").GetGuid())).ToArray();
        Assert.Equal(new[] { "2026-11-01", "2026-12-01" }, rows.Select(x => x.GetProperty("date").GetString()!).ToArray());
    }

    [Fact]
    public async Task incoming_recurring_occurrence_adds_cash_only_to_its_currency()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-income");
        var (workspaceId, _) = await WorkspaceAndCategory(factory, "recurring-projection-income");
        var categoryId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new
        {
            id = categoryId, name = "Sueldo", kind = "Income"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/income-sources", new
        {
            id = sourceId, name = "Trabajo", kind = "Salary"
        })).StatusCode);
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/recurrences", new
        {
            id = ruleId, flow = "Income", description = "Sueldo mensual", categoryId, incomeSourceId = sourceId, merchantId = (Guid?)null,
            amount = "2500.00", amountMode = "Fixed", currency = "USD", frequency = "Monthly", interval = 1,
            startDate = "2026-10-25", endDate = (string?)null, dueDay = (int?)null, preferredAccountId = (Guid?)null,
            preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
        })).StatusCode);

        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2026-10-26", scenario = "Estimated", assumptions = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence");
        Assert.Equal(RecurringCalendar.StableOccurrenceId(workspaceId, ruleId, new DateOnly(2026, 10, 25)), row.GetProperty("sourceId").GetGuid());
        Assert.Equal("Incoming", row.GetProperty("direction").GetString());
        Assert.Equal("USD", row.GetProperty("currency").GetString());
        var usd = Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "USD");
        var ars = Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("2500.00", usd.GetProperty("dailyBalances").EnumerateArray().Last().GetProperty("cashBalance").GetString());
        Assert.Equal("0.00", ars.GetProperty("dailyBalances").EnumerateArray().Last().GetProperty("cashBalance").GetString());
    }

    [Fact]
    public async Task personal_reader_gets_no_foreign_rule_data()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var owner = await factory.AuthenticatedClientAsync("recurring-projection-private-owner");
        using var reader = await factory.AuthenticatedClientAsync("recurring-projection-private-reader");
        var (_, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-private-owner");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(owner, ruleId, categoryId, "2026-10-25")).StatusCode);

        using var response = await reader.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2026-10-26", scenario = "Estimated", assumptions = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.DoesNotContain(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence");
        Assert.DoesNotContain(ruleId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Servicio mensual", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IsolationLevel.RepeatableRead, "repeatable read")]
    [InlineData(IsolationLevel.Serializable, "serializable")]
    public async Task valid_borrowed_projection_transaction_stays_read_write_and_open(IsolationLevel isolationLevel, string expectedIsolation)
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-borrowed-valid");
        var harness = await CreateProjectionHarness(factory, "recurring-projection-borrowed-valid");
        await using var db = harness.Db;
        await using var transaction = await db.Database.BeginTransactionAsync(isolationLevel);

        var (confirmed, confirmedStatus, confirmedError) = await harness.Service.GetAsync(harness.Context,
            DateOnly.FromDateTime(TestInstant.UtcDateTime).AddDays(1), "Confirmed", null, CancellationToken.None);
        Assert.Equal(200, confirmedStatus);
        Assert.Null(confirmedError);
        Assert.NotNull(confirmed);
        Assert.Same(transaction, db.Database.CurrentTransaction);

        var (estimated, estimatedStatus, estimatedError) = await harness.Service.CalculateAsync(harness.Context,
            DateOnly.FromDateTime(TestInstant.UtcDateTime).AddDays(1), [], null, CancellationToken.None);
        Assert.Equal(200, estimatedStatus);
        Assert.Null(estimatedError);
        Assert.NotNull(estimated);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        Assert.Equal(expectedIsolation, await ReadCurrentTransactionSetting(db, "transaction_isolation", transaction));
        Assert.Equal("off", await ReadCurrentTransactionSetting(db, "transaction_read_only", transaction));
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE borrowed_projection_still_writeable (id integer)");
        Assert.Same(transaction, db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task get_rejects_read_committed_borrowed_transaction_before_projection_reads()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-borrowed-weak-get");
        var probe = new ProjectionSourceReadProbeInterceptor();
        var harness = await CreateProjectionHarness(factory, "recurring-projection-borrowed-weak-get", probe);
        await using var db = harness.Db;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        probe.Arm();

        var (projection, status, error) = await harness.Service.GetAsync(harness.Context,
            DateOnly.FromDateTime(TestInstant.UtcDateTime).AddDays(1), "Confirmed", null, CancellationToken.None);

        Assert.Equal(409, status);
        Assert.Null(projection);
        Assert.Contains("RepeatableRead o Serializable", error, StringComparison.Ordinal);
        Assert.Equal(0, probe.ReadCount);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        Assert.Equal("read committed", await ReadCurrentTransactionSetting(db, "transaction_isolation", transaction));
        Assert.Equal("off", await ReadCurrentTransactionSetting(db, "transaction_read_only", transaction));
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE weak_projection_rejection_keeps_transaction (id integer)");
        Assert.Same(transaction, db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task calculate_rejects_read_committed_borrowed_transaction_before_projection_reads()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-borrowed-weak-calculate");
        var probe = new ProjectionSourceReadProbeInterceptor();
        var harness = await CreateProjectionHarness(factory, "recurring-projection-borrowed-weak-calculate", probe);
        await using var db = harness.Db;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        probe.Arm();

        var (projection, status, error) = await harness.Service.CalculateAsync(harness.Context,
            DateOnly.FromDateTime(TestInstant.UtcDateTime).AddDays(1), [], null, CancellationToken.None);

        Assert.Equal(409, status);
        Assert.Null(projection);
        Assert.Contains("RepeatableRead o Serializable", error, StringComparison.Ordinal);
        Assert.Equal(0, probe.ReadCount);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        Assert.Equal("read committed", await ReadCurrentTransactionSetting(db, "transaction_isolation", transaction));
        Assert.Equal("off", await ReadCurrentTransactionSetting(db, "transaction_read_only", transaction));
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE weak_calculation_rejection_keeps_transaction (id integer)");
        Assert.Same(transaction, db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task concurrent_occurrence_record_during_projection_never_counts_estimate_and_actual_together()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var client = await factory.AuthenticatedClientAsync("recurring-projection-concurrent");
        var (_, categoryId) = await WorkspaceAndCategory(factory, "recurring-projection-concurrent");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, "2026-10-01")).StatusCode);

        factory.RecurringProjectionReadBarrier.Arm();
        var projectionTask = client.PostAsJsonAsync("/api/projection/calculate", new
        {
            through = "2026-10-31", scenario = "Estimated", assumptions = Array.Empty<object>()
        });
        await factory.RecurringProjectionReadBarrier.WaitUntilReadAsync().WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            var record = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", new
            {
                requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-25", actualAmount = "1000.00",
                cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
                cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
                catalogOverride = (object?)null, clearPreferredAccount = true
            });
            Assert.Equal(HttpStatusCode.OK, record.StatusCode);
        }
        finally
        {
            factory.RecurringProjectionReadBarrier.Release();
        }

        using var response = await projectionTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await using var db = Db(factory.TestConnection);
        var operationId = await db.RecurringOccurrenceActions.Where(x => x.RuleId == ruleId).Select(x => x.FinanceOperationId).SingleAsync();
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        var estimates = rows.Count(x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence");
        var actuals = rows.Count(x => x.GetProperty("sourceKind").GetString() == "FinanceOperation" && x.GetProperty("sourceId").GetGuid() == operationId);
        Assert.Equal(1, estimates + actuals);
    }

    private static Task<HttpResponseMessage> CreateRule(HttpClient client, Guid id, Guid categoryId, string startDate, Guid? accountId = null) =>
        client.PostAsJsonAsync("/api/recurrences", new
        {
            id, flow = "Expense", description = "Servicio mensual", categoryId, incomeSourceId = (Guid?)null, merchantId = (Guid?)null,
            amount = "1000.00", amountMode = "Fixed", currency = "ARS", frequency = "Monthly", interval = 1,
            startDate, endDate = (string?)null, dueDay = (int?)null, preferredAccountId = accountId, preferredCardId = (Guid?)null,
            healthcareComponents = Array.Empty<object>()
        });

    private static async Task<(Guid WorkspaceId, Guid CategoryId)> WorkspaceAndCategory(ApiFactory factory, string subject)
    {
        await using var db = Db(factory.TestConnection);
        var workspaceId = await db.Workspaces.Where(x => x.OwnerSubject == subject).Select(x => x.Id).SingleAsync();
        var categoryId = await db.FinanceCategories.Where(x => x.WorkspaceId == workspaceId && x.Kind == "Expense" && x.IsDefault).Select(x => x.Id).SingleAsync();
        return (workspaceId, categoryId);
    }

    private static async Task<Guid> WorkspaceId(ApiFactory factory, string subject)
    {
        await using var db = Db(factory.TestConnection);
        return await db.Workspaces.Where(x => x.OwnerSubject == subject).Select(x => x.Id).SingleAsync();
    }

    private static async Task<ProjectionHarness> CreateProjectionHarness(ApiFactory factory, string subject, DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        var db = new AppDbContext(options.Options);
        var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == subject);
        var workspace = await db.Workspaces.SingleAsync(x => x.OwnerUserId == user.Id && x.Kind == "Personal");
        var context = new WorkspaceRequestContext(user, workspace, "Owner", WorkspacePermissions.For("Owner", personalOwner: true));
        var clock = new BusinessClock(new FixedTimeProvider(TestInstant));
        var balances = new SavingsAccountBalanceQuery(db, clock);
        var adapter = new RecurringProjectionAdapter(db, new RecurringCalendar(db), clock);
        return new ProjectionHarness(db, new ProjectionService(db, clock, balances, adapter), context);
    }

    private static async Task<string> ReadCurrentTransactionSetting(AppDbContext db, string setting, IDbContextTransaction transaction)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = $"SHOW {setting}";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static AppDbContext Db(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    private sealed record ProjectionHarness(AppDbContext Db, ProjectionService Service, WorkspaceRequestContext Context);
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider { public override DateTimeOffset GetUtcNow() => utcNow; }
}

public sealed class ProjectionSourceReadProbeInterceptor : DbCommandInterceptor
{
    private int _armed;
    private int _readCount;
    public int ReadCount => Volatile.Read(ref _readCount);
    public void Arm() { Volatile.Write(ref _readCount, 0); Volatile.Write(ref _armed, 1); }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _armed) == 1) Interlocked.Increment(ref _readCount);
        return ValueTask.FromResult(result);
    }
}
