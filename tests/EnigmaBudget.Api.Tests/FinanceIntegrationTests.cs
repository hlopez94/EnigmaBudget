using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EnigmaBudget.Api.Tests;

public sealed class FinanceIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory;

    public FinanceIntegrationTests() => _factory = new ApiFactory();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Accounts_created_after_empty_onboarding_accept_income_expense_and_transfer_once()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/onboarding")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = accountA,
                        name = "Caja",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "100.00",
                        openingDate = "2026-10-01",
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
                        id = accountB,
                        name = "Ahorro",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "0.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );

        var incomeId = Guid.NewGuid();
        var income = new
        {
            id = incomeId,
            kind = "Income",
            accountId = accountA,
            date = "2026-10-10",
            description = "Sueldo",
            category = "Trabajo",
            amount = "50.00",
            paid = true,
        };
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/operations", income)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/operations", income)).StatusCode
        );
        var expense = await client.PostAsJsonAsync(
            "/api/operations",
            new
            {
                id = Guid.NewGuid(),
                kind = "Expense",
                accountId = accountA,
                date = "2026-10-11",
                description = "Compra",
                category = "Comida",
                amount = "10.00",
                paid = false,
            }
        );
        Assert.Equal(HttpStatusCode.OK, expense.StatusCode);
        var expenseId = (await expense.Content.ReadFromJsonAsync<Operation>())!.Id;
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/operations/{expenseId}/payments",
                    new
                    {
                        id = Guid.NewGuid(),
                        accountId = accountA,
                        date = "2026-10-12",
                        amount = "4.00",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/transfers",
                    new
                    {
                        id = Guid.NewGuid(),
                        fromAccountId = accountA,
                        toAccountId = accountB,
                        date = "2026-10-13",
                        amount = "20.00",
                        destinationAmount = "20.00",
                    }
                )
            ).StatusCode
        );

        var accounts = await client.GetFromJsonAsync<Account[]>("/api/accounts");
        Assert.Equal("126.00", accounts!.Single(x => x.Id == accountA).CurrentBalance);
        Assert.Equal("20.00", accounts!.Single(x => x.Id == accountB).CurrentBalance);
        var opResponse = await client.GetAsync("/api/operations?period=2026-10");
        Assert.True(opResponse.IsSuccessStatusCode, await opResponse.Content.ReadAsStringAsync());
        var operation = (await opResponse.Content.ReadFromJsonAsync<Operation[]>())!.Single(x =>
            x.Id == expenseId
        );
        Assert.Equal("4.00", operation.PaidAmount);
        Assert.Equal("6.00", operation.PendingAmount);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/finance?period=2026-10")).StatusCode
        );
    }

    [Fact]
    public async Task Other_workspace_cannot_read_or_mutate_account()
    {
        using var owner = await _factory.AuthenticatedClientAsync("finance-owner");
        using var stranger = await _factory.AuthenticatedClientAsync("finance-stranger");
        var accountId = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await owner.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = accountId,
                        name = "Caja",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "0.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        var response = await stranger.PutAsJsonAsync(
            $"/api/accounts/{accountId}",
            new
            {
                version = 1,
                name = "Ajena",
                bankName = "Banco",
                isArchived = false,
            }
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Idempotent_creates_and_balance_adjustments_conflict_when_effect_payload_changes()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = accountId,
                        name = "Caja",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "10.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = accountId,
                        name = "Caja",
                        bankName = "Banco",
                        currency = "ARS",
                        openingBalance = "11.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        var cardId = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/cards",
                    new
                    {
                        id = cardId,
                        name = "Visa",
                        bankName = "Banco",
                        closingDay = 10,
                        dueDay = 20,
                        statementDebtArs = "2.00",
                        statementDebtUsd = "0.00",
                        asOfDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    "/api/cards",
                    new
                    {
                        id = cardId,
                        name = "Visa",
                        bankName = "Banco",
                        closingDay = 10,
                        dueDay = 20,
                        statementDebtArs = "3.00",
                        statementDebtUsd = "0.00",
                        asOfDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        var adjustmentId = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/accounts/{accountId}/adjustments",
                    new
                    {
                        id = adjustmentId,
                        date = "2026-10-20",
                        observedBalance = "12.00",
                        reason = "Recuento",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/accounts/{accountId}/adjustments",
                    new
                    {
                        id = adjustmentId,
                        date = "2026-10-21",
                        observedBalance = "12.00",
                        reason = "Recuento cambiado",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/accounts/{accountId}/adjustments",
                    new
                    {
                        id = adjustmentId,
                        date = "2026-10-20",
                        observedBalance = "13.00",
                        reason = "Recuento",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/accounts/{accountId}/adjustments",
                    new
                    {
                        id = adjustmentId,
                        date = "2026-10-20",
                        observedBalance = "12.00",
                        reason = "Otro motivo",
                    }
                )
            ).StatusCode
        );
        var opId = Guid.NewGuid();
        var operation = new
        {
            id = opId,
            kind = "Expense",
            accountId,
            date = "2026-10-20",
            description = "Compra",
            category = "Hogar",
            amount = "1.00",
            paid = false,
        };
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/operations", operation)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    "/api/operations",
                    new
                    {
                        id = opId,
                        kind = "Expense",
                        accountId,
                        date = "2026-10-20",
                        description = "Compra",
                        category = "Hogar",
                        amount = "1.00",
                        paid = true,
                    }
                )
            ).StatusCode
        );
        var groupId = Guid.NewGuid();
        var self = Guid.NewGuid();
        var guest = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/groups",
                    new
                    {
                        id = groupId,
                        name = "Viaje",
                        participants = new[]
                        {
                            new
                            {
                                id = self,
                                name = "Yo",
                                isSelf = true,
                            },
                            new
                            {
                                id = guest,
                                name = "Ana",
                                isSelf = false,
                            },
                        },
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    "/api/groups",
                    new
                    {
                        id = groupId,
                        name = "Viaje",
                        participants = new[]
                        {
                            new
                            {
                                id = self,
                                name = "Yo",
                                isSelf = true,
                            },
                            new
                            {
                                id = guest,
                                name = "Otra persona",
                                isSelf = false,
                            },
                        },
                    }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Voiding_an_expense_reverses_balance_and_removes_it_from_budget_and_reason_is_idempotent()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var account = Guid.NewGuid();
        var operationId = Guid.NewGuid();
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
                        openingBalance = "100.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/operations",
                    new
                    {
                        id = operationId,
                        kind = "Expense",
                        accountId = account,
                        date = "2026-10-10",
                        description = "Compra anulada",
                        category = "Hogar",
                        amount = "12.00",
                        paid = true,
                    }
                )
            ).StatusCode
        );
        var voidId = Guid.NewGuid();
        var request = new
        {
            id = voidId,
            version = 1,
            reason = "Carga duplicada",
        };
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync($"/api/operations/{operationId}/void", request)
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/operations/{operationId}/void",
                    new
                    {
                        id = voidId,
                        version = 1,
                        reason = "Otro motivo",
                    }
                )
            ).StatusCode
        );
        var accounts = await client.GetFromJsonAsync<Account[]>("/api/accounts");
        Assert.Equal("100.00", accounts!.Single().CurrentBalance);
        var overview = await client.GetFromJsonAsync<JsonElement>("/api/finance?period=2026-10");
        Assert.Equal(
            "0.00",
            overview.GetProperty("budget").GetProperty("cashExpensesArs").GetString()
        );
    }

    [Fact]
    public async Task Concurrent_partial_operation_payments_cannot_exceed_the_obligation()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var account = Guid.NewGuid();
        var op = Guid.NewGuid();
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
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/operations",
                    new
                    {
                        id = op,
                        kind = "Expense",
                        accountId = account,
                        date = "2026-10-10",
                        description = "Deuda",
                        category = "Hogar",
                        amount = "100.00",
                        paid = false,
                    }
                )
            ).StatusCode
        );
        var first = client.PostAsJsonAsync(
            $"/api/operations/{op}/payments",
            new
            {
                id = Guid.NewGuid(),
                accountId = account,
                date = "2026-10-11",
                amount = "75.00",
            }
        );
        var second = client.PostAsJsonAsync(
            $"/api/operations/{op}/payments",
            new
            {
                id = Guid.NewGuid(),
                accountId = account,
                date = "2026-10-11",
                amount = "75.00",
            }
        );
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(
            results,
            x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest
        );
        var current = await client.GetFromJsonAsync<Account[]>("/api/accounts");
        Assert.Equal("925.00", current!.Single().CurrentBalance);
        var operations = await client.GetFromJsonAsync<Operation[]>(
            $"/api/operations?period=2026-10"
        );
        Assert.Equal("75.00", operations!.Single(x => x.Id == op).PaidAmount);
    }

    private sealed record Account(Guid Id, string CurrentBalance);

    private sealed record Operation(Guid Id, string PaidAmount, string PendingAmount);
}
