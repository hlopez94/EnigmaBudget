using System.Net;
using System.Net.Http.Json;

namespace EnigmaBudget.Api.Tests;

public sealed class CardCycleIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Future_card_opening_is_excluded_consistently_from_current_debt_views()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pastDate = today.AddDays(-2).ToString("yyyy-MM-dd");
        var todayDate = today.ToString("yyyy-MM-dd");
        var futureDate = today.AddDays(2).ToString("yyyy-MM-dd");
        var pastCard = Guid.NewGuid();
        var todayCard = Guid.NewGuid();
        var futureCard = Guid.NewGuid();
        var account = Guid.NewGuid();

        async Task CreateCard(Guid id, string name, string ars, string usd, string asOfDate)
        {
            var response = await client.PostAsJsonAsync(
                "/api/cards",
                new
                {
                    id,
                    name,
                    bankName = "Banco",
                    closingDay = 10,
                    dueDay = 20,
                    statementDebtArs = ars,
                    statementDebtUsd = usd,
                    asOfDate,
                }
            );
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await CreateCard(pastCard, "Pasada", "100.00", "0.00", pastDate);
        await CreateCard(todayCard, "Hoy", "40.00", "3.00", todayDate);
        await CreateCard(futureCard, "Futura", "150.00", "25.00", futureDate);

        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = account,
                        name = "Caja",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "1000.00",
                        openingDate = pastDate,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{pastCard}/payments",
                    new
                    {
                        id = Guid.NewGuid(),
                        accountId = account,
                        date = todayDate,
                        amount = "130.00",
                        currency = "ARS",
                        appliedAmount = "130.00",
                        statementId = (Guid?)null,
                    }
                )
            ).StatusCode
        );

        var cards = await client.GetFromJsonAsync<CardDebtSummary[]>("/api/cards");
        Assert.Equal("0.00", Assert.Single(cards!, x => x.Id == futureCard).DebtArs);
        Assert.Equal("0.00", Assert.Single(cards!, x => x.Id == futureCard).DebtUsd);
        Assert.Equal("40.00", Assert.Single(cards!, x => x.Id == todayCard).DebtArs);
        Assert.Equal("3.00", Assert.Single(cards!, x => x.Id == todayCard).DebtUsd);
        Assert.Equal("0.00", Assert.Single(cards!, x => x.Id == pastCard).DebtArs);
        Assert.Equal("30.00", Assert.Single(cards!, x => x.Id == pastCard).CreditArs);

        var period = today.ToString("yyyy-MM");
        using var finance = System.Text.Json.JsonDocument.Parse(
            await client.GetStringAsync($"/api/finance?period={period}")
        );
        var overview = finance.RootElement;
        var futureOverviewCard = overview.GetProperty("cards").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == futureCard);
        Assert.Equal("0.00", futureOverviewCard.GetProperty("debtArs").GetString());
        Assert.Equal("0.00", futureOverviewCard.GetProperty("debtUsd").GetString());
        Assert.Equal("40.00", overview.GetProperty("totals").GetProperty("cardDebtArs").GetString());
        Assert.Equal("3.00", overview.GetProperty("totals").GetProperty("cardDebtUsd").GetString());
        Assert.Equal("30.00", overview.GetProperty("totals").GetProperty("cardCreditArs").GetString());
    }

    [Fact]
    public async Task Statement_reconciles_once_and_partial_card_payments_reduce_pending_amount()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var card = Guid.NewGuid();
        var account = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/cards",
                    new
                    {
                        id = card,
                        name = "Visa",
                        bankName = "Banco",
                        closingDay = 10,
                        dueDay = 20,
                        statementDebtArs = "0.00",
                        statementDebtUsd = "0.00",
                        asOfDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = account,
                        name = "Caja",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "100000.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        var purchase = await client.PostAsJsonAsync(
            "/api/purchases",
            new
            {
                id = Guid.NewGuid(),
                cardId = card,
                date = "2026-10-02",
                description = "Notebook",
                category = "Trabajo",
                currency = "ARS",
                totalAmount = "120000.00",
                installmentCount = 3,
                firstPeriod = "2026-10",
                groupId = (Guid?)null,
            }
        );
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        var statement = await client.GetFromJsonAsync<Statement>(
            $"/api/cards/{card}/statement?period=2026-10&currency=ARS"
        );
        Assert.Equal("40000.00", statement!.ExpectedAmount);
        var save = await client.PutAsJsonAsync(
            $"/api/cards/{card}/statement",
            new
            {
                id = Guid.NewGuid(),
                period = "2026-10",
                currency = "ARS",
                version = 0,
                dueDate = "2026-10-20",
                actualTotal = "40100.00",
                adjustments = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        description = "Cargo confirmado",
                        category = "Interés",
                        amount = "100.00",
                    },
                },
            }
        );
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = await save.Content.ReadFromJsonAsync<SavedStatement>();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{card}/statement/confirm",
                    new { id = saved!.Id, version = saved.Version }
                )
            ).StatusCode
        );
        var november = await client.GetFromJsonAsync<Statement>(
            $"/api/cards/{card}/statement?period=2026-11&currency=ARS"
        );
        Assert.Equal("80100.00", november!.ExpectedAmount);
        var novemberSave = await client.PutAsJsonAsync(
            $"/api/cards/{card}/statement",
            new
            {
                id = Guid.NewGuid(),
                period = "2026-11",
                currency = "ARS",
                version = 0,
                dueDate = "2026-11-20",
                actualTotal = "80100.00",
                adjustments = Array.Empty<object>(),
            }
        );
        Assert.Equal(HttpStatusCode.OK, novemberSave.StatusCode);
        var novemberDraft = await novemberSave.Content.ReadFromJsonAsync<SavedStatement>();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{card}/statement/confirm",
                    new { id = novemberDraft!.Id, version = novemberDraft.Version }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{card}/payments",
                    new
                    {
                        id = Guid.NewGuid(),
                        accountId = account,
                        date = "2026-10-21",
                        amount = "50100.00",
                        currency = "ARS",
                        appliedAmount = "50100.00",
                        statementId = saved.Id,
                    }
                )
            ).StatusCode
        );
        var paid = await client.GetFromJsonAsync<Statement>(
            $"/api/cards/{card}/statement?period=2026-10&currency=ARS"
        );
        Assert.Equal("0.00", paid!.PendingAmount);
        Assert.Equal("10000.00", paid.CreditBalance);
        var next = await client.GetFromJsonAsync<Statement>(
            $"/api/cards/{card}/statement?period=2026-11&currency=ARS"
        );
        Assert.Equal("30000.00", next!.PendingAmount);
        var cardSummary = await client.GetFromJsonAsync<CardSummary[]>("/api/cards");
        var singleCardSummary = Assert.Single(cardSummary!);
        Assert.Equal("70000.00", singleCardSummary.DebtArs);
        Assert.Equal("0.00", singleCardSummary.CreditArs);
    }

    [Fact]
    public async Task Fx_card_payment_uses_bank_amount_and_applied_debt_amount_separately()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var card = Guid.NewGuid();
        var account = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/cards",
                    new
                    {
                        id = card,
                        name = "Visa",
                        bankName = "Banco",
                        closingDay = 10,
                        dueDay = 20,
                        statementDebtArs = "10.00",
                        statementDebtUsd = "0.00",
                        asOfDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = account,
                        name = "USD",
                        bankName = "Banco",
                        currency = "USD",
                        openingBalance = "50.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        var response = await client.PostAsJsonAsync(
            $"/api/cards/{card}/payments",
            new
            {
                id = Guid.NewGuid(),
                accountId = account,
                date = "2026-10-20",
                amount = "5.00",
                currency = "ARS",
                appliedAmount = "10.00",
                statementId = (Guid?)null,
            }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await client.GetFromJsonAsync<BankAccountBalance[]>("/api/accounts");
        Assert.Equal("45.00", accounts!.Single().CurrentBalance);
        var cards = await client.GetFromJsonAsync<CardSummary[]>("/api/cards");
        Assert.Equal("0.00", cards!.Single().DebtArs);
    }

    [Fact]
    public async Task Concurrent_card_payments_serialize_and_replayed_excess_is_visible_as_credit()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var card = Guid.NewGuid();
        var account = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/cards",
                    new
                    {
                        id = card,
                        name = "Visa",
                        bankName = "Banco",
                        closingDay = 10,
                        dueDay = 20,
                        statementDebtArs = "0.00",
                        statementDebtUsd = "0.00",
                        asOfDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = account,
                        name = "Caja",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "100000.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/purchases",
                    new
                    {
                        id = Guid.NewGuid(),
                        cardId = card,
                        date = "2026-10-02",
                        description = "Curso",
                        category = "Estudio",
                        currency = "ARS",
                        totalAmount = "40000.00",
                        installmentCount = 1,
                        firstPeriod = "2026-10",
                        groupId = (Guid?)null,
                    }
                )
            ).StatusCode
        );
        var draft = await client.PutAsJsonAsync(
            $"/api/cards/{card}/statement",
            new
            {
                id = Guid.NewGuid(),
                period = "2026-10",
                currency = "ARS",
                version = 0,
                dueDate = "2026-10-20",
                actualTotal = "40000.00",
                adjustments = Array.Empty<object>(),
            }
        );
        var statement = await draft.Content.ReadFromJsonAsync<SavedStatement>();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{card}/statement/confirm",
                    new { id = statement!.Id, version = statement.Version }
                )
            ).StatusCode
        );
        var request1 = new
        {
            id = Guid.NewGuid(),
            accountId = account,
            date = "2026-10-21",
            amount = "30000.00",
            currency = "ARS",
            appliedAmount = "30000.00",
            statementId = statement.Id,
        };
        var request2 = new
        {
            id = Guid.NewGuid(),
            accountId = account,
            date = "2026-10-21",
            amount = "30000.00",
            currency = "ARS",
            appliedAmount = "30000.00",
            statementId = statement.Id,
        };
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/cards/{card}/payments", request1),
            client.PostAsJsonAsync($"/api/cards/{card}/payments", request2)
        );
        Assert.All(
            responses,
            response =>
                Assert.Contains(
                    response.StatusCode,
                    new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }
                )
        );
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.OK);
        var payloadToReplay =
            responses[0].StatusCode == HttpStatusCode.Conflict ? request1 : request2;
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{card}/payments",
                    payloadToReplay
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{card}/payments",
                    new
                    {
                        id = payloadToReplay.id,
                        accountId = payloadToReplay.accountId,
                        date = "2026-10-22",
                        amount = payloadToReplay.amount,
                        currency = payloadToReplay.currency,
                        appliedAmount = payloadToReplay.appliedAmount,
                        statementId = payloadToReplay.statementId,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/cards/{card}/payments",
                    new
                    {
                        id = payloadToReplay.id,
                        accountId = payloadToReplay.accountId,
                        date = payloadToReplay.date,
                        amount = payloadToReplay.amount,
                        currency = payloadToReplay.currency,
                        appliedAmount = "29999.00",
                        statementId = payloadToReplay.statementId,
                    }
                )
            ).StatusCode
        );
        var summary = await client.GetFromJsonAsync<Statement>(
            $"/api/cards/{card}/statement?period=2026-10&currency=ARS"
        );
        Assert.Equal("0.00", summary!.PendingAmount);
        Assert.Equal("20000.00", summary.CreditBalance);
        var cards = await client.GetFromJsonAsync<CardSummary[]>("/api/cards");
        var singleCard = Assert.Single(cards!);
        Assert.Equal("0.00", singleCard.DebtArs);
        Assert.Equal("20000.00", singleCard.CreditArs);
        var accounts = await client.GetFromJsonAsync<BankAccountBalance[]>("/api/accounts");
        Assert.Equal("40000.00", accounts!.Single().CurrentBalance);
    }

    private sealed record Statement(
        string ExpectedAmount,
        string PendingAmount,
        string CreditBalance
    );

    private sealed record SavedStatement(Guid Id, int Version);

    private sealed record CardSummary(string DebtArs, string CreditArs);

    private sealed record CardDebtSummary(Guid Id, string DebtArs, string DebtUsd, string CreditArs);

    private sealed record BankAccountBalance(string CurrentBalance);
}
