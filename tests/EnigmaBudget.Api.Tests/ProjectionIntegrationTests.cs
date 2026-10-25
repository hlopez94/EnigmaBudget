using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class ProjectionIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset TestInstant = DateTimeOffset.Parse("2026-10-25T15:00:00Z");
    private readonly ApiFactory _factory = new(timeProvider: new FixedTimeProvider(TestInstant));

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task confirmed_projection_exposes_daily_cash_series_for_each_currency()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var through = Today().AddDays(2).ToString("yyyy-MM-dd");

        using var response = await client.GetAsync($"/api/projection?through={through}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("currencies", out var currencies));
        Assert.True(root.TryGetProperty("rows", out _));
        Assert.True(root.TryGetProperty("alerts", out _));
        Assert.All(currencies.EnumerateArray(), currency =>
        {
            Assert.True(currency.TryGetProperty("dailyBalances", out var dailyBalances));
            Assert.NotEmpty(dailyBalances.EnumerateArray());
            Assert.Contains("minimumFreeBalance", currency.EnumerateObject().Select(x => x.Name));
        });
    }

    [Fact]
    public async Task estimated_or_saved_scenario_is_not_reported_as_confirmed_projection()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var through = Today().AddDays(2).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await client.GetAsync($"/api/projection?through={through}&scenario=Estimated")).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await client.GetAsync($"/api/projection?through={through}&scenario=Confirmed&scenarioId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task opening_card_debt_in_both_currencies_keeps_distinct_projection_rows()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = Today();
        var cardId = Guid.NewGuid();
        var period = today.ToString("yyyy-MM");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cards", new
        {
            id = cardId, name = "Saldo ARS y USD", bankName = "Banco", closingDay = 10, dueDay = (int?)null,
            statementDebtArs = "200.00", statementDebtUsd = "50.00", asOfDate = today.AddDays(-1).ToString("yyyy-MM-dd"),
        })).StatusCode);

        async Task<Guid> ConfirmOpeningStatement(string currency, string total)
        {
            var statementId = Guid.NewGuid();
            var dueDate = today.AddDays(1);
            var response = await client.PutAsJsonAsync($"/api/cards/{cardId}/statement", new
            {
                id = statementId, period, currency, version = 0, dueDate = dueDate.ToString("yyyy-MM-dd"),
                actualTotal = total, adjustments = Array.Empty<object>(),
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var saved = await response.Content.ReadFromJsonAsync<SavedStatement>();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/cards/{cardId}/statement/confirm", new
            {
                id = saved!.Id, version = saved.Version,
            })).StatusCode);
            return statementId;
        }

        var arsStatement = await ConfirmOpeningStatement("ARS", "200.00");
        var usdStatement = await ConfirmOpeningStatement("USD", "50.00");
        var through = today.AddDays(2);
        using var response = await client.GetAsync($"/api/projection?through={through:yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("rows").EnumerateArray()
            .Where(x => x.GetProperty("sourceKind").GetString() == "CardObligation" && x.GetProperty("sourceId").GetGuid() == cardId).ToArray();
        Assert.Equal(2, rows.Length);
        var ars = Assert.Single(rows, x => x.GetProperty("currency").GetString() == "ARS");
        var usd = Assert.Single(rows, x => x.GetProperty("currency").GetString() == "USD");
        Assert.Equal("200.00", ars.GetProperty("amount").GetString());
        Assert.Equal("50.00", usd.GetProperty("amount").GetString());
        Assert.NotEqual(ars.GetProperty("componentId").GetString(), usd.GetProperty("componentId").GetString());
        Assert.Equal(arsStatement.ToString(), ars.GetProperty("summaryId").GetString());
        Assert.Equal(usdStatement.ToString(), usd.GetProperty("summaryId").GetString());
        var currencies = document.RootElement.GetProperty("currencies").EnumerateArray().ToArray();
        var arsSeries = Assert.Single(currencies, x => x.GetProperty("currency").GetString() == "ARS");
        var usdSeries = Assert.Single(currencies, x => x.GetProperty("currency").GetString() == "USD");
        Assert.Equal("-200.00", arsSeries.GetProperty("dailyBalances").EnumerateArray().Last().GetProperty("freeBalance").GetString());
        Assert.Equal("-50.00", usdSeries.GetProperty("dailyBalances").EnumerateArray().Last().GetProperty("freeBalance").GetString());
    }

    [Fact]
    public async Task future_statement_credit_does_not_reduce_older_cash_outflow_and_offsets_later_installment_once()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = Today();
        var cardId = Guid.NewGuid();
        var firstPeriod = today.ToString("yyyy-MM");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cards", new
        {
            id = cardId, name = "Crédito futuro", bankName = "Banco", closingDay = 10, dueDay = 20,
            statementDebtArs = "0.00", statementDebtUsd = "0.00", asOfDate = today.AddDays(-24).ToString("yyyy-MM-dd"),
        })).StatusCode);
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS",
            openingBalance = "500.00", openingDate = today.AddDays(-10).ToString("yyyy-MM-dd"),
        })).StatusCode);

        async Task<Guid> AddInstallment(string description, string period, string amount)
        {
            var response = await client.PostAsJsonAsync("/api/purchases", new
            {
                id = Guid.NewGuid(), cardId, date = today.ToString("yyyy-MM-dd"), description, category = "Varios",
                currency = "ARS", totalAmount = amount, installmentCount = 1, firstPeriod = period, groupId = (Guid?)null,
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var purchase = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return purchase.RootElement.GetProperty("installments")[0].GetProperty("id").GetGuid();
        }

        async Task ConfirmStatement(string period, string dueDate, string total, object[] adjustments)
        {
            var response = await client.PutAsJsonAsync($"/api/cards/{cardId}/statement", new
            {
                id = Guid.NewGuid(), period, currency = "ARS", version = 0, dueDate,
                actualTotal = total, adjustments,
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var saved = await response.Content.ReadFromJsonAsync<SavedStatement>();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/cards/{cardId}/statement/confirm", new
            {
                id = saved!.Id, version = saved.Version,
            })).StatusCode);
        }

        var octoberInstallment = await AddInstallment("Vencimiento de octubre", firstPeriod, "200.00");
        await ConfirmStatement(firstPeriod, "2026-10-20", "200.00", []);
        var novemberPeriod = DateOnly.ParseExact(firstPeriod + "-01", "yyyy-MM-dd").AddMonths(1).ToString("yyyy-MM");
        await ConfirmStatement(novemberPeriod, "2026-11-20", "150.00", [new
        {
            id = Guid.NewGuid(), description = "Crédito noviembre", category = "Ajuste", amount = "-50.00",
        }]);
        var decemberPeriod = DateOnly.ParseExact(firstPeriod + "-01", "yyyy-MM-dd").AddMonths(2).ToString("yyyy-MM");
        var decemberInstallment = await AddInstallment("Vencimiento de diciembre", decemberPeriod, "100.00");
        await ConfirmStatement(decemberPeriod, "2026-12-20", "250.00", []);

        using var response = await client.GetAsync("/api/projection?through=2026-12-21&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("rows").EnumerateArray()
            .Where(x => x.GetProperty("sourceKind").GetString() == "CardObligation").ToArray();
        Assert.Equal("200.00", Assert.Single(rows, x => x.GetProperty("sourceId").GetGuid() == octoberInstallment).GetProperty("amount").GetString());
        Assert.Equal("50.00", Assert.Single(rows, x => x.GetProperty("sourceId").GetGuid() == decemberInstallment).GetProperty("amount").GetString());
        Assert.DoesNotContain(rows, x => x.GetProperty("direction").GetString() == "Incoming");
        var cards = await client.GetFromJsonAsync<CardDebtSummary[]>("/api/cards");
        Assert.Equal("250.00", Assert.Single(cards!, x => x.Id == cardId).DebtArs);
        var ars = document.RootElement.GetProperty("currencies").EnumerateArray()
            .Single(x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("500.00", ars.GetProperty("cashBalance").GetString());
        var daily = ars.GetProperty("dailyBalances").EnumerateArray().ToDictionary(x => x.GetProperty("date").GetString()!);
        Assert.Equal("300.00", daily["2026-10-26"].GetProperty("freeBalance").GetString());
        Assert.Equal("300.00", daily["2026-11-21"].GetProperty("freeBalance").GetString());
        Assert.Equal("250.00", daily["2026-12-20"].GetProperty("freeBalance").GetString());
    }

    [Fact]
    public async Task funds_available_from_changes_free_cash_without_changing_cash_balance()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var availableFrom = Today().AddDays(7);
        var prior = Today().AddDays(-1);
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja retenida", bankName = "Banco", currency = "ARS",
            openingBalance = "1000.00", openingDate = prior.ToString("yyyy-MM-dd"),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/savings/accounts/{accountId}/liquidity", new
        {
            requestId = Guid.NewGuid(), version = 1, fundsAvailableFrom = availableFrom.ToString("yyyy-MM-dd"),
        })).StatusCode);

        using var response = await client.GetAsync($"/api/projection?through={availableFrom.AddDays(1):yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ars = document.RootElement.GetProperty("currencies").EnumerateArray().Single(x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("1000.00", ars.GetProperty("cashBalance").GetString());
        Assert.Equal("0.00", ars.GetProperty("availableCash").GetString());
        Assert.Equal("0.00", ars.GetProperty("freeBalance").GetString());
        var daily = ars.GetProperty("dailyBalances").EnumerateArray().ToDictionary(x => x.GetProperty("date").GetString()!);
        Assert.Equal("1000.00", daily[availableFrom.AddDays(-1).ToString("yyyy-MM-dd")].GetProperty("cashBalance").GetString());
        Assert.Equal("0.00", daily[availableFrom.AddDays(-1).ToString("yyyy-MM-dd")].GetProperty("availableCash").GetString());
        Assert.Equal("1000.00", daily[availableFrom.ToString("yyyy-MM-dd")].GetProperty("cashBalance").GetString());
        Assert.Equal("1000.00", daily[availableFrom.ToString("yyyy-MM-dd")].GetProperty("availableCash").GetString());
        var release = Assert.Single(document.RootElement.GetProperty("rows").EnumerateArray(),
            x => x.GetProperty("sourceKind").GetString() == "AccountLiquidity");
        Assert.Equal("Availability", release.GetProperty("direction").GetString());
    }

    [Fact]
    public async Task future_opening_balance_enters_cash_on_its_opening_date()
    {
        var accountId = Guid.NewGuid();
        var openingDate = Today().AddDays(7);
        using var client = await _factory.AuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Apertura futura", bankName = "Banco", currency = "ARS",
            openingBalance = "500.00", openingDate = openingDate.ToString("yyyy-MM-dd"),
        })).StatusCode);

        using var response = await client.GetAsync($"/api/projection?through={openingDate.AddDays(1):yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ars = document.RootElement.GetProperty("currencies").EnumerateArray().Single(x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("0.00", ars.GetProperty("cashBalance").GetString());
        var daily = ars.GetProperty("dailyBalances").EnumerateArray().ToDictionary(x => x.GetProperty("date").GetString()!);
        Assert.Equal("0.00", daily[openingDate.AddDays(-1).ToString("yyyy-MM-dd")].GetProperty("cashBalance").GetString());
        Assert.Equal("500.00", daily[openingDate.ToString("yyyy-MM-dd")].GetProperty("cashBalance").GetString());
        Assert.Contains(document.RootElement.GetProperty("rows").EnumerateArray(), x =>
            x.GetProperty("sourceKind").GetString() == "AccountOpening" && x.GetProperty("sourceId").GetGuid() == accountId);
    }

    [Fact]
    public async Task pending_expense_uses_remaining_amount_and_excludes_voided_operation()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = Today();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS",
            openingBalance = "1000.00", openingDate = today.AddDays(-10).ToString("yyyy-MM-dd"),
        })).StatusCode);
        var expenseId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = expenseId, kind = "Expense", accountId, date = today.ToString("yyyy-MM-dd"),
            description = "Servicio", category = "Hogar", amount = "200.00", paid = false,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{expenseId}/payments", new
        {
            id = Guid.NewGuid(), accountId, date = today.ToString("yyyy-MM-dd"), amount = "75.00",
        })).StatusCode);
        var voidedId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = voidedId, kind = "Expense", accountId, date = today.ToString("yyyy-MM-dd"),
            description = "Anulado", category = "Hogar", amount = "50.00", paid = false,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{voidedId}/void", new
        {
            id = Guid.NewGuid(), version = 1, reason = "Registro duplicado",
        })).StatusCode);

        using var response = await client.GetAsync($"/api/projection?through={today.AddDays(1):yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("rows").EnumerateArray()
            .Where(x => x.GetProperty("sourceKind").GetString() == "FinanceOperation").ToArray();
        var pending = Assert.Single(rows);
        Assert.Equal(expenseId, pending.GetProperty("sourceId").GetGuid());
        Assert.Equal("125.00", pending.GetProperty("amount").GetString());
        Assert.Equal("Outgoing", pending.GetProperty("direction").GetString());
    }

    [Fact]
    public async Task overdue_pending_operation_remains_visible_with_original_date_and_warning()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = Today();
        var accountId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS",
            openingBalance = "500.00", openingDate = today.AddDays(-20).ToString("yyyy-MM-dd"),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = expenseId, kind = "Expense", accountId, date = today.AddDays(-5).ToString("yyyy-MM-dd"),
            description = "Vencido", category = "Hogar", amount = "80.00", paid = false,
        })).StatusCode);

        using var response = await client.GetAsync($"/api/projection?through={today.AddDays(1):yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(document.RootElement.GetProperty("rows").EnumerateArray(), x =>
            x.GetProperty("sourceId").GetGuid() == expenseId);
        Assert.Equal(today.AddDays(-5).ToString("yyyy-MM-dd"), row.GetProperty("date").GetString());
        Assert.Equal("Overdue", row.GetProperty("dateCertainty").GetString());
        Assert.NotEmpty(row.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task positive_final_cash_does_not_hide_intermediate_shortfall()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = Today();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS",
            openingBalance = "1000.00", openingDate = today.AddDays(-10).ToString("yyyy-MM-dd"),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = Guid.NewGuid(), kind = "Expense", accountId, date = today.AddDays(1).ToString("yyyy-MM-dd"),
            description = "Vencimiento", category = "Hogar", amount = "1200.00", paid = false,
        })).StatusCode);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options))
        {
            var account = await db.Accounts.SingleAsync(x => x.Id == accountId);
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(), WorkspaceId = account.WorkspaceId, AccountId = accountId,
                Date = today.AddDays(2), Currency = "ARS", Amount = 500m, Source = "future-ledger",
                SourceId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync($"/api/projection?through={today.AddDays(3):yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ars = document.RootElement.GetProperty("currencies").EnumerateArray().Single(x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("1000.00", ars.GetProperty("cashBalance").GetString());
        Assert.Equal("-200.00", ars.GetProperty("minimumFreeBalance").GetString());
        Assert.Equal(today.AddDays(1).ToString("yyyy-MM-dd"), ars.GetProperty("firstShortfallDate").GetString());
        var daily = ars.GetProperty("dailyBalances").EnumerateArray().ToArray();
        Assert.True(decimal.Parse(daily[^1].GetProperty("freeBalance").GetString()!) > 0m);
    }

    [Fact]
    public async Task opening_card_debt_without_due_day_is_undated_visible_and_warned()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = Today();
        var cardId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cards", new
        {
            id = cardId, name = "Sin vencimiento", bankName = "Banco", closingDay = (int?)null, dueDay = (int?)null,
            statementDebtArs = "100.00", statementDebtUsd = "0.00", asOfDate = today.AddDays(-1).ToString("yyyy-MM-dd"),
        })).StatusCode);

        using var response = await client.GetAsync($"/api/projection?through={today.AddDays(2):yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(document.RootElement.GetProperty("rows").EnumerateArray(), x =>
            x.GetProperty("sourceKind").GetString() == "CardObligation");
        Assert.Null(row.GetProperty("date").GetString());
        Assert.Contains(row.GetProperty("warnings").EnumerateArray(), x => x.GetString()!.Contains("fecha de vencimiento"));
        Assert.Contains(document.RootElement.GetProperty("alerts").EnumerateArray(), x =>
            x.GetProperty("code").GetString() == "UnknownDueDate");
    }

    [Fact]
    public async Task archived_restricted_account_reservation_stays_visible_and_is_subtracted_once()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = Today();
        var accountId = Guid.NewGuid();
        var goalId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS",
            openingBalance = "1000.00", openingDate = today.AddDays(-10).ToString("yyyy-MM-dd"),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/goals", new
        {
            id = goalId, name = "Viaje", targetCurrency = "ARS", targetAmount = "2000.00",
            desiredDate = (string?)null, monthlyContribution = "100.00", nextContributionDate = today.AddDays(10).ToString("yyyy-MM-dd"),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/allocations", new
        {
            id = Guid.NewGuid(), goalId, accountId, amount = "600.00",
        })).StatusCode);
        var availableFrom = today.AddDays(2);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/savings/accounts/{accountId}/liquidity", new
        {
            requestId = Guid.NewGuid(), version = 1, fundsAvailableFrom = availableFrom.ToString("yyyy-MM-dd"),
        })).StatusCode);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options))
        {
            var account = await db.Accounts.SingleAsync(x => x.Id == accountId);
            account.IsArchived = true;
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync($"/api/projection?through={availableFrom.AddDays(2):yyyy-MM-dd}&scenario=Confirmed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ars = document.RootElement.GetProperty("currencies").EnumerateArray().Single(x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("600.00", ars.GetProperty("reservedNominal").GetString());
        Assert.Equal("-600.00", ars.GetProperty("freeBalance").GetString());
        var daily = ars.GetProperty("dailyBalances").EnumerateArray().ToDictionary(x => x.GetProperty("date").GetString()!);
        Assert.Equal("-600.00", daily[availableFrom.AddDays(-1).ToString("yyyy-MM-dd")].GetProperty("freeBalance").GetString());
        Assert.Equal("400.00", daily[availableFrom.ToString("yyyy-MM-dd")].GetProperty("freeBalance").GetString());
        Assert.Equal("400.00", daily[availableFrom.AddDays(1).ToString("yyyy-MM-dd")].GetProperty("freeBalance").GetString());
        Assert.Contains(document.RootElement.GetProperty("rows").EnumerateArray(), x =>
            x.GetProperty("sourceKind").GetString() == "AccountLiquidity" && x.GetProperty("accountId").GetGuid() == accountId);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(TestInstant,
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires")).DateTime);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record SavedStatement(Guid Id, int Version);
    private sealed record CardDebtSummary(Guid Id, string DebtArs, string DebtUsd, string CreditArs);
}
