using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class BusinessClockIntegrationTests
{
    private static readonly DateOnly CrossingDate = new(2026, 11, 1);

    [Fact]
    public async Task Before_Argentina_midnight_rejects_future_effects_and_defaults_group_to_October()
    {
        using var factory = Factory("2026-11-01T01:30:00Z");
        using var client = await factory.AuthenticatedClientAsync("clock-before");
        var account = Guid.NewGuid();
        var card = Guid.NewGuid();
        var group = Guid.NewGuid();
        var self = Guid.NewGuid();
        var guest = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await CreateAccount(client, account, "2026-10-01", "100.00")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateCard(client, card, "2026-10-01", "100.00")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/groups", new
        {
            id = group, name = "Grupo", participants = new[] { new { id = self, name = "Yo", isSelf = true }, new { id = guest, name = "Invitado", isSelf = false } }
        })).StatusCode);
        await SetRule(client, group, "2026-10", self, guest);
        await SetRule(client, group, "2026-11", self, guest);

        var pending = await client.PostAsJsonAsync("/api/operations", new
        {
            id = Guid.NewGuid(), kind = "Expense", accountId = account, date = "2026-11-01", description = "Pendiente", category = "", amount = "10.00", paid = false
        });
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingId = (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var paid = await client.PostAsJsonAsync("/api/operations", new
        {
            id = Guid.NewGuid(), kind = "Income", accountId = account, date = "2026-11-01", description = "Cobro", category = "", amount = "10.00", paid = true
        });
        var partialPayment = await client.PostAsJsonAsync($"/api/operations/{pendingId}/payments", new
        {
            id = Guid.NewGuid(), accountId = account, date = "2026-11-01", amount = "1.00", operationVersion = 1
        });
        var transfer = await client.PostAsJsonAsync("/api/transfers", new
        {
            id = Guid.NewGuid(), fromAccountId = account, toAccountId = Guid.NewGuid(), date = "2026-11-01", amount = "1.00", destinationAmount = "1.00"
        });
        var adjustment = await client.PostAsJsonAsync($"/api/accounts/{account}/adjustments", new
        {
            id = Guid.NewGuid(), date = "2026-11-01", observedBalance = "100.00", reason = "Conciliación"
        });
        var cardPayment = await client.PostAsJsonAsync($"/api/cards/{card}/payments", new
        {
            id = Guid.NewGuid(), accountId = account, date = "2026-11-01", amount = "1.00", currency = "ARS", appliedAmount = "1.00", statementId = (Guid?)null
        });
        var collection = await client.PostAsJsonAsync($"/api/groups/{group}/collections", new
        {
            id = Guid.NewGuid(), installmentId = Guid.NewGuid(), participantId = guest, accountId = account, date = "2026-11-01", amount = "1.00", appliedAmount = "1.00", ruleVersion = 1
        });

        Assert.Equal(HttpStatusCode.BadRequest, paid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, partialPayment.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, transfer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, adjustment.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cardPayment.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, collection.StatusCode);
        var groups = await client.GetFromJsonAsync<JsonElement[]>("/api/groups");
        Assert.Equal("2026-10", groups!.Single().GetProperty("rule").GetProperty("period").GetString());
        Assert.Equal("100.00", (await GetAccounts(client)).Single(x => x.Id == account).CurrentBalance);
        await using var db = NewDb(factory.TestConnection);
        Assert.Empty(await db.LedgerEntries.Where(x => x.Amount != 0).ToArrayAsync());
        Assert.Empty(await db.FinancePayments.ToArrayAsync());
        Assert.Empty(await db.Transfers.ToArrayAsync());
        Assert.Empty(await db.CardPayments.ToArrayAsync());
        Assert.Empty(await db.Collections.ToArrayAsync());
    }

    [Theory]
    [InlineData("2026-11-01T01:30:00Z", "0.00", "30.00", "2026-10", 400)]
    [InlineData("2026-11-01T03:30:00Z", "515.00", "120.00", "2026-11", 200)]
    public async Task Argentina_day_controls_openings_and_ledger_cutoff_while_future_installments_remain_committed(
        string utcNow, string expectedAccountBalance, string expectedCardDebt, string expectedDefaultPeriod, int expectedPaymentStatus)
    {
        using var factory = Factory(utcNow);
        using var client = await factory.AuthenticatedClientAsync("clock-balance");
        var account = Guid.NewGuid();
        var card = Guid.NewGuid();
        var group = Guid.NewGuid();
        var self = Guid.NewGuid();
        var guest = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await CreateAccount(client, account, "2026-11-01", "500.00")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateCard(client, card, "2026-11-01", "100.00")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/groups", new
        {
            id = group, name = "Grupo", participants = new[] { new { id = self, name = "Yo", isSelf = true }, new { id = guest, name = "Invitado", isSelf = false } }
        })).StatusCode);
        await SetRule(client, group, "2026-10", self, guest);
        await SetRule(client, group, "2026-11", self, guest);

        await using (var db = NewDb(factory.TestConnection))
        {
            var workspace = await db.Workspaces.SingleAsync(x => x.OwnerSubject == "clock-balance");
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(), WorkspaceId = workspace.Id, AccountId = account,
                Date = CrossingDate, Currency = "ARS", Amount = 25m, Source = "legacy-test", SourceId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }
        var purchase = await client.PostAsJsonAsync("/api/purchases", new
        {
            id = Guid.NewGuid(), cardId = card, date = "2026-11-01", description = "Cuota comprometida", category = "", currency = "ARS", totalAmount = "30.00", installmentCount = 1, firstPeriod = "2026-11", groupId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        var cardPayment = await client.PostAsJsonAsync($"/api/cards/{card}/payments", new
        {
            id = Guid.NewGuid(), accountId = account, date = "2026-11-01", amount = "10.00", currency = "ARS", appliedAmount = "10.00", statementId = (Guid?)null
        });
        Assert.Equal((HttpStatusCode)expectedPaymentStatus, cardPayment.StatusCode);

        var accounts = await GetAccounts(client);
        Assert.Equal(expectedAccountBalance, accounts.Single(x => x.Id == account).CurrentBalance);
        var cards = await client.GetFromJsonAsync<JsonElement[]>("/api/cards");
        Assert.Equal(expectedCardDebt, cards!.Single(x => x.GetProperty("id").GetGuid() == card).GetProperty("debtArs").GetString());
        using var overview = JsonDocument.Parse(await client.GetStringAsync("/api/finance?period=2026-11"));
        var overviewAccount = overview.RootElement.GetProperty("accounts").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == account);
        Assert.Equal(expectedAccountBalance, overviewAccount.GetProperty("currentBalance").GetString());
        Assert.Equal(expectedCardDebt, overview.RootElement.GetProperty("cards").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == card).GetProperty("debtArs").GetString());
        var groups = await client.GetFromJsonAsync<JsonElement[]>("/api/groups");
        Assert.Equal(expectedDefaultPeriod, groups!.Single().GetProperty("rule").GetProperty("period").GetString());
        using var currentOverview = JsonDocument.Parse(await client.GetStringAsync("/api/finance"));
        Assert.Equal(expectedDefaultPeriod, currentOverview.RootElement.GetProperty("period").GetString());
        Assert.Equal(expectedAccountBalance, currentOverview.RootElement.GetProperty("totals").GetProperty("balanceArs").GetString());
    }

    private static ApiFactory Factory(string instant) => new(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse(instant)));
    private static AppDbContext NewDb(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    private static Task<HttpResponseMessage> CreateAccount(HttpClient client, Guid id, string date, string balance) => client.PostAsJsonAsync("/api/accounts", new
    {
        id, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = balance, openingDate = date
    });
    private static Task<HttpResponseMessage> CreateCard(HttpClient client, Guid id, string date, string debt) => client.PostAsJsonAsync("/api/cards", new
    {
        id, name = "Tarjeta", bankName = "Banco", closingDay = 10, dueDay = 20, statementDebtArs = debt, statementDebtUsd = "0.00", asOfDate = date
    });
    private static Task<AccountSnapshot[]> GetAccounts(HttpClient client) => client.GetFromJsonAsync<AccountSnapshot[]>("/api/accounts")!;
    private static async Task SetRule(HttpClient client, Guid group, string period, Guid self, Guid guest)
    {
        var result = await client.PutAsJsonAsync($"/api/groups/{group}/rules/{period}", new
        {
            version = 0, mode = "Equal", basisCurrency = "ARS", weights = new[] { new { participantId = self, value = "1.00" }, new { participantId = guest, value = "1.00" } }
        });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
    private sealed record AccountSnapshot(Guid Id, string CurrentBalance);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
