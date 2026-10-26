using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Net.Http.Json;
using System.Net;

namespace EnigmaBudget.Api.Tests;

public sealed class FinanceCatalogMigrationTests
{
    private const string PreviousMigration = "20261025180000_ProjectionAssumptionSourceIdentity";

    [Fact]
    public async Task legacy_category_backfill_preserves_labels_and_is_workspace_scoped()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db);

        await migrator.MigrateAsync();

        Assert.Equal(9, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace", rows.WorkspaceA));
        Assert.Equal(3, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace", rows.WorkspaceB));
        Assert.Equal(rows.OperationA, await Scalar<Guid>(db, "SELECT \"Id\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA));
        Assert.Equal("Gasto histórico", await Scalar<string>(db, "SELECT \"Category\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA));
        Assert.Equal(321.09m, await Scalar<decimal>(db, "SELECT \"Amount\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA));
        Assert.Equal(7, await Scalar<int>(db, "SELECT \"Version\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA));
        Assert.Equal(new DateOnly(2024, 2, 4), await Scalar<DateOnly>(db, "SELECT \"Date\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA));
        Assert.NotEqual(Guid.Empty, await Scalar<Guid>(db, "SELECT \"CategoryId\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationB));
        Assert.True(await Scalar<bool>(db, "SELECT \"IsVoided\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationB));
        Assert.NotEqual(
            await Scalar<Guid>(db, "SELECT \"CategoryId\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA),
            await Scalar<Guid>(db, "SELECT \"CategoryId\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationB));
    }

    [Fact]
    public async Task case_and_spacing_distinct_legacy_buckets_are_preserved()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db);
        await migrator.MigrateAsync();

        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Expense' AND \"Name\" = 'Gasto histórico'", rows.WorkspaceA));
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Expense' AND \"Name\" = 'gasto histórico ' ", rows.WorkspaceA));
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Expense' AND \"Name\" = 'gasto histórico'", rows.WorkspaceA));
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Expense' AND \"Name\" = 'Gasto histórico '", rows.WorkspaceA));
    }

    [Fact]
    public async Task uncategorized_expense_and_income_are_seeded_for_empty_workspace()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var workspace = await SeedEmptyWorkspace(db);
        await migrator.MigrateAsync();

        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Name\" = 'Sin clasificar' AND \"Kind\" = 'Expense'", workspace));
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Name\" = 'Sin clasificar' AND \"Kind\" = 'Income'", workspace));
        Assert.Equal(2, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"IsDefault\"", workspace));
        Assert.Equal("false", await ColumnDefault(db, "FinanceCategories", "IsArchived"));
        Assert.Equal("1", await ColumnDefault(db, "FinanceCategories", "Version"));
    }

    [Fact]
    public async Task empty_label_maps_to_uncategorized_category_of_known_kind()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db, operationCategory: "\u00a0\u2003");
        await migrator.MigrateAsync();

        var id = await Scalar<Guid>(db, "SELECT \"CategoryId\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA);
        Assert.Equal("Sin clasificar", await Scalar<string>(db, "SELECT \"Name\" FROM \"FinanceCategories\" WHERE \"Id\" = @id", id));
        Assert.Equal("Expense", await Scalar<string>(db, "SELECT \"Kind\" FROM \"FinanceCategories\" WHERE \"Id\" = @id", id));
        Assert.Equal("\u00a0\u2003", await Scalar<string>(db, "SELECT \"Category\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", rows.OperationA));
    }

    [Fact]
    public async Task income_label_does_not_infer_source()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db, incomeCategory: "Sueldo");
        await migrator.MigrateAsync();

        Assert.Equal(0, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceIncomeSources\" WHERE \"WorkspaceId\" = @workspace", rows.WorkspaceA));
        Assert.Equal("Income", await Scalar<string>(db, "SELECT c.\"Kind\" FROM \"FinanceCategories\" c JOIN \"FinanceOperations\" o ON c.\"Id\" = o.\"CategoryId\" WHERE o.\"Id\" = @id", rows.IncomeOperation));
    }

    [Fact]
    public async Task merchants_and_income_sources_start_empty()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db);
        await migrator.MigrateAsync();

        Assert.Equal(0, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceMerchants\" WHERE \"WorkspaceId\" = @workspace", rows.WorkspaceA));
        Assert.Equal(0, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceIncomeSources\" WHERE \"WorkspaceId\" = @workspace", rows.WorkspaceA));
    }

    [Fact]
    public async Task card_purchase_and_adjustment_categories_are_preserved()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db);
        await migrator.MigrateAsync();

        Assert.Equal("Tarjeta", await Scalar<string>(db, "SELECT \"Category\" FROM \"Purchases\" WHERE \"Id\" = @id", rows.PurchaseId));
        Assert.Equal(450m, await Scalar<decimal>(db, "SELECT \"TotalAmount\" FROM \"Purchases\" WHERE \"Id\" = @id", rows.PurchaseId));
        Assert.Equal(new DateOnly(2024, 2, 2), await Scalar<DateOnly>(db, "SELECT \"Date\" FROM \"Purchases\" WHERE \"Id\" = @id", rows.PurchaseId));
        Assert.Equal("Expense", await Scalar<string>(db, "SELECT c.\"Kind\" FROM \"FinanceCategories\" c JOIN \"Purchases\" p ON c.\"Id\" = p.\"CategoryId\" WHERE p.\"Id\" = @id", rows.PurchaseId));
        Assert.Equal("Ajuste", await Scalar<string>(db, "SELECT \"Category\" FROM \"StatementAdjustments\" WHERE \"Id\" = @id", rows.AdjustmentId));
        Assert.Equal(-4m, await Scalar<decimal>(db, "SELECT \"Amount\" FROM \"StatementAdjustments\" WHERE \"Id\" = @id", rows.AdjustmentId));
        Assert.Equal("Expense", await Scalar<string>(db, "SELECT c.\"Kind\" FROM \"FinanceCategories\" c JOIN \"StatementAdjustments\" a ON c.\"Id\" = a.\"CategoryId\" WHERE a.\"Id\" = @id", rows.AdjustmentId));
    }

    [Fact]
    public async Task same_label_in_two_workspaces_creates_distinct_categories()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db);
        await migrator.MigrateAsync();

        var a = await Scalar<Guid>(db, "SELECT \"Id\" FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Expense' AND \"Name\" = 'Gasto histórico'", rows.WorkspaceA);
        var b = await Scalar<Guid>(db, "SELECT \"Id\" FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Expense' AND \"Name\" = 'Gasto histórico'", rows.WorkspaceB);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task catalog_foreign_keys_reject_cross_workspace_category_references()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var rows = await SeedLegacyRows(db);
        await migrator.MigrateAsync();
        var categoryB = await Scalar<Guid>(db, "SELECT \"Id\" FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace AND \"Kind\" = 'Expense' AND \"Name\" = 'Gasto histórico'", rows.WorkspaceB);

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"FinanceOperations\" SET \"CategoryId\" = {categoryB} WHERE \"Id\" = {rows.OperationA}"));
    }

    [Fact]
    public async Task new_personal_and_household_receive_defaults_atomically()
    {
        using var factory = new ApiFactory(environment: "Development");
        await using var db = Db(factory);
        await db.Database.MigrateAsync();
        var client = await factory.AuthenticatedClientAsync("catalog-personal-and-household");
        using var meResponse = await client.GetAsync("/api/me");
        meResponse.EnsureSuccessStatusCode();
        using var me = System.Text.Json.JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        var personalId = me.RootElement.GetProperty("personalWorkspaceId").GetGuid();
        Assert.Equal(2, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace", personalId));

        using var householdResponse = await client.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar de prueba" });
        Assert.Equal(HttpStatusCode.Created, householdResponse.StatusCode);
        using var household = System.Text.Json.JsonDocument.Parse(await householdResponse.Content.ReadAsStringAsync());
        var householdId = household.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(2, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace", householdId));
    }

    [Fact]
    public async Task repeated_existing_workspace_bootstrap_does_not_add_catalog_rows()
    {
        using var factory = new ApiFactory(environment: "Development");
        await using var db = Db(factory);
        await db.Database.MigrateAsync();
        var client = await factory.AuthenticatedClientAsync("catalog-repeat-bootstrap");
        using var first = await client.GetAsync("/api/me");
        first.EnsureSuccessStatusCode();
        using var second = await client.GetAsync("/api/workspaces");
        second.EnsureSuccessStatusCode();
        using var me = System.Text.Json.JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var workspaceId = me.RootElement.GetProperty("personalWorkspaceId").GetGuid();
        Assert.Equal(2, await Scalar<int>(db, "SELECT count(*) FROM \"FinanceCategories\" WHERE \"WorkspaceId\" = @workspace", workspaceId));
    }

    private static async Task<LegacyRows> SeedLegacyRows(AppDbContext db, string operationCategory = "Gasto histórico", string incomeCategory = "Ingreso fijo")
    {
        var workspaceA = await SeedEmptyWorkspace(db);
        var workspaceB = await SeedEmptyWorkspace(db);
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"Currency\", \"OpeningBalance\", \"OpeningDate\", \"IsArchived\", \"Version\") VALUES ({accountA}, {workspaceA}, {"A"}, {"Banco"}, {"ARS"}, {0m}, {new DateOnly(2024, 1, 1)}, {false}, {1}), ({accountB}, {workspaceB}, {"B"}, {"Banco"}, {"ARS"}, {0m}, {new DateOnly(2024, 1, 1)}, {false}, {1})");
        var operationA = Guid.NewGuid();
        var operationB = Guid.NewGuid();
        var operationSpacing = Guid.NewGuid();
        var operationCase = Guid.NewGuid();
        var operationTrailingSpace = Guid.NewGuid();
        var incomeOperation = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({operationA}, {workspaceA}, {"Expense"}, {accountA}, {new DateOnly(2024, 2, 4)}, {"Histórico"}, {operationCategory}, {"ARS"}, {321.09m}, {false}, {7}, {true}), ({operationSpacing}, {workspaceA}, {"Expense"}, {accountA}, {new DateOnly(2024, 2, 4)}, {"Otro histórico"}, {"gasto histórico "}, {"ARS"}, {82m}, {false}, {4}, {false}), ({operationCase}, {workspaceA}, {"Expense"}, {accountA}, {new DateOnly(2024, 2, 4)}, {"Otro histórico"}, {"gasto histórico"}, {"ARS"}, {83m}, {false}, {5}, {false}), ({operationTrailingSpace}, {workspaceA}, {"Expense"}, {accountA}, {new DateOnly(2024, 2, 4)}, {"Otro histórico"}, {"Gasto histórico "}, {"ARS"}, {84m}, {false}, {6}, {false}), ({operationB}, {workspaceB}, {"Expense"}, {accountB}, {new DateOnly(2024, 2, 4)}, {"Otra copia"}, {"Gasto histórico"}, {"ARS"}, {75m}, {true}, {3}, {false}), ({incomeOperation}, {workspaceA}, {"Income"}, {accountA}, {new DateOnly(2024, 2, 5)}, {"Ingreso"}, {incomeCategory}, {"ARS"}, {900m}, {false}, {2}, {true})");
        var cardId = Guid.NewGuid();
        var statementId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();
        var adjustmentId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Cards\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"StatementDebtArs\", \"StatementDebtUsd\", \"AsOfDate\", \"IsArchived\", \"Version\") VALUES ({cardId}, {workspaceA}, {"Tarjeta"}, {"Banco"}, {0m}, {0m}, {new DateOnly(2024, 1, 1)}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CardStatements\" (\"Id\", \"WorkspaceId\", \"CardId\", \"Period\", \"Currency\", \"Version\", \"State\") VALUES ({statementId}, {workspaceA}, {cardId}, {"2024-02"}, {"ARS"}, {1}, {"Draft"})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Purchases\" (\"Id\", \"WorkspaceId\", \"CardId\", \"Date\", \"Description\", \"Category\", \"Currency\", \"TotalAmount\", \"InstallmentCount\", \"FirstPeriod\") VALUES ({purchaseId}, {workspaceA}, {cardId}, {new DateOnly(2024, 2, 2)}, {"Compra"}, {"Tarjeta"}, {"ARS"}, {450m}, {1}, {"2024-02"})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"StatementAdjustments\" (\"Id\", \"WorkspaceId\", \"StatementId\", \"Description\", \"Category\", \"Amount\") VALUES ({adjustmentId}, {workspaceA}, {statementId}, {"Ajuste"}, {"Ajuste"}, {-4m})");
        return new(workspaceA, workspaceB, operationA, operationB, incomeOperation, purchaseId, adjustmentId);
    }

    private static async Task<Guid> SeedEmptyWorkspace(AppDbContext db)
    {
        var subject = $"catalog-owner-{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ApplicationUsers\" (\"Id\", \"Subject\", \"EmailVerified\", \"CreatedAt\") VALUES ({userId}, {subject}, {false}, {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Workspaces\" (\"Id\", \"OwnerSubject\", \"Name\", \"Kind\", \"OwnerUserId\", \"IsArchived\", \"Version\") VALUES ({workspaceId}, {subject}, {"Personal"}, {"Personal"}, {userId}, {false}, {1})");
        return workspaceId;
    }

    private static async Task<T> Scalar<T>(AppDbContext db, string sql, object value)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
        var parameter = System.Text.RegularExpressions.Regex.Match(sql, "@[A-Za-z][A-Za-z0-9_]*").Value[1..];
        command.Parameters.AddWithValue(parameter, value);
        var result = await command.ExecuteScalarAsync();
        if (result is null) return default!;
        if (result is T typed) return typed;
        return (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static async Task<string?> ColumnDefault(AppDbContext db, string table, string column)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT column_default FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column", (NpgsqlConnection)db.Database.GetDbConnection());
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (string)result;
    }

    private static AppDbContext Db(ApiFactory factory) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
    private sealed record LegacyRows(Guid WorkspaceA, Guid WorkspaceB, Guid OperationA, Guid OperationB, Guid IncomeOperation, Guid PurchaseId, Guid AdjustmentId);
}
