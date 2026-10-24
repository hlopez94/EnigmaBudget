using System.Net;
using System.Net.Http.Json;

namespace EnigmaBudget.Api.Tests;

public sealed class CardCycleIntegrationTests:IDisposable
{
    private readonly ApiFactory _factory=new();public void Dispose()=>_factory.Dispose();
    [Fact]
    public async Task Statement_reconciles_once_and_partial_card_payments_reduce_pending_amount()
    {
        using var client=await _factory.AuthenticatedClientAsync();var card=Guid.NewGuid();var account=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/cards",new{id=card,name="Visa",bankName="Banco",closingDay=10,dueDay=20,statementDebtArs="0.00",statementDebtUsd="0.00",asOfDate="2026-10-01"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/accounts",new{id=account,name="Caja",bankName="Banco",currency="ARS",openingBalance="100000.00",openingDate="2026-10-01"})).StatusCode);
        var purchase=await client.PostAsJsonAsync("/api/purchases",new{id=Guid.NewGuid(),cardId=card,date="2026-10-02",description="Notebook",category="Trabajo",currency="ARS",totalAmount="120000.00",installmentCount=3,firstPeriod="2026-10",groupId=(Guid?)null});Assert.Equal(HttpStatusCode.OK,purchase.StatusCode);
        var statement=await client.GetFromJsonAsync<Statement>($"/api/cards/{card}/statement?period=2026-10&currency=ARS");Assert.Equal("40000.00",statement!.ExpectedAmount);
        var save=await client.PutAsJsonAsync($"/api/cards/{card}/statement",new{id=Guid.NewGuid(),period="2026-10",currency="ARS",version=0,dueDate="2026-10-20",actualTotal="40000.00",adjustments=Array.Empty<object>()});Assert.Equal(HttpStatusCode.OK,save.StatusCode);var saved=await save.Content.ReadFromJsonAsync<SavedStatement>();
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/cards/{card}/statement/confirm",new{id=saved!.Id,version=saved.Version})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/cards/{card}/payments",new{id=Guid.NewGuid(),accountId=account,date="2026-10-21",amount="20000.00",currency="ARS",appliedAmount="20000.00",statementId=saved.Id})).StatusCode);
        var partial=await client.GetFromJsonAsync<Statement>($"/api/cards/{card}/statement?period=2026-10&currency=ARS");Assert.Equal("20000.00",partial!.PendingAmount);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/cards/{card}/payments",new{id=Guid.NewGuid(),accountId=account,date="2026-10-22",amount="20000.00",currency="ARS",appliedAmount="20000.00",statementId=saved.Id})).StatusCode);
        var paid=await client.GetFromJsonAsync<Statement>($"/api/cards/{card}/statement?period=2026-10&currency=ARS");Assert.Equal("0.00",paid!.PendingAmount);
    }
    private sealed record Statement(string ExpectedAmount,string PendingAmount);
    private sealed record SavedStatement(Guid Id,int Version);
}
