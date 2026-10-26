using EnigmaBudget.Api.Data;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EnigmaBudget.Api.Tests;

public sealed class WorkspaceMigrationTests
{
    [Fact]
    public async Task Upgrade_backfills_owners_without_changing_legacy_financial_ids_or_values()
    {
        using var factory = new ApiFactory(environment: "Testing");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(factory.TestConnection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261024234249_FinanceAtomicityIdempotency");

        var workspaceA = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var workspaceB = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var accountA = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var cardA = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var opA = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var ledgerA = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var openingA = Guid.Parse("60000000-0000-0000-0000-000000000001");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Workspaces\" (\"Id\", \"OwnerSubject\") VALUES ({workspaceA}, {"legacy-owner-a"}), ({workspaceB}, {"legacy-owner-b"})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"Currency\", \"OpeningBalance\", \"OpeningDate\", \"IsArchived\", \"Version\") VALUES ({accountA}, {workspaceA}, {"Cuenta histórica"}, {"Banco"}, {"ARS"}, {123456.78m}, {new DateOnly(2024, 2, 3)}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OpeningBalances\" (\"Id\", \"WorkspaceId\", \"AccountId\", \"Currency\", \"Amount\", \"EffectiveDate\") VALUES ({openingA}, {workspaceA}, {accountA}, {"ARS"}, {123456.78m}, {new DateOnly(2024, 2, 3)})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Cards\" (\"Id\", \"WorkspaceId\", \"Name\", \"BankName\", \"ClosingDay\", \"DueDay\", \"StatementDebtArs\", \"StatementDebtUsd\", \"AsOfDate\", \"IsArchived\", \"Version\") VALUES ({cardA}, {workspaceA}, {"Tarjeta histórica"}, {"Banco"}, {12}, {25}, {9876.54m}, {42.35m}, {new DateOnly(2024, 2, 29)}, {false}, {1})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"FinanceOperations\" (\"Id\", \"WorkspaceId\", \"Kind\", \"AccountId\", \"Date\", \"Description\", \"Category\", \"Currency\", \"Amount\", \"IsVoided\", \"Version\", \"InitiallyPaid\") VALUES ({opA}, {workspaceA}, {"Expense"}, {accountA}, {new DateOnly(2024, 2, 4)}, {"Gasto histórico"}, {"Hogar"}, {"ARS"}, {321.09m}, {false}, {1}, {true})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"LedgerEntries\" (\"Id\", \"WorkspaceId\", \"AccountId\", \"Date\", \"Currency\", \"Amount\", \"Source\", \"SourceId\", \"ObservedBalance\", \"Reason\") VALUES ({ledgerA}, {workspaceA}, {accountA}, {new DateOnly(2024, 2, 4)}, {"ARS"}, {-321.09m}, {"operation"}, {opA}, {123135.69m}, {null})");

        await migrator.MigrateAsync();

        var account = await ReadSingleAsync(db, "SELECT \"Id\", \"WorkspaceId\", \"OpeningBalance\", \"OpeningDate\" FROM \"Accounts\" WHERE \"Id\" = @id", accountA, reader => (reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2), DateOnly.FromDateTime(reader.GetDateTime(3))));
        Assert.Equal((accountA, workspaceA, 123456.78m, new DateOnly(2024, 2, 3)), account);

        var card = await ReadSingleAsync(db, "SELECT \"Id\", \"StatementDebtArs\", \"StatementDebtUsd\", \"AsOfDate\" FROM \"Cards\" WHERE \"Id\" = @id", cardA, reader => (reader.GetGuid(0), reader.GetDecimal(1), reader.GetDecimal(2), DateOnly.FromDateTime(reader.GetDateTime(3))));
        Assert.Equal((cardA, 9876.54m, 42.35m, new DateOnly(2024, 2, 29)), card);

        var history = await ReadSingleAsync(db, "SELECT \"Id\", \"Amount\", \"Date\" FROM \"FinanceOperations\" WHERE \"Id\" = @id", opA, reader => (reader.GetGuid(0), reader.GetDecimal(1), DateOnly.FromDateTime(reader.GetDateTime(2))));
        Assert.Equal((opA, 321.09m, new DateOnly(2024, 2, 4)), history);
        Assert.Equal(2, await db.Workspaces.CountAsync(x => x.Kind == "Personal"));
        Assert.Equal(2, await db.WorkspaceMemberships.CountAsync(x => x.Role == "Owner" && x.Active));
        var generations = await db.WorkspaceMemberships.Where(x => x.Active).Select(x => x.ActivationGeneration).ToArrayAsync();
        Assert.All(generations, generation => Assert.NotEqual(Guid.Empty, generation));
        Assert.Equal(generations.Length, generations.Distinct().Count());
        Assert.Empty(await db.PersonalReadGrants.ToArrayAsync());
        Assert.Empty(await db.WorkspaceInvitations.ToArrayAsync());
        Assert.Equal(2, await db.ApplicationUsers.CountAsync());
        Assert.Equal(workspaceA, await db.Workspaces.Where(x => x.OwnerSubject == "legacy-owner-a").Select(x => x.Id).SingleAsync());
        Assert.Equal(workspaceB, await db.Workspaces.Where(x => x.OwnerSubject == "legacy-owner-b").Select(x => x.Id).SingleAsync());
        Assert.Equal(openingA, await db.OpeningBalances.Where(x => x.AccountId == accountA).Select(x => x.Id).SingleAsync());
        Assert.Equal(123456.78m, await db.OpeningBalances.Where(x => x.AccountId == accountA).Select(x => x.Amount).SingleAsync());
        Assert.Equal(ledgerA, await db.LedgerEntries.Where(x => x.Id == ledgerA).Select(x => x.Id).SingleAsync());
        Assert.Equal(-321.09m, await db.LedgerEntries.Where(x => x.Id == ledgerA).Select(x => x.Amount).SingleAsync());

        using var first = factory.IdentityClient("legacy-owner-a");
        using var second = factory.IdentityClient("legacy-owner-b");
        await first.GetAsync("/api/auth/session");
        await second.GetAsync("/api/auth/session");
        var firstAccounts = await first.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        var secondAccounts = await second.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.Equal(accountA, Assert.Single(firstAccounts!).Id);
        Assert.Empty(secondAccounts!);
        first.DefaultRequestHeaders.Add("X-Workspace-Id", workspaceB.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync("/api/accounts")).StatusCode);
    }

    private static async Task<T> ReadSingleAsync<T>(AppDbContext db, string sql, Guid id, Func<NpgsqlDataReader, T> read)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow);
        Assert.True(await reader.ReadAsync());
        return read(reader);
    }

    private sealed record AccountDto(Guid Id);
}
