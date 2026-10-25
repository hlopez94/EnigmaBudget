using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Text.RegularExpressions;

namespace EnigmaBudget.Api.Tests;

public sealed class SavingsMigrationTests
{
    private const string PreviousMigration = "20261025143000_NativeSessions";

    [Fact]
    public async Task Savings_schema_has_expected_precision_keys_indexes_and_workspace_constraints()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        await db.Database.MigrateAsync();

        Assert.Equal("numeric(18,2)", await ColumnType(db, "SavingsGoals", "TargetAmount"));
        Assert.Equal("numeric(18,2)", await ColumnType(db, "SavingsGoals", "MonthlyContribution"));
        Assert.Equal("numeric(18,2)", await ColumnType(db, "SavingsAllocations", "ReservedAmount"));
        Assert.Equal("numeric(18,2)", await ColumnType(db, "SavingsMovements", "Amount"));
        Assert.Equal("numeric(18,2)", await ColumnType(db, "ProjectionAssumptions", "Amount"));

        Assert.Contains("(\"WorkspaceId\", \"RequestId\")", await IndexDefinition(db, "SavingsCommands", "PK_SavingsCommands"));
        var nonReversalIndex = await IndexDefinition(db, "SavingsMovements", "IX_SavingsMovements_WorkspaceId_CommandId_AllocationId_Kind");
        Assert.Contains("CREATE UNIQUE INDEX", nonReversalIndex);
        Assert.Contains("(\"WorkspaceId\", \"CommandId\", \"AllocationId\", \"Kind\")", nonReversalIndex);
        Assert.Contains("WHERE", nonReversalIndex);
        Assert.Contains("'Reversal'", nonReversalIndex);
        Assert.Contains("(\"WorkspaceId\", \"ReversesMovementId\")", await IndexDefinition(db, "SavingsMovements", "IX_SavingsMovements_WorkspaceId_ReversesMovementId"));

        Assert.Contains("FK_SavingsGoals_Workspaces_WorkspaceId", await ConstraintNames(db, "SavingsGoals"));
        Assert.Contains("FK_SavingsAllocations_Workspaces_WorkspaceId", await ConstraintNames(db, "SavingsAllocations"));
        Assert.Contains("FK_SavingsMovements_Workspaces_WorkspaceId", await ConstraintNames(db, "SavingsMovements"));
        Assert.Contains("FK_SavingsCommands_Workspaces_WorkspaceId", await ConstraintNames(db, "SavingsCommands"));
        Assert.Contains("FK_ProjectionScenarios_Workspaces_WorkspaceId", await ConstraintNames(db, "ProjectionScenarios"));
        Assert.Contains("FK_ProjectionAssumptions_Workspaces_WorkspaceId", await ConstraintNames(db, "ProjectionAssumptions"));
        AssertCompositeForeignKey(await ForeignKeyDefinitions(db, "SavingsAllocations"), "WorkspaceId", "GoalId", "SavingsGoals", "WorkspaceId", "Id");
        AssertCompositeForeignKey(await ForeignKeyDefinitions(db, "SavingsAllocations"), "WorkspaceId", "AccountId", "Accounts", "WorkspaceId", "Id");
        AssertCompositeForeignKey(await ForeignKeyDefinitions(db, "SavingsMovements"), "WorkspaceId", "AllocationId", "SavingsAllocations", "WorkspaceId", "Id");
        AssertCompositeForeignKey(await ForeignKeyDefinitions(db, "SavingsMovements"), "WorkspaceId", "FinancePaymentId", "FinancePayments", "WorkspaceId", "Id");
        AssertCompositeForeignKey(await ForeignKeyDefinitions(db, "SavingsMovements"), "WorkspaceId", "CommandId", "SavingsCommands", "WorkspaceId", "RequestId");
        AssertCompositeForeignKey(await ForeignKeyDefinitions(db, "SavingsMovements"), "WorkspaceId", "ReversesMovementId", "SavingsMovements", "WorkspaceId", "Id");
        AssertCompositeForeignKey(await ForeignKeyDefinitions(db, "ProjectionAssumptions"), "WorkspaceId", "ScenarioId", "ProjectionScenarios", "WorkspaceId", "Id");
        Assert.Contains("FK_SavingsMovements_ApplicationUsers_ActorUserId", await ConstraintNames(db, "SavingsMovements"));
        Assert.Contains("FK_ProjectionScenarios_ApplicationUsers_ActorUserId", await ConstraintNames(db, "ProjectionScenarios"));

        Assert.Contains("Version", await ColumnNames(db, "SavingsMovements"));
        Assert.Contains("Version", await ColumnNames(db, "ProjectionScenarios"));
        Assert.Contains("ActorUserId", await ColumnNames(db, "SavingsMovements"));
        Assert.Contains("ActorUserId", await ColumnNames(db, "ProjectionScenarios"));
        Assert.Contains("(\"WorkspaceId\", \"Id\")", await IndexDefinition(db, "Accounts", "AK_Accounts_WorkspaceId_Id"));
        Assert.Contains("(\"WorkspaceId\", \"Id\")", await IndexDefinition(db, "FinancePayments", "AK_FinancePayments_WorkspaceId_Id"));
        Assert.Contains("(\"WorkspaceId\", \"Id\")", await IndexDefinition(db, "SavingsGoals", "AK_SavingsGoals_WorkspaceId_Id"));
        Assert.Contains("(\"WorkspaceId\", \"Id\")", await IndexDefinition(db, "SavingsAllocations", "AK_SavingsAllocations_WorkspaceId_Id"));
        Assert.Contains("(\"WorkspaceId\", \"Id\")", await IndexDefinition(db, "SavingsMovements", "AK_SavingsMovements_WorkspaceId_Id"));
        Assert.Contains("(\"WorkspaceId\", \"Id\")", await IndexDefinition(db, "ProjectionScenarios", "AK_ProjectionScenarios_WorkspaceId_Id"));

        await AssertCrossWorkspaceLinksRejected(db);
    }

    [Fact]
    public async Task Down_migration_removes_only_savings_objects_and_preserves_finance_rows()
    {
        using var factory = new ApiFactory(environment: "Testing");
        await using var db = Db(factory);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ApplicationUsers\" (\"Id\", \"Subject\", \"EmailVerified\", \"CreatedAt\") VALUES ({userId}, {$"savings-down-{userId:N}"}, {false}, {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Workspaces\" (\"Id\", \"OwnerSubject\", \"Name\", \"Kind\", \"OwnerUserId\", \"IsArchived\", \"Version\") VALUES ({workspaceId}, {$"savings-down-{userId:N}"}, {"Preservado"}, {"Personal"}, {userId}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"Currency\", \"OpeningBalance\", \"OpeningDate\", \"IsArchived\", \"Version\") VALUES ({accountId}, {workspaceId}, {"Cuenta"}, {"Banco"}, {"ARS"}, {123.45m}, {new DateOnly(2026, 1, 2)}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({operationId}, {workspaceId}, {"Expense"}, {accountId}, {new DateOnly(2026, 1, 3)}, {"Histórico"}, {"Hogar"}, {"ARS"}, {12.34m}, {false}, {1}, {true})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinancePayments\" (\"Id\", \"WorkspaceId\", \"OperationId\", \"AccountId\", \"Date\", \"Amount\", \"Currency\") VALUES ({paymentId}, {workspaceId}, {operationId}, {accountId}, {new DateOnly(2026, 1, 3)}, {12.34m}, {"ARS"})");

        await migrator.MigrateAsync();
        Assert.Equal(6, (await TableNames(db, "SavingsGoals", "SavingsAllocations", "SavingsMovements", "SavingsCommands", "ProjectionScenarios", "ProjectionAssumptions")).Length);
        await migrator.MigrateAsync(PreviousMigration);

        Assert.Equal(123.45m, await Scalar<decimal>(db, "SELECT \"OpeningBalance\" FROM \"Accounts\" WHERE \"Id\" = @id", accountId));
        Assert.Equal(12.34m, await Scalar<decimal>(db, "SELECT \"Amount\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", operationId));
        Assert.Equal(12.34m, await Scalar<decimal>(db, "SELECT \"Amount\" FROM \"FinancePayments\" WHERE \"Id\" = @id", paymentId));
        Assert.Empty(await TableNames(db, "SavingsGoals", "SavingsAllocations", "SavingsMovements", "SavingsCommands", "ProjectionScenarios", "ProjectionAssumptions"));
        Assert.Equal(1, await Scalar<int>(db, "SELECT count(*) FROM \"Accounts\" WHERE \"Id\" = @id", accountId));
    }

    private static async Task AssertCrossWorkspaceLinksRejected(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var goalA = Guid.NewGuid();
        var goalB = Guid.NewGuid();
        var commandA = Guid.NewGuid();
        var commandB = Guid.NewGuid();
        var allocationA = Guid.NewGuid();
        var allocationB = Guid.NewGuid();
        var paymentB = Guid.NewGuid();
        var scenarioA = Guid.NewGuid();
        var scenarioB = Guid.NewGuid();
        var movementA = Guid.NewGuid();
        var movementB = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ApplicationUsers\" (\"Id\", \"Subject\", \"EmailVerified\", \"CreatedAt\") VALUES ({userId}, {$"savings-fk-{userId:N}"}, {false}, {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Workspaces\" (\"Id\", \"OwnerSubject\", \"Name\", \"Kind\", \"OwnerUserId\", \"IsArchived\", \"Version\") VALUES ({workspaceA}, {$"savings-fk-{userId:N}"}, {"A"}, {"Personal"}, {userId}, {false}, {1}), ({workspaceB}, {null}, {"B"}, {"Household"}, {userId}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"Currency\", \"OpeningBalance\", \"OpeningDate\", \"IsArchived\", \"Version\") VALUES ({accountA}, {workspaceA}, {"A"}, {"Banco"}, {"ARS"}, {1m}, {new DateOnly(2026, 1, 1)}, {false}, {1}), ({accountB}, {workspaceB}, {"B"}, {"Banco"}, {"ARS"}, {1m}, {new DateOnly(2026, 1, 1)}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SavingsGoals\" (\"Id\", \"WorkspaceId\", \"Name\", \"TargetCurrency\", \"TargetAmount\", \"State\", \"Version\") VALUES ({goalA}, {workspaceA}, {"A"}, {"ARS"}, {1m}, {"Active"}, {1}), ({goalB}, {workspaceB}, {"B"}, {"ARS"}, {1m}, {"Active"}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SavingsCommands\" (\"WorkspaceId\", \"RequestId\", \"CommandKind\", \"PayloadHash\", \"ResultResourceId\", \"OccurredAt\") VALUES ({workspaceA}, {commandA}, {"test"}, {"hash-a"}, {goalA}, {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SavingsCommands\" (\"WorkspaceId\", \"RequestId\", \"CommandKind\", \"PayloadHash\", \"ResultResourceId\", \"OccurredAt\") VALUES ({workspaceB}, {commandB}, {"test"}, {"hash-b"}, {goalB}, {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SavingsAllocations\" (\"Id\", \"WorkspaceId\", \"GoalId\", \"AccountId\", \"Currency\", \"ReservedAmount\", \"Version\") VALUES ({allocationA}, {workspaceA}, {goalA}, {accountA}, {"ARS"}, {1m}, {1}), ({allocationB}, {workspaceB}, {goalB}, {accountB}, {"ARS"}, {1m}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({Guid.NewGuid()}, {workspaceB}, {"Expense"}, {accountB}, {new DateOnly(2026, 1, 1)}, {"B"}, {"B"}, {"ARS"}, {1m}, {false}, {1}, {true})");
        var operationB = await Scalar<Guid>(db, "SELECT \"Id\" FROM \"FinanceOperations\" WHERE \"WorkspaceId\" = @workspace", workspaceB);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinancePayments\" (\"Id\", \"WorkspaceId\", \"OperationId\", \"AccountId\", \"Date\", \"Amount\", \"Currency\") VALUES ({paymentB}, {workspaceB}, {operationB}, {accountB}, {new DateOnly(2026, 1, 1)}, {1m}, {"ARS"})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ProjectionScenarios\" (\"Id\", \"WorkspaceId\", \"Name\", \"ActorUserId\", \"IsDeleted\", \"Version\") VALUES ({scenarioA}, {workspaceA}, {"A"}, {userId}, {false}, {1}), ({scenarioB}, {workspaceB}, {"B"}, {userId}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"OccurredAt\", \"Version\") VALUES ({movementB}, {workspaceB}, {allocationB}, {commandB}, {"Allocate"}, {"ARS"}, {1m}, {userId}, {DateTimeOffset.UtcNow}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"OccurredAt\", \"Version\") VALUES ({movementA}, {workspaceA}, {allocationA}, {commandA}, {"Apply"}, {"ARS"}, {1m}, {userId}, {DateTimeOffset.UtcNow}, {1})");

        await AssertDbConstraintViolation(db, $"INSERT INTO \"SavingsAllocations\" (\"Id\", \"WorkspaceId\", \"GoalId\", \"AccountId\", \"Currency\", \"ReservedAmount\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {goalA}, {accountB}, 'ARS', 1, 1)");
        await AssertDbConstraintViolation(db, $"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"FinancePaymentId\", \"OccurredAt\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {allocationA}, {commandA}, 'Apply', 'ARS', 1, {userId}, {paymentB}, {DateTimeOffset.UtcNow}, 1)");
        await AssertDbConstraintViolation(db, $"INSERT INTO \"ProjectionAssumptions\" (\"Id\", \"WorkspaceId\", \"ScenarioId\", \"Kind\", \"Description\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {scenarioB}, 'Income', 'cross-workspace', 1)");
        await AssertDbConstraintViolation(db, $"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"ReversesMovementId\", \"OccurredAt\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {allocationA}, {commandA}, 'Reversal', 'ARS', 1, {userId}, {movementB}, {DateTimeOffset.UtcNow}, 1)");
        await AssertDbConstraintViolation(db, $"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"OccurredAt\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {allocationA}, {commandB}, 'Allocate', 'ARS', 1, {userId}, {DateTimeOffset.UtcNow}, 1)");
        await AssertDbConstraintViolation(db, $"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"OccurredAt\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {allocationB}, {commandA}, 'Allocate', 'ARS', 1, {userId}, {DateTimeOffset.UtcNow}, 1)");
        await AssertDbConstraintViolation(db, $"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"OccurredAt\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {allocationA}, {commandA}, 'Apply', 'ARS', 1, {userId}, {DateTimeOffset.UtcNow}, 1)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"ReversesMovementId\", \"OccurredAt\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {allocationA}, {commandA}, 'Reversal', 'ARS', 1, {userId}, {movementA}, {DateTimeOffset.UtcNow}, 1)");
        await AssertDbConstraintViolation(db, $"INSERT INTO \"SavingsMovements\" (\"Id\", \"WorkspaceId\", \"AllocationId\", \"CommandId\", \"Kind\", \"Currency\", \"Amount\", \"ActorUserId\", \"ReversesMovementId\", \"OccurredAt\", \"Version\") VALUES ({Guid.NewGuid()}, {workspaceA}, {allocationA}, {commandA}, 'Reversal', 'ARS', 1, {userId}, {movementA}, {DateTimeOffset.UtcNow}, 1)");
        await AssertDbConstraintViolation(db, $"UPDATE \"SavingsMovements\" SET \"Amount\" = 2 WHERE \"Id\" = {movementB}");
        await AssertDbConstraintViolation(db, $"DELETE FROM \"SavingsMovements\" WHERE \"Id\" = {movementB}");
    }

    private static async Task AssertDbConstraintViolation(AppDbContext db, FormattableString sql)
    {
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
    }

    private static async Task<string?> ColumnType(AppDbContext db, string table, string column) =>
        await Scalar<string?>(db, "SELECT data_type || CASE WHEN numeric_precision IS NOT NULL THEN '(' || numeric_precision || ',' || numeric_scale || ')' ELSE '' END FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column", table, column);

    private static async Task<string?> IndexDefinition(AppDbContext db, string table, string index) =>
        await Scalar<string?>(db, "SELECT pg_get_indexdef(i.indexrelid) FROM pg_index i JOIN pg_class t ON t.oid = i.indrelid JOIN pg_class ix ON ix.oid = i.indexrelid WHERE t.relname = @table AND ix.relname = @index AND t.relnamespace = current_schema()::regnamespace", table, index);

    private static async Task<string[]> ConstraintNames(AppDbContext db, string table) =>
        await Array(db, "SELECT conname FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid WHERE t.relname = @table AND t.relnamespace = current_schema()::regnamespace", table);

    private static async Task<string[]> ForeignKeyDefinitions(AppDbContext db, string table) =>
        await Array(db, "SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid WHERE c.contype = 'f' AND t.relname = @table AND t.relnamespace = current_schema()::regnamespace", table);

    private static void AssertCompositeForeignKey(string[] definitions, string localFirst, string localSecond, string principalTable, string principalFirst, string principalSecond)
    {
        var expected = $"FOREIGNKEY(\"{localFirst}\",\"{localSecond}\")REFERENCES\"{principalTable}\"(\"{principalFirst}\",\"{principalSecond}\")";
        Assert.Contains(definitions, definition => definition.Replace(" ", "", StringComparison.Ordinal).Contains(expected, StringComparison.Ordinal));
    }

    private static async Task<string[]> ColumnNames(AppDbContext db, string table) =>
        await Array(db, "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table", table);

    private static async Task<string[]> TableNames(AppDbContext db, params string[] tables) =>
        await Array(db, "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = ANY(@tables)", (object)tables);

    private static async Task<string[]> Array(AppDbContext db, string sql, params object[] values)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = Command(db, sql, values);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result.ToArray();
    }

    private static async Task<T> Scalar<T>(AppDbContext db, string sql, params object[] values)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = Command(db, sql, values);
        var result = await command.ExecuteScalarAsync();
        if (result is null) return default!;
        if (result is T typed) return typed;
        return (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static NpgsqlCommand Command(AppDbContext db, string sql, params object[] values)
    {
        var command = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var names = Regex.Matches(sql, "@[A-Za-z][A-Za-z0-9_]*").Select(match => match.Value[1..]).Distinct().ToArray();
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue(names[i], values[i]);
        return command;
    }

    private static AppDbContext Db(ApiFactory factory) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
}
