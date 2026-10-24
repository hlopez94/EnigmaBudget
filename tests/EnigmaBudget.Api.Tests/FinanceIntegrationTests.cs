using System.Net;
using System.Net.Http.Json;

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
        var accountA = Guid.NewGuid(); var accountB = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/onboarding")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id=accountA, name="Caja", bankName="Banco", currency="ARS", openingBalance="100.00", openingDate="2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id=accountB, name="Ahorro", bankName="Banco", currency="ARS", openingBalance="0.00", openingDate="2026-10-01" })).StatusCode);

        var incomeId = Guid.NewGuid();
        var income = new { id=incomeId, kind="Income", accountId=accountA, date="2026-10-10", description="Sueldo", category="Trabajo", amount="50.00", paid=true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", income)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", income)).StatusCode);
        var expense = await client.PostAsJsonAsync("/api/operations", new { id=Guid.NewGuid(), kind="Expense", accountId=accountA, date="2026-10-11", description="Compra", category="Comida", amount="10.00", paid=false });
        Assert.Equal(HttpStatusCode.OK, expense.StatusCode);
        var expenseId = (await expense.Content.ReadFromJsonAsync<Operation>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{expenseId}/payments", new { id=Guid.NewGuid(), accountId=accountA, date="2026-10-12", amount="4.00" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/transfers", new { id=Guid.NewGuid(), fromAccountId=accountA, toAccountId=accountB, date="2026-10-13", amount="20.00", destinationAmount="20.00" })).StatusCode);

        var accounts = await client.GetFromJsonAsync<Account[]>("/api/accounts");
        Assert.Equal("126.00", accounts!.Single(x=>x.Id==accountA).CurrentBalance);
        Assert.Equal("20.00", accounts!.Single(x=>x.Id==accountB).CurrentBalance);
        var opResponse = await client.GetAsync("/api/operations?period=2026-10");
        Assert.True(opResponse.IsSuccessStatusCode, await opResponse.Content.ReadAsStringAsync());
        var operation = (await opResponse.Content.ReadFromJsonAsync<Operation[]>())!.Single(x=>x.Id==expenseId);
        Assert.Equal("4.00", operation.PaidAmount);
        Assert.Equal("6.00", operation.PendingAmount);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/finance?period=2026-10")).StatusCode);
    }

    [Fact]
    public async Task Other_workspace_cannot_read_or_mutate_account()
    {
        using var owner = await _factory.AuthenticatedClientAsync("finance-owner");
        using var stranger = await _factory.AuthenticatedClientAsync("finance-stranger");
        var accountId=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/accounts", new { id=accountId, name="Caja", bankName="Banco", currency="ARS", openingBalance="0.00", openingDate="2026-10-01" })).StatusCode);
        var response = await stranger.PutAsJsonAsync($"/api/accounts/{accountId}", new { version=1, name="Ajena", bankName="Banco", isArchived=false });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    private sealed record Account(Guid Id, string CurrentBalance);
    private sealed record Operation(Guid Id, string PaidAmount, string PendingAmount);
}
