using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EnigmaBudget.Api.Tests;

public sealed class RecurringHealthcareMigrationTests
{
    [Fact]
    public async Task rule_revision_versions_are_unique_and_state_is_valid()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertRevision(db, fixture, Guid.NewGuid(), 1, "Active"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var invalidState = await Assert.ThrowsAsync<PostgresException>(() => InsertRevision(db, fixture, Guid.NewGuid(), 2, "Archived"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidState.SqlState);
    }

    [Fact]
    public async Task occurrence_action_is_unique_by_rule_date_and_request_id()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db);
        var duplicateDate = await Assert.ThrowsAsync<PostgresException>(() => InsertAction(db, fixture, Guid.NewGuid(), fixture.ActionRequestId, new DateOnly(2026, 10, 1)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateDate.SqlState);
        var duplicateRequest = await Assert.ThrowsAsync<PostgresException>(() => InsertAction(db, fixture, Guid.NewGuid(), fixture.ActionRequestId, new DateOnly(2026, 10, 2)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateRequest.SqlState);
    }

    [Fact]
    public async Task noncash_settlement_positive_decimal_actor_idempotency_and_composite_fks()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var first = await SeedWorkspace(db);
        var other = await SeedWorkspace(db);
        await InsertSettlement(db, first, first.SettlementRootId, "PayrollWithholding", 20m, first.ComponentId, null, null, first.SettlementRequestId);
        var badAmount = await Assert.ThrowsAsync<PostgresException>(() => InsertSettlement(db, first, Guid.NewGuid(), "PayrollWithholding", 0m, first.ComponentId, null, null, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badAmount.SqlState);
        var badSource = await Assert.ThrowsAsync<PostgresException>(() => InsertSettlement(db, first, Guid.NewGuid(), "PayrollWithholding", 10m, null, null, null, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badSource.SqlState);
        var crossWorkspace = await Assert.ThrowsAsync<PostgresException>(() => InsertSettlement(db, first, Guid.NewGuid(), "PayrollWithholding", 10m, other.ComponentId, null, null, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossWorkspace.SqlState);
        var duplicateRequest = await Assert.ThrowsAsync<PostgresException>(() => InsertSettlement(db, first, Guid.NewGuid(), "Reversal", 10m, null, null, first.SettlementRootId, first.SettlementRequestId));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateRequest.SqlState);
        Assert.Equal("numeric", await ColumnType(db, "NonCashSettlements", "Amount"));
    }

    [Fact]
    public async Task one_compensation_per_settlement()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db);
        await InsertSettlement(db, fixture, fixture.SettlementRootId, "PayrollWithholding", 20m, fixture.ComponentId, null, null, fixture.SettlementRequestId);
        await InsertSettlement(db, fixture, Guid.NewGuid(), "Reversal", 20m, null, null, fixture.SettlementRootId, Guid.NewGuid());
        var secondCompensation = await Assert.ThrowsAsync<PostgresException>(() => InsertSettlement(db, fixture, Guid.NewGuid(), "Reversal", 20m, null, null, fixture.SettlementRootId, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, secondCompensation.SqlState);
    }

    [Fact]
    public async Task operation_account_can_be_null_for_recurring_or_undecided_expense()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db, nullableAccount: true);
        Assert.True(await IsNullable(db, "FinanceOperations", "AccountId"));
        Assert.Null(new OperationDto(Guid.NewGuid(), "Expense", null, "2026-10-25", "Consulta", "Salud", "100.00", "ARS", "0.00", "100.00", "Pending", 1, [] ).AccountId);
        var incomeWithoutAccount = await Assert.ThrowsAsync<PostgresException>(() => InsertOperationWithoutAccount(db, fixture));
        Assert.Equal(PostgresErrorCodes.CheckViolation, incomeWithoutAccount.SqlState);
    }

    [Fact]
    public async Task cash_payment_always_requires_account()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        Assert.False(await IsNullable(db, "FinancePayments", "AccountId"));
        var fixture = await SeedWorkspace(db);
        await InsertPayment(db, fixture, Guid.NewGuid(), fixture.AccountId);
        var missingAccount = await Assert.ThrowsAsync<PostgresException>(() => InsertPayment(db, fixture, Guid.NewGuid(), null));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, missingAccount.SqlState);
    }

    [Fact]
    public async Task linked_purchase_has_workspace_scoped_operation_fk()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var first = await SeedWorkspace(db);
        var other = await SeedWorkspace(db);
        await InsertPurchase(db, first, Guid.NewGuid(), first.OperationId);
        var duplicateLink = await Assert.ThrowsAsync<PostgresException>(() => InsertPurchase(db, first, Guid.NewGuid(), first.OperationId));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateLink.SqlState);
        var crossWorkspace = await Assert.ThrowsAsync<PostgresException>(() => InsertPurchase(db, first, Guid.NewGuid(), other.OperationId));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossWorkspace.SqlState);
    }

    [Fact]
    public async Task healthcare_components_retain_kind_and_applied_withholding_flag()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var first = await SeedWorkspace(db);
        var other = await SeedWorkspace(db);
        var plannedId = Guid.NewGuid();
        await InsertComponent(db, first, plannedId, first.RevisionId, null);
        Assert.Equal(2, await Scalar<int>(db, "SELECT count(*) FROM \"HealthcareComponents\" WHERE \"WorkspaceId\" = @workspace", first.WorkspaceId));
        var invalidAppliedFlag = await Assert.ThrowsAsync<PostgresException>(() => InsertComponent(db, first, Guid.NewGuid(), first.RevisionId, null, "ProviderCharge", true));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidAppliedFlag.SqlState);
        var crossWorkspaceRevision = await Assert.ThrowsAsync<PostgresException>(() => InsertComponent(db, first, Guid.NewGuid(), other.RevisionId, null));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossWorkspaceRevision.SqlState);
        var crossWorkspaceAction = await Assert.ThrowsAsync<PostgresException>(() => InsertComponent(db, other, Guid.NewGuid(), other.RevisionId, first.ActionId));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossWorkspaceAction.SqlState);
        Assert.Equal("numeric", await ColumnType(db, "HealthcareComponents", "Amount"));
    }

    [Fact]
    public async Task migration_does_not_create_future_operations()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        Assert.Equal(0, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceOperations\""));
        Assert.Equal(0, await Scalar<int>(db, "SELECT count(*) FROM \"RecurringOccurrenceActions\""));
    }

    [Fact]
    public void cash_payroll_card_and_reversal_calculations_preserve_gross_consumption()
    {
        var operation = Operation("Expense", 1000m);
        var payment = new FinancePayment { Id = Guid.NewGuid(), WorkspaceId = operation.WorkspaceId, OperationId = operation.Id, AccountId = Guid.NewGuid(), Amount = 400m, Currency = "ARS" };
        var root = Settlement(operation, "PayrollWithholding", 300m);
        var card = Settlement(operation, "CardCharge", 200m);
        var reversal = Settlement(operation, "Reversal", 100m);
        reversal.ReversesSettlementId = card.Id;

        var result = OperationSettlementCalculator.Calculate(operation, [payment], [root, card, reversal]);

        Assert.Equal(1000m, result.GrossConsumption);
        Assert.Equal(400m, result.CashPaidAmount);
        Assert.Equal(400m, result.NonCashSettledAmount);
        Assert.Equal(100m, result.CardChargeSettledAmount);
        Assert.Equal(800m, result.TotalSettledAmount);
        Assert.Equal(200m, result.PendingAmount);
        Assert.Equal(900m, result.OperationMonthBudgetContribution);
        Assert.Equal("Partial", result.Status);
    }

    [Fact]
    public void voided_operation_keeps_cash_history_but_clears_active_settlement_totals()
    {
        var operation = Operation("Expense", 1000m);
        operation.IsVoided = true;
        var payment = new FinancePayment { Id = Guid.NewGuid(), WorkspaceId = operation.WorkspaceId, OperationId = operation.Id, AccountId = Guid.NewGuid(), Amount = 300m, Currency = "ARS" };
        var settlement = Settlement(operation, "PayrollWithholding", 200m);

        var result = OperationSettlementCalculator.Calculate(operation, [payment], [settlement]);

        Assert.Equal(0m, result.GrossConsumption);
        Assert.Equal(300m, result.CashPaidAmount);
        Assert.Equal(0m, result.NonCashSettledAmount);
        Assert.Equal(0m, result.TotalSettledAmount);
        Assert.Equal(0m, result.PendingAmount);
        Assert.Equal(0m, result.OperationMonthBudgetContribution);
        Assert.Equal("Voided", result.Status);
    }

    private static FinanceOperation Operation(string kind, decimal amount) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = Guid.NewGuid(), Kind = kind, Date = new DateOnly(2026, 10, 1),
        Description = "Consulta", Category = "Salud", Currency = "ARS", Amount = amount
    };

    private static NonCashSettlement Settlement(FinanceOperation operation, string kind, decimal amount) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = operation.WorkspaceId, OperationId = operation.Id,
        Currency = "ARS", Kind = kind, Amount = amount, RequestId = Guid.NewGuid(), PayloadHash = "hash",
        ActorUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task<MigratedDatabase> MigratedDb()
    {
        var factory = new ApiFactory(environment: "Testing");
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
        if (Environment.GetEnvironmentVariable("RECURRING_TASK1_TEST_BEFORE_MIGRATION") == "1")
            await db.GetService<IMigrator>().MigrateAsync("20261025212354_MonthlyFinanceBudgets");
        else
            await db.Database.MigrateAsync();
        return new(factory, db);
    }

    private sealed class MigratedDatabase(ApiFactory factory, AppDbContext db) : IDisposable
    {
        public AppDbContext Db { get; } = db;
        public void Dispose()
        {
            Db.Dispose();
            factory.Dispose();
        }
    }

    private static async Task<Fixture> SeedWorkspace(AppDbContext db, bool nullableAccount = false)
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var subject = $"recurring-fixture-{Guid.NewGuid():N}";
        var categoryId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var actionRequestId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var settlementRootId = Guid.NewGuid();
        var settlementRequestId = Guid.NewGuid();
        var scheduledDate = new DateOnly(2026, 10, 1);

        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ApplicationUsers\" (\"Id\", \"Subject\", \"EmailVerified\", \"CreatedAt\") VALUES ({userId}, {subject}, {false}, {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Workspaces\" (\"Id\", \"OwnerSubject\", \"Name\", \"Kind\", \"OwnerUserId\", \"IsArchived\", \"Version\") VALUES ({workspaceId}, {subject}, {"Fixture"}, {"Personal"}, {userId}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceCategories\" (\"Id\", \"WorkspaceId\", \"Kind\", \"Name\", \"IsArchived\", \"IsDefault\", \"Version\") VALUES ({categoryId}, {workspaceId}, {"Expense"}, {"Salud"}, {false}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"Currency\", \"OpeningBalance\", \"OpeningDate\", \"IsArchived\", \"Version\") VALUES ({accountId}, {workspaceId}, {"Cuenta"}, {"Banco"}, {"ARS"}, {0m}, {new DateOnly(2026, 1, 1)}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Cards\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"StatementDebtArs\", \"StatementDebtUsd\", \"AsOfDate\", \"IsArchived\", \"Version\") VALUES ({cardId}, {workspaceId}, {"Tarjeta"}, {"Banco"}, {0m}, {0m}, {new DateOnly(2026, 1, 1)}, {false}, {1})");
        Guid? operationAccountId = nullableAccount ? null : accountId;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"CategoryId\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({operationId}, {workspaceId}, {"Expense"}, {operationAccountId}, {scheduledDate}, {"Consulta"}, {"Salud"}, {categoryId}, {"ARS"}, {100m}, {false}, {1}, {false})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"RecurringRules\" (\"Id\", \"WorkspaceId\", \"CurrentVersion\", \"State\") VALUES ({ruleId}, {workspaceId}, {1}, {"Active"})");
        await InsertRevision(db, new Fixture(workspaceId, userId, categoryId, accountId, cardId, operationId, ruleId, revisionId, actionId, actionRequestId, componentId, settlementRootId, settlementRequestId), revisionId, 1, "Active");
        var fixture = new Fixture(workspaceId, userId, categoryId, accountId, cardId, operationId, ruleId, revisionId, actionId, actionRequestId, componentId, settlementRootId, settlementRequestId);
        await InsertAction(db, fixture, actionId, actionRequestId, scheduledDate);
        await InsertComponent(db, fixture, componentId, revisionId, actionId);
        return fixture;
    }

    private static Task<int> InsertRevision(AppDbContext db, Fixture fixture, Guid id, int version, string state) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"RecurringRuleRevisions\" (\"Id\", \"WorkspaceId\", \"RuleId\", \"Version\", \"EffectiveFrom\", \"State\", \"ScheduleAnchorDate\", \"Flow\", \"Description\", \"CategoryId\", \"Amount\", \"AmountMode\", \"Currency\", \"Frequency\", \"Interval\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.RuleId}, {version}, {new DateOnly(2026, 10, 1)}, {state}, {new DateOnly(2026, 10, 1)}, {"Expense"}, {"Consulta"}, {fixture.CategoryId}, {100m}, {"Fixed"}, {"ARS"}, {"Monthly"}, {1})");

    private static Task<int> InsertAction(AppDbContext db, Fixture fixture, Guid id, Guid requestId, DateOnly date) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"RecurringOccurrenceActions\" (\"Id\", \"WorkspaceId\", \"RuleId\", \"RuleRevisionId\", \"ScheduledDate\", \"Outcome\", \"FinanceOperationId\", \"RequestId\", \"PayloadHash\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.RuleId}, {fixture.RevisionId}, {date}, {"Recorded"}, {fixture.OperationId}, {requestId}, {"payload"})");

    private static Task<int> InsertComponent(AppDbContext db, Fixture fixture, Guid id, Guid revisionId, Guid? actionId, string kind = "PayrollWithholding", bool appliesToProviderBalance = true) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"HealthcareComponents\" (\"Id\", \"WorkspaceId\", \"RuleRevisionId\", \"OccurrenceActionId\", \"Kind\", \"Amount\", \"AppliesToProviderBalance\") VALUES ({id}, {fixture.WorkspaceId}, {revisionId}, {actionId}, {kind}, {20m}, {appliesToProviderBalance})");

    private static Task<int> InsertSettlement(AppDbContext db, Fixture fixture, Guid id, string kind, decimal amount, Guid? componentId, Guid? purchaseId, Guid? reversesId, Guid requestId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"NonCashSettlements\" (\"Id\", \"WorkspaceId\", \"OperationId\", \"Currency\", \"Kind\", \"Amount\", \"HealthcareComponentId\", \"CardPurchaseId\", \"ReversesSettlementId\", \"RequestId\", \"PayloadHash\", \"ActorUserId\", \"CreatedAt\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.OperationId}, {"ARS"}, {kind}, {amount}, {componentId}, {purchaseId}, {reversesId}, {requestId}, {"hash"}, {fixture.UserId}, {DateTimeOffset.UtcNow})");

    private static Task<int> InsertPayment(AppDbContext db, Fixture fixture, Guid id, Guid? accountId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinancePayments\" (\"Id\", \"WorkspaceId\", \"OperationId\", \"AccountId\", \"Date\", \"Amount\", \"Currency\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.OperationId}, {accountId}, {new DateOnly(2026, 10, 1)}, {20m}, {"ARS"})");

    private static Task<int> InsertPurchase(AppDbContext db, Fixture fixture, Guid id, Guid operationId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Purchases\" (\"Id\", \"WorkspaceId\", \"CardId\", \"Date\", \"Description\", \"Category\", \"Currency\", \"TotalAmount\", \"InstallmentCount\", \"FirstPeriod\", \"ExpenseOperationId\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.CardId}, {new DateOnly(2026, 10, 1)}, {"Compra"}, {"Salud"}, {"ARS"}, {100m}, {1}, {"2026-10"}, {operationId})");

    private static Task<int> InsertOperationWithoutAccount(AppDbContext db, Fixture fixture) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"CategoryId\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({Guid.NewGuid()}, {fixture.WorkspaceId}, {"Income"}, {(Guid?)null}, {new DateOnly(2026, 10, 1)}, {"Ingreso"}, {"Salud"}, {fixture.CategoryId}, {"ARS"}, {100m}, {false}, {1}, {false})");

    private sealed record Fixture(Guid WorkspaceId, Guid UserId, Guid CategoryId, Guid AccountId, Guid CardId, Guid OperationId, Guid RuleId, Guid RevisionId, Guid ActionId, Guid ActionRequestId, Guid ComponentId, Guid SettlementRootId, Guid SettlementRequestId);

    private static async Task<bool> IsNullable(AppDbContext db, string table, string column) =>
        await Scalar<string>(db, "SELECT is_nullable FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column", table, column) == "YES";

    private static async Task<string> ColumnType(AppDbContext db, string table, string column) =>
        await Scalar<string>(db, "SELECT data_type FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column", table, column);

    private static async Task<bool> HasForeignKey(AppDbContext db, string table, string column1, string column2) =>
        await Scalar<int>(db, "SELECT count(*) FROM (SELECT k.constraint_name FROM information_schema.key_column_usage k JOIN information_schema.table_constraints c ON c.constraint_name = k.constraint_name AND c.constraint_schema = k.constraint_schema WHERE c.constraint_type = 'FOREIGN KEY' AND k.table_schema = current_schema() AND k.table_name = @table AND k.column_name IN (@first, @second) GROUP BY k.constraint_name HAVING count(*) = 2 AND count(DISTINCT k.column_name) = 2) f", table, column1, column2) > 0;

    private static async Task<T> Scalar<T>(AppDbContext db, string sql, params object[] values)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
        var names = System.Text.RegularExpressions.Regex.Matches(sql, "@[A-Za-z][A-Za-z0-9_]*").Select(x => x.Value[1..]).Distinct().ToArray();
        for (var i = 0; i < names.Length; i++) command.Parameters.AddWithValue(names[i], values[i]);
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }
}
