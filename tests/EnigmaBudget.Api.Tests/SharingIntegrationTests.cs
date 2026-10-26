using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EnigmaBudget.Api.Tests;

public sealed class SharingIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Purchase_installments_are_exact_and_month_rules_are_versioned_and_confirmable()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var cardId = Guid.NewGuid();
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
                        openingBalance = "0.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
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
                        statementDebtArs = "0.00",
                        statementDebtUsd = "0.00",
                        asOfDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
        var self = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var groupId = Guid.NewGuid();
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
        var rule = await client.PutAsJsonAsync(
            $"/api/groups/{groupId}/rules/2026-10",
            new
            {
                version = 0,
                mode = "Percentage",
                basisCurrency = "ARS",
                weights = new[]
                {
                    new { participantId = self, value = "60.00" },
                    new { participantId = guest, value = "40.00" },
                },
            }
        );
        Assert.Equal(HttpStatusCode.OK, rule.StatusCode);
        var purchaseId = Guid.NewGuid();
        var purchase = await client.PostAsJsonAsync(
            "/api/purchases",
            new
            {
                id = purchaseId,
                cardId,
                date = "2026-10-02",
                description = "Hotel",
                category = "Viaje",
                currency = "ARS",
                totalAmount = "120000.01",
                installmentCount = 3,
                firstPeriod = "2026-10",
                groupId,
            }
        );
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    "/api/purchases",
                    new
                    {
                        id = purchaseId,
                        cardId,
                        date = "2026-10-02",
                        description = "Hotel",
                        category = "Otro",
                        currency = "ARS",
                        totalAmount = "120000.01",
                        installmentCount = 3,
                        firstPeriod = "2026-10",
                        groupId,
                    }
                )
            ).StatusCode
        );
        var body = await purchase.Content.ReadFromJsonAsync<Purchase>();
        Assert.Equal(
            new[] { "40000.01", "40000.00", "40000.00" },
            body!.Installments.Select(x => x.Amount)
        );
        var receivables = await client.GetFromJsonAsync<Receivable[]>(
            $"/api/groups/{groupId}/receivables?period=2026-10"
        );
        Assert.Equal("16000.00", receivables!.Single(x => x.ParticipantId == guest).Amount);
        Assert.Equal("24000.01", receivables!.Single(x => x.ParticipantId == self).Amount);
        var collectionId = Guid.NewGuid();
        var collectionRequest = new
        {
            id = collectionId,
            installmentId = body.Installments[0].Id,
            participantId = guest,
            accountId,
            date = "2026-10-20",
            amount = "8000.00",
            appliedAmount = "8000.00",
            ruleVersion = 1,
        };
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/groups/{groupId}/collections",
                    collectionRequest
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/groups/{groupId}/collections",
                    collectionRequest
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    $"/api/groups/{groupId}/collections",
                    new
                    {
                        id = collectionId,
                        installmentId = body.Installments[0].Id,
                        participantId = guest,
                        accountId,
                        date = "2026-10-20",
                        amount = "8000.00",
                        appliedAmount = "7999.00",
                        ruleVersion = 1,
                    }
                )
            ).StatusCode
        );
        var balance = await client.GetFromJsonAsync<AccountBalance[]>($"/api/accounts");
        Assert.Equal("8000.00", balance!.Single(x => x.Id == accountId).CurrentBalance);
        var stale = await client.PostAsJsonAsync(
            $"/api/groups/{groupId}/collections",
            new
            {
                id = Guid.NewGuid(),
                installmentId = body.Installments[0].Id,
                participantId = guest,
                accountId,
                date = "2026-10-20",
                amount = "1.00",
                appliedAmount = "1.00",
                ruleVersion = 0,
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var selfPay = await client.PostAsJsonAsync(
            $"/api/groups/{groupId}/collections",
            new
            {
                id = Guid.NewGuid(),
                installmentId = body.Installments[0].Id,
                participantId = self,
                accountId,
                date = "2026-10-20",
                amount = "1.00",
                appliedAmount = "1.00",
                ruleVersion = 1,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, selfPay.StatusCode);
        var excessive = await client.PostAsJsonAsync(
            $"/api/groups/{groupId}/collections",
            new
            {
                id = Guid.NewGuid(),
                installmentId = body.Installments[0].Id,
                participantId = guest,
                accountId,
                date = "2026-10-20",
                amount = "8001.00",
                appliedAmount = "8001.00",
                ruleVersion = 1,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/groups/{groupId}/rules/2026-10/confirm",
                    new { version = 1 }
                )
            ).StatusCode
        );
        var locked = await client.PutAsJsonAsync(
            $"/api/groups/{groupId}/rules/2026-10",
            new
            {
                version = 1,
                mode = "Percentage",
                basisCurrency = "ARS",
                weights = new[]
                {
                    new { participantId = self, value = "50.00" },
                    new { participantId = guest, value = "50.00" },
                },
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        var purchaseUsingFrozenRule = await client.PostAsJsonAsync(
            "/api/purchases",
            new
            {
                id = Guid.NewGuid(),
                cardId,
                date = "2026-10-03",
                description = "Taxi",
                category = "Viaje",
                currency = "ARS",
                totalAmount = "100.00",
                installmentCount = 1,
                firstPeriod = "2026-10",
                groupId,
            }
        );
        Assert.Equal(HttpStatusCode.OK, purchaseUsingFrozenRule.StatusCode);
        var octoberAfterNewPurchase = await client.GetFromJsonAsync<Receivable[]>(
            $"/api/groups/{groupId}/receivables?period=2026-10"
        );
        Assert.Equal(
            "16040.00",
            octoberAfterNewPurchase!
                .Where(x => x.ParticipantId == guest)
                .Sum(x =>
                    decimal.Parse(x.Amount, System.Globalization.CultureInfo.InvariantCulture)
                )
                .ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        );
        var future = await client.PutAsJsonAsync(
            $"/api/groups/{groupId}/rules/2026-11",
            new
            {
                version = 0,
                mode = "Percentage",
                basisCurrency = "ARS",
                weights = new[]
                {
                    new { participantId = self, value = "30.00" },
                    new { participantId = guest, value = "70.00" },
                },
            }
        );
        Assert.Equal(HttpStatusCode.OK, future.StatusCode);
        var november = await client.GetFromJsonAsync<Receivable[]>(
            $"/api/groups/{groupId}/receivables?period=2026-11"
        );
        Assert.Equal("28000.00", november!.Single(x => x.ParticipantId == guest).Amount);
        var overview = await client.GetFromJsonAsync<JsonElement>("/api/finance?period=2026-10");
        Assert.Equal(4, overview.GetProperty("receivables").GetArrayLength());
    }

    [Fact]
    public async Task Concurrent_collections_for_one_participant_cannot_overcollect()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var account = Guid.NewGuid();
        var card = Guid.NewGuid();
        var group = Guid.NewGuid();
        var self = Guid.NewGuid();
        var guest = Guid.NewGuid();
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
                        openingBalance = "0.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
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
                    "/api/groups",
                    new
                    {
                        id = group,
                        name = "Grupo",
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
            HttpStatusCode.OK,
            (
                await client.PutAsJsonAsync(
                    $"/api/groups/{group}/rules/2026-10",
                    new
                    {
                        version = 0,
                        mode = "Percentage",
                        basisCurrency = "ARS",
                        weights = new[]
                        {
                            new { participantId = self, value = "50.00" },
                            new { participantId = guest, value = "50.00" },
                        },
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
                description = "Compra",
                category = "Viaje",
                currency = "ARS",
                totalAmount = "100.00",
                installmentCount = 1,
                firstPeriod = "2026-10",
                groupId = group,
            }
        );
        var installment = (await purchase.Content.ReadFromJsonAsync<Purchase>())!
            .Installments[0]
            .Id;
        var p1 = client.PostAsJsonAsync(
            $"/api/groups/{group}/collections",
            new
            {
                id = Guid.NewGuid(),
                installmentId = installment,
                participantId = guest,
                accountId = account,
                date = "2026-10-20",
                amount = "30.00",
                appliedAmount = "30.00",
                ruleVersion = 1,
            }
        );
        var p2 = client.PostAsJsonAsync(
            $"/api/groups/{group}/collections",
            new
            {
                id = Guid.NewGuid(),
                installmentId = installment,
                participantId = guest,
                accountId = account,
                date = "2026-10-20",
                amount = "30.00",
                appliedAmount = "30.00",
                ruleVersion = 1,
            }
        );
        var responses = await Task.WhenAll(p1, p2);
        Assert.Equal(2, responses.Length);
        var success = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(
            responses,
            x => x.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict
        );
        if (rejected.StatusCode == HttpStatusCode.BadRequest)
        {
            using var rejectionBody = await rejected.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.Equal(
                "El cobro supera el saldo pendiente de la persona.",
                rejectionBody!.RootElement.GetProperty("detail").GetString()
            );
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        using var successBody = await success.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("30.00", successBody!.RootElement.GetProperty("appliedAmount").GetString());
        var receivables = await client.GetFromJsonAsync<Receivable[]>(
            $"/api/groups/{group}/receivables?period=2026-10"
        );
        var guestReceivable = receivables!.Single(x => x.ParticipantId == guest);
        Assert.Equal("50.00", guestReceivable.Amount);
        Assert.Equal("30.00", guestReceivable.CollectedAmount);
        Assert.Equal("20.00", guestReceivable.PendingAmount);
        var accountBalances = await client.GetFromJsonAsync<AccountBalance[]>("/api/accounts");
        Assert.Equal("30.00", accountBalances!.Single(x => x.Id == account).CurrentBalance);
    }

    [Fact]
    public async Task Fx_collection_uses_separate_bank_and_receivable_amounts()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var usd = Guid.NewGuid();
        var card = Guid.NewGuid();
        var group = Guid.NewGuid();
        var self = Guid.NewGuid();
        var guest = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/accounts",
                    new
                    {
                        id = usd,
                        name = "USD",
                        bankName = "Banco",
                        currency = "USD",
                        openingBalance = "50.00",
                        openingDate = "2026-10-01",
                    }
                )
            ).StatusCode
        );
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
                    "/api/groups",
                    new
                    {
                        id = group,
                        name = "Grupo",
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
            HttpStatusCode.OK,
            (
                await client.PutAsJsonAsync(
                    $"/api/groups/{group}/rules/2026-10",
                    new
                    {
                        version = 0,
                        mode = "Percentage",
                        basisCurrency = "ARS",
                        weights = new[]
                        {
                            new { participantId = self, value = "50.00" },
                            new { participantId = guest, value = "50.00" },
                        },
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
                description = "Compra",
                category = "Viaje",
                currency = "ARS",
                totalAmount = "100.00",
                installmentCount = 1,
                firstPeriod = "2026-10",
                groupId = group,
            }
        );
        var installment = (await purchase.Content.ReadFromJsonAsync<Purchase>())!
            .Installments[0]
            .Id;
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    $"/api/groups/{group}/collections",
                    new
                    {
                        id = Guid.NewGuid(),
                        installmentId = installment,
                        participantId = guest,
                        accountId = usd,
                        date = "2026-10-20",
                        amount = "3.00",
                        appliedAmount = "20.00",
                        ruleVersion = 1,
                    }
                )
            ).StatusCode
        );
        var accounts = await client.GetFromJsonAsync<AccountBalance[]>("/api/accounts");
        Assert.Equal("53.00", accounts!.Single(x => x.Id == usd).CurrentBalance);
        var receivables = await client.GetFromJsonAsync<Receivable[]>(
            $"/api/groups/{group}/receivables?period=2026-10"
        );
        Assert.Equal("20.00", receivables!.Single(x => x.ParticipantId == guest).CollectedAmount);
    }

    [Fact]
    public async Task Concurrent_rule_updates_with_same_version_only_apply_one_payload()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var group = Guid.NewGuid();
        var self = Guid.NewGuid();
        var guest = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/groups",
                    new
                    {
                        id = group,
                        name = "Grupo",
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
            HttpStatusCode.OK,
            (
                await client.PutAsJsonAsync(
                    $"/api/groups/{group}/rules/2026-10",
                    new
                    {
                        version = 0,
                        mode = "Percentage",
                        basisCurrency = "ARS",
                        weights = new[]
                        {
                            new { participantId = self, value = "50.00" },
                            new { participantId = guest, value = "50.00" },
                        },
                    }
                )
            ).StatusCode
        );

        var first = client.PutAsJsonAsync(
            $"/api/groups/{group}/rules/2026-10",
            new
            {
                version = 1,
                mode = "Percentage",
                basisCurrency = "ARS",
                weights = new[]
                {
                    new { participantId = self, value = "60.00" },
                    new { participantId = guest, value = "40.00" },
                },
            }
        );
        var second = client.PutAsJsonAsync(
            $"/api/groups/{group}/rules/2026-10",
            new
            {
                version = 1,
                mode = "Percentage",
                basisCurrency = "ARS",
                weights = new[]
                {
                    new { participantId = self, value = "40.00" },
                    new { participantId = guest, value = "60.00" },
                },
            }
        );
        var responses = await Task.WhenAll(first, second);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    private sealed record Purchase(Installment[] Installments);

    private sealed record Installment(Guid Id, string Amount);

    private sealed record Receivable(
        Guid ParticipantId,
        string Amount,
        string CollectedAmount,
        string PendingAmount
    );

    private sealed record AccountBalance(Guid Id, string CurrentBalance);
}
