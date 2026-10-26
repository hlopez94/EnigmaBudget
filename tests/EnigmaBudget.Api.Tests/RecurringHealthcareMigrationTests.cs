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
        Assert.Equal(1, await InsertOperationWithoutAccount(db, fixture));
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceOperations\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Income' AND \"AccountId\" IS NULL", fixture.WorkspaceId));
    }

    [Fact]
    public async Task migration_preserves_legacy_finance_operation_fk_values_and_payment_payload()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261025212354_MonthlyFinanceBudgets");
        var legacy = await SeedLegacyPayment(db);

        await migrator.MigrateAsync();

        Assert.Equal(legacy.FinanceOperationId, await Scalar<Guid>(db, "SELECT \"FinanceOperationId\" FROM \"FinancePayments\" WHERE \"Id\" = @id", legacy.PaymentId));
        Assert.Equal(legacy.OperationId, await Scalar<Guid>(db, "SELECT \"OperationId\" FROM \"FinancePayments\" WHERE \"Id\" = @id", legacy.PaymentId));
        Assert.Equal(legacy.AccountId, await Scalar<Guid>(db, "SELECT \"AccountId\" FROM \"FinancePayments\" WHERE \"Id\" = @id", legacy.PaymentId));
        Assert.Equal(123.45m, await Scalar<decimal>(db, "SELECT \"Amount\" FROM \"FinancePayments\" WHERE \"Id\" = @id", legacy.PaymentId));
        Assert.Equal("ARS", await Scalar<string>(db, "SELECT \"Currency\" FROM \"FinancePayments\" WHERE \"Id\" = @id", legacy.PaymentId));
        Assert.True(await IsNullable(db, "FinancePayments", "CommandPayloadHash"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT count(*) FROM \"FinancePayments\" WHERE \"Id\" = @id AND \"CommandPayloadHash\" IS NOT NULL", legacy.PaymentId));
        Assert.Equal(new DateOnly(2026, 10, 15), await Scalar<DateOnly>(db, "SELECT \"Date\" FROM \"FinancePayments\" WHERE \"Id\" = @id", legacy.PaymentId));
        Assert.Equal("Legacy pago sueldo", await Scalar<string>(db, "SELECT \"Description\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", legacy.OperationId));
        Assert.Equal("Sueldo", await Scalar<string>(db, "SELECT \"Category\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", legacy.OperationId));
    }

    [Fact]
    public async Task migration_down_does_not_fabricate_an_account_for_unassigned_operations()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db, nullableAccount: true);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261025212354_MonthlyFinanceBudgets"));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, failure.SqlState);
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceOperations\" WHERE \"WorkspaceId\" = @workspace AND \"AccountId\" IS NULL", fixture.WorkspaceId));
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"RecurringRules\" WHERE \"WorkspaceId\" = @workspace", fixture.WorkspaceId));
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
    public async Task settlement_source_validator_accepts_persisted_actual_payroll_and_rejects_forecast_or_foreign_operation()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db);
        var other = await SeedWorkspace(db);
        var valid = NewSettlement(fixture, "PayrollWithholding", 20m, fixture.ComponentId);
        Assert.True(await OperationSettlementSourceValidator.ValidateAsync(db, await LoadOperation(db, fixture), valid, CancellationToken.None));

        var plannedComponentId = Guid.NewGuid();
        await InsertComponent(db, fixture, plannedComponentId, fixture.RevisionId, null);
        var planned = NewSettlement(fixture, "PayrollWithholding", 20m, plannedComponentId);
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, await LoadOperation(db, fixture), planned, CancellationToken.None));

        var otherOperationId = await InsertOtherOperation(db, fixture);
        var otherOperationActionId = Guid.NewGuid();
        await InsertAction(db, fixture, otherOperationActionId, Guid.NewGuid(), new DateOnly(2026, 10, 2), otherOperationId);
        var otherOperationComponentId = Guid.NewGuid();
        await InsertComponent(db, fixture, otherOperationComponentId, fixture.RevisionId, otherOperationActionId);
        var differentOperationSource = NewSettlement(fixture, "PayrollWithholding", 20m, otherOperationComponentId);
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, await LoadOperation(db, fixture), differentOperationSource, CancellationToken.None));

        var wrongCurrency = NewSettlement(fixture, "PayrollWithholding", 20m, fixture.ComponentId);
        wrongCurrency.Currency = "USD";
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, await LoadOperation(db, fixture), wrongCurrency, CancellationToken.None));

        var foreign = NewSettlement(fixture, "PayrollWithholding", 20m, other.ComponentId);
        var operationCount = await db.FinanceOperations.CountAsync();
        var paymentCount = await db.FinancePayments.CountAsync();
        var settlementCount = await db.NonCashSettlements.CountAsync();
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, await LoadOperation(db, fixture), foreign, CancellationToken.None));
        Assert.Equal(operationCount, await db.FinanceOperations.CountAsync());
        Assert.Equal(paymentCount, await db.FinancePayments.CountAsync());
        Assert.Equal(settlementCount, await db.NonCashSettlements.CountAsync());
    }

    [Fact]
    public async Task settlement_source_validator_checks_persisted_card_purchase_operation_currency_category_and_amount()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db);
        var otherOperationId = await InsertOtherOperation(db, fixture);
        var purchaseId = Guid.NewGuid();
        await InsertPurchase(db, fixture, purchaseId, fixture.OperationId);
        var operation = await LoadOperation(db, fixture);
        var valid = NewSettlement(fixture, "CardCharge", 100m, purchaseId: purchaseId);
        Assert.True(await OperationSettlementSourceValidator.ValidateAsync(db, operation, valid, CancellationToken.None));

        var mismatchedOperationPurchaseId = Guid.NewGuid();
        await InsertPurchase(db, fixture, mismatchedOperationPurchaseId, otherOperationId);
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, operation, NewSettlement(fixture, "CardCharge", 100m, purchaseId: mismatchedOperationPurchaseId), CancellationToken.None));

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Purchases\" SET \"Currency\" = {"USD"} WHERE \"Id\" = {purchaseId}");
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, operation, valid, CancellationToken.None));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Purchases\" SET \"Currency\" = {"ARS"}, \"TotalAmount\" = {99m} WHERE \"Id\" = {purchaseId}");
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, operation, valid, CancellationToken.None));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Purchases\" SET \"TotalAmount\" = {100m}, \"CategoryId\" = {(Guid?)null} WHERE \"Id\" = {purchaseId}");
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, operation, valid, CancellationToken.None));
    }

    [Fact]
    public async Task settlement_source_validator_accepts_only_full_same_operation_root_reversals()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db);
        var otherOperationId = await InsertOtherOperation(db, fixture);
        var rootId = Guid.NewGuid();
        await InsertSettlement(db, fixture, rootId, "PayrollWithholding", 20m, fixture.ComponentId, null, null, Guid.NewGuid());
        var operation = await LoadOperation(db, fixture);
        var full = NewSettlement(fixture, "Reversal", 20m, reversesId: rootId);
        Assert.True(await OperationSettlementSourceValidator.ValidateAsync(db, operation, full, CancellationToken.None));
        var partial = NewSettlement(fixture, "Reversal", 10m, reversesId: rootId);
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, operation, partial, CancellationToken.None));

        var otherOperationRootId = Guid.NewGuid();
        var otherFixture = fixture with { OperationId = otherOperationId };
        var otherActionId = Guid.NewGuid();
        await InsertAction(db, fixture, otherActionId, Guid.NewGuid(), new DateOnly(2026, 10, 2), otherOperationId);
        var otherComponentId = Guid.NewGuid();
        await InsertComponent(db, fixture, otherComponentId, fixture.RevisionId, otherActionId);
        await InsertSettlement(db, otherFixture, otherOperationRootId, "PayrollWithholding", 20m, otherComponentId, null, null, Guid.NewGuid());
        var foreignRoot = NewSettlement(fixture, "Reversal", 20m, reversesId: otherOperationRootId);
        Assert.False(await OperationSettlementSourceValidator.ValidateAsync(db, operation, foreignRoot, CancellationToken.None));
    }

    [Fact]
    public async Task settlement_source_validator_resolves_tracked_added_action_component_purchase_and_root()
    {
        using var migrated = await MigratedDb();
        var db = migrated.Db;
        var fixture = await SeedWorkspace(db);
        var operation = await LoadOperation(db, fixture);
        var actionId = Guid.NewGuid();
        var action = new RecurringOccurrenceAction
        {
            Id = actionId, WorkspaceId = fixture.WorkspaceId, RuleId = fixture.RuleId, RuleRevisionId = fixture.RevisionId,
            ScheduledDate = new DateOnly(2026, 11, 1), Outcome = "Recorded", FinanceOperationId = fixture.OperationId,
            RequestId = Guid.NewGuid(), PayloadHash = "tracked"
        };
        var componentId = Guid.NewGuid();
        var component = new HealthcareComponent
        {
            Id = componentId, WorkspaceId = fixture.WorkspaceId, RuleRevisionId = fixture.RevisionId, OccurrenceActionId = actionId,
            Kind = "PayrollWithholding", Amount = 30m, AppliesToProviderBalance = true
        };
        db.RecurringOccurrenceActions.Add(action);
        db.HealthcareComponents.Add(component);
        Assert.True(await OperationSettlementSourceValidator.ValidateAsync(db, operation, NewSettlement(fixture, "PayrollWithholding", 30m, componentId), CancellationToken.None));

        var trackedPurchaseId = Guid.NewGuid();
        db.Purchases.Add(new CardPurchase
        {
            Id = trackedPurchaseId, WorkspaceId = fixture.WorkspaceId, CardId = fixture.CardId, ExpenseOperationId = fixture.OperationId,
            Date = new DateOnly(2026, 11, 1), Description = "Compra", Category = "Salud", CategoryId = fixture.CategoryId,
            Currency = "ARS", TotalAmount = 40m, InstallmentCount = 1, FirstPeriod = "2026-11"
        });
        Assert.True(await OperationSettlementSourceValidator.ValidateAsync(db, operation, NewSettlement(fixture, "CardCharge", 40m, purchaseId: trackedPurchaseId), CancellationToken.None));

        var trackedRootId = Guid.NewGuid();
        var trackedRoot = NewSettlement(fixture, "PayrollWithholding", 30m, componentId);
        trackedRoot.Id = trackedRootId;
        db.NonCashSettlements.Add(trackedRoot);
        var trackedReversal = NewSettlement(fixture, "Reversal", 30m, reversesId: trackedRootId);
        Assert.True(await OperationSettlementSourceValidator.ValidateAsync(db, operation, trackedReversal, CancellationToken.None));
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
        var reversal = Settlement(operation, "Reversal", 200m);
        reversal.ReversesSettlementId = card.Id;

        var result = OperationSettlementCalculator.Calculate(operation, [payment], [root, card, reversal]);

        Assert.Equal(1000m, result.GrossConsumption);
        Assert.Equal(400m, result.CashPaidAmount);
        Assert.Equal(300m, result.NonCashSettledAmount);
        Assert.Equal(0m, result.CardChargeSettledAmount);
        Assert.Equal(700m, result.TotalSettledAmount);
        Assert.Equal(300m, result.PendingAmount);
        Assert.Equal(1000m, result.OperationMonthBudgetContribution);
        Assert.Equal("Partial", result.Status);
    }

    [Fact]
    public void operation_settlement_calculator_rejects_partial_root_reversal()
    {
        var operation = Operation("Expense", 1000m);
        var root = Settlement(operation, "CardCharge", 200m);
        var partial = Settlement(operation, "Reversal", 100m);
        partial.ReversesSettlementId = root.Id;

        Assert.Throws<InvalidOperationException>(() => OperationSettlementCalculator.Calculate(operation, [], [root, partial]));
    }

    [Fact]
    public void voided_operation_rejects_partial_root_reversal()
    {
        var operation = Operation("Expense", 1000m);
        operation.IsVoided = true;
        var root = Settlement(operation, "CardCharge", 200m);
        var partial = Settlement(operation, "Reversal", 100m);
        partial.ReversesSettlementId = root.Id;

        Assert.Throws<InvalidOperationException>(() => OperationSettlementCalculator.Calculate(operation, [], [root, partial]));
    }

    [Fact]
    public void voided_operation_valid_full_reversal_keeps_historical_cash_and_zeroes_active_totals()
    {
        var operation = Operation("Expense", 1000m);
        operation.IsVoided = true;
        var payment = new FinancePayment { Id = Guid.NewGuid(), WorkspaceId = operation.WorkspaceId, OperationId = operation.Id, AccountId = Guid.NewGuid(), Amount = 300m, Currency = "ARS" };
        var root = Settlement(operation, "CardCharge", 200m);
        var reversal = Settlement(operation, "Reversal", 200m);
        reversal.ReversesSettlementId = root.Id;

        var result = OperationSettlementCalculator.Calculate(operation, [payment], [root, reversal]);

        Assert.Equal(0m, result.GrossConsumption);
        Assert.Equal(300m, result.CashPaidAmount);
        Assert.Equal(0m, result.NonCashSettledAmount);
        Assert.Equal(0m, result.CardChargeSettledAmount);
        Assert.Equal(0m, result.TotalSettledAmount);
        Assert.Equal(0m, result.PendingAmount);
        Assert.Equal(0m, result.OperationMonthBudgetContribution);
        Assert.Equal("Voided", result.Status);
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

    private static AppDbContext Db(ApiFactory factory) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);

    private static async Task<LegacyPayment> SeedLegacyPayment(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var subject = $"legacy-payment-{Guid.NewGuid():N}";
        var accountId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ApplicationUsers\" (\"Id\", \"Subject\", \"EmailVerified\", \"CreatedAt\") VALUES ({userId}, {subject}, {false}, {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Workspaces\" (\"Id\", \"OwnerSubject\", \"Name\", \"Kind\", \"OwnerUserId\", \"IsArchived\", \"Version\") VALUES ({workspaceId}, {subject}, {"Legacy"}, {"Personal"}, {userId}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"Currency\", \"OpeningBalance\", \"OpeningDate\", \"IsArchived\", \"Version\") VALUES ({accountId}, {workspaceId}, {"Sueldo"}, {"Banco"}, {"ARS"}, {0m}, {new DateOnly(2026, 1, 1)}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({operationId}, {workspaceId}, {"Income"}, {accountId}, {new DateOnly(2026, 10, 15)}, {"Legacy pago sueldo"}, {"Sueldo"}, {"ARS"}, {123.45m}, {false}, {2}, {true})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinancePayments\" (\"Id\", \"WorkspaceId\", \"OperationId\", \"AccountId\", \"Date\", \"Amount\", \"Currency\", \"FinanceOperationId\") VALUES ({paymentId}, {workspaceId}, {operationId}, {accountId}, {new DateOnly(2026, 10, 15)}, {123.45m}, {"ARS"}, {operationId})");
        return new(workspaceId, accountId, operationId, paymentId, operationId);
    }

    private sealed record LegacyPayment(Guid WorkspaceId, Guid AccountId, Guid OperationId, Guid PaymentId, Guid FinanceOperationId);

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

    private static Task<int> InsertAction(AppDbContext db, Fixture fixture, Guid id, Guid requestId, DateOnly date, Guid? financeOperationId = null) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"RecurringOccurrenceActions\" (\"Id\", \"WorkspaceId\", \"RuleId\", \"RuleRevisionId\", \"ScheduledDate\", \"Outcome\", \"FinanceOperationId\", \"RequestId\", \"PayloadHash\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.RuleId}, {fixture.RevisionId}, {date}, {"Recorded"}, {financeOperationId ?? fixture.OperationId}, {requestId}, {"payload"})");

    private static Task<int> InsertComponent(AppDbContext db, Fixture fixture, Guid id, Guid revisionId, Guid? actionId, string kind = "PayrollWithholding", bool appliesToProviderBalance = true) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"HealthcareComponents\" (\"Id\", \"WorkspaceId\", \"RuleRevisionId\", \"OccurrenceActionId\", \"Kind\", \"Amount\", \"AppliesToProviderBalance\") VALUES ({id}, {fixture.WorkspaceId}, {revisionId}, {actionId}, {kind}, {20m}, {appliesToProviderBalance})");

    private static Task<int> InsertSettlement(AppDbContext db, Fixture fixture, Guid id, string kind, decimal amount, Guid? componentId, Guid? purchaseId, Guid? reversesId, Guid requestId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"NonCashSettlements\" (\"Id\", \"WorkspaceId\", \"OperationId\", \"Currency\", \"Kind\", \"Amount\", \"HealthcareComponentId\", \"CardPurchaseId\", \"ReversesSettlementId\", \"RequestId\", \"PayloadHash\", \"ActorUserId\", \"CreatedAt\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.OperationId}, {"ARS"}, {kind}, {amount}, {componentId}, {purchaseId}, {reversesId}, {requestId}, {"hash"}, {fixture.UserId}, {DateTimeOffset.UtcNow})");

    private static Task<int> InsertPayment(AppDbContext db, Fixture fixture, Guid id, Guid? accountId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinancePayments\" (\"Id\", \"WorkspaceId\", \"OperationId\", \"AccountId\", \"Date\", \"Amount\", \"Currency\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.OperationId}, {accountId}, {new DateOnly(2026, 10, 1)}, {20m}, {"ARS"})");

    private static Task<int> InsertPurchase(AppDbContext db, Fixture fixture, Guid id, Guid operationId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Purchases\" (\"Id\", \"WorkspaceId\", \"CardId\", \"Date\", \"Description\", \"Category\", \"CategoryId\", \"Currency\", \"TotalAmount\", \"InstallmentCount\", \"FirstPeriod\", \"ExpenseOperationId\") VALUES ({id}, {fixture.WorkspaceId}, {fixture.CardId}, {new DateOnly(2026, 10, 1)}, {"Compra"}, {"Salud"}, {fixture.CategoryId}, {"ARS"}, {100m}, {1}, {"2026-10"}, {operationId})");

    private static async Task<FinanceOperation> LoadOperation(AppDbContext db, Fixture fixture) =>
        await db.FinanceOperations.SingleAsync(operation => operation.WorkspaceId == fixture.WorkspaceId && operation.Id == fixture.OperationId);

    private static NonCashSettlement NewSettlement(Fixture fixture, string kind, decimal amount, Guid? componentId = null, Guid? purchaseId = null, Guid? reversesId = null) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = fixture.WorkspaceId, OperationId = fixture.OperationId, Currency = "ARS", Kind = kind,
        Amount = amount, HealthcareComponentId = componentId, CardPurchaseId = purchaseId, ReversesSettlementId = reversesId,
        RequestId = Guid.NewGuid(), PayloadHash = "source-test", ActorUserId = fixture.UserId, CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task<Guid> InsertOtherOperation(AppDbContext db, Fixture fixture)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"CategoryId\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({id}, {fixture.WorkspaceId}, {"Expense"}, {fixture.AccountId}, {new DateOnly(2026, 10, 2)}, {"Otra consulta"}, {"Salud"}, {fixture.CategoryId}, {"ARS"}, {100m}, {false}, {1}, {false})");
        return id;
    }

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
