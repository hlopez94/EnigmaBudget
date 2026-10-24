using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EnigmaBudget.Api.Tests;

public sealed class SharingIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    public void Dispose()=>_factory.Dispose();

    [Fact]
    public async Task Purchase_installments_are_exact_and_month_rules_are_versioned_and_confirmable()
    {
        using var client=await _factory.AuthenticatedClientAsync();
        var cardId=Guid.NewGuid();
        var accountId=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/accounts",new{id=accountId,name="Caja",bankName="Banco",currency="ARS",openingBalance="0.00",openingDate="2026-10-01"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/cards",new{id=cardId,name="Visa",bankName="Banco",closingDay=10,dueDay=20,statementDebtArs="0.00",statementDebtUsd="0.00",asOfDate="2026-10-01"})).StatusCode);
        var self=Guid.NewGuid();var guest=Guid.NewGuid();var groupId=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/groups",new{id=groupId,name="Viaje",participants=new[]{new{id=self,name="Yo",isSelf=true},new{id=guest,name="Ana",isSelf=false}}})).StatusCode);
        var rule=await client.PutAsJsonAsync($"/api/groups/{groupId}/rules/2026-10",new{version=0,mode="Percentage",basisCurrency="ARS",weights=new[]{new{participantId=self,value="60.00"},new{participantId=guest,value="40.00"}}});
        Assert.Equal(HttpStatusCode.OK,rule.StatusCode);
        var purchase=await client.PostAsJsonAsync("/api/purchases",new{id=Guid.NewGuid(),cardId,date="2026-10-02",description="Hotel",category="Viaje",currency="ARS",totalAmount="120000.01",installmentCount=3,firstPeriod="2026-10",groupId});
        Assert.Equal(HttpStatusCode.OK,purchase.StatusCode);
        var body=await purchase.Content.ReadFromJsonAsync<Purchase>();
        Assert.Equal(new[]{"40000.01","40000.00","40000.00"},body!.Installments.Select(x=>x.Amount));
        var receivables=await client.GetFromJsonAsync<Receivable[]>($"/api/groups/{groupId}/receivables?period=2026-10");
        Assert.Equal("16000.00",receivables!.Single(x=>x.ParticipantId==guest).Amount);
        Assert.Equal("24000.01",receivables!.Single(x=>x.ParticipantId==self).Amount);
        var collectionId=Guid.NewGuid();var collectionRequest=new{id=collectionId,installmentId=body.Installments[0].Id,participantId=guest,accountId,date="2026-10-20",amount="8000.00",appliedAmount="8000.00",ruleVersion=1};
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/groups/{groupId}/collections",collectionRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/groups/{groupId}/collections",collectionRequest)).StatusCode);
        var balance=await client.GetFromJsonAsync<AccountBalance[]>($"/api/accounts");Assert.Equal("8000.00",balance!.Single(x=>x.Id==accountId).CurrentBalance);
        var stale=await client.PostAsJsonAsync($"/api/groups/{groupId}/collections",new{id=Guid.NewGuid(),installmentId=body.Installments[0].Id,participantId=guest,accountId,date="2026-10-20",amount="1.00",appliedAmount="1.00",ruleVersion=0});Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var selfPay=await client.PostAsJsonAsync($"/api/groups/{groupId}/collections",new{id=Guid.NewGuid(),installmentId=body.Installments[0].Id,participantId=self,accountId,date="2026-10-20",amount="1.00",appliedAmount="1.00",ruleVersion=1});Assert.Equal(HttpStatusCode.BadRequest,selfPay.StatusCode);
        var excessive=await client.PostAsJsonAsync($"/api/groups/{groupId}/collections",new{id=Guid.NewGuid(),installmentId=body.Installments[0].Id,participantId=guest,accountId,date="2026-10-20",amount="8001.00",appliedAmount="8001.00",ruleVersion=1});Assert.Equal(HttpStatusCode.BadRequest,excessive.StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/groups/{groupId}/rules/2026-10/confirm",new{version=1})).StatusCode);
        var locked=await client.PutAsJsonAsync($"/api/groups/{groupId}/rules/2026-10",new{version=1,mode="Percentage",basisCurrency="ARS",weights=new[]{new{participantId=self,value="50.00"},new{participantId=guest,value="50.00"}}});
        Assert.Equal(HttpStatusCode.Conflict,locked.StatusCode);
        var future=await client.PutAsJsonAsync($"/api/groups/{groupId}/rules/2026-11",new{version=0,mode="Percentage",basisCurrency="ARS",weights=new[]{new{participantId=self,value="30.00"},new{participantId=guest,value="70.00"}}});
        Assert.Equal(HttpStatusCode.OK,future.StatusCode);
        var november=await client.GetFromJsonAsync<Receivable[]>($"/api/groups/{groupId}/receivables?period=2026-11");
        Assert.Equal("28000.00",november!.Single(x=>x.ParticipantId==guest).Amount);
        var overview=await client.GetFromJsonAsync<JsonElement>("/api/finance?period=2026-10");
        Assert.Equal(2,overview.GetProperty("receivables").GetArrayLength());
    }

    private sealed record Purchase(Installment[] Installments);
    private sealed record Installment(Guid Id,string Amount);
    private sealed record Receivable(Guid ParticipantId,string Amount);
    private sealed record AccountBalance(Guid Id,string CurrentBalance);
}
