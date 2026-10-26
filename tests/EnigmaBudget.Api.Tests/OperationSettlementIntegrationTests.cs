using System.Net;
using System.Net.Http.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class OperationSettlementIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset TestInstant = DateTimeOffset.Parse("2026-10-25T15:00:00Z");
    private readonly ApiFactory _factory = new(timeProvider: new FixedTimeProvider(TestInstant));
    public void Dispose() => _factory.Dispose();
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task noncash_settlement_is_scoped_idempotent_and_does_not_create_a_ledger_entry()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS",
            openingBalance = "0.00", openingDate = "2026-01-01"
        })).StatusCode);
        var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = operationId, kind = "Expense", accountId, date = "2026-10-10", description = "Consulta",
            category = "Salud", amount = "1000.00", paid = false
        })).StatusCode);

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        var operation = await db.FinanceOperations.SingleAsync(x => x.Id == operationId);
        var userId = await db.Workspaces.Where(x => x.Id == operation.WorkspaceId).Select(x => x.OwnerUserId).SingleAsync();
        var ruleId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        db.RecurringRules.Add(new RecurringRule { Id = ruleId, WorkspaceId = operation.WorkspaceId, CurrentVersion = 1 });
        db.RecurringRuleRevisions.Add(new RecurringRuleRevision
        {
            Id = revisionId, WorkspaceId = operation.WorkspaceId, RuleId = ruleId, Version = 1,
            EffectiveFrom = new DateOnly(2026, 10, 1), ScheduleAnchorDate = new DateOnly(2026, 10, 1),
            Flow = "Expense", Description = "Consulta", Amount = 1000m, Currency = "ARS", Frequency = "Monthly", Interval = 1
        });
        db.RecurringOccurrenceActions.Add(new RecurringOccurrenceAction
        {
            Id = actionId, WorkspaceId = operation.WorkspaceId, RuleId = ruleId, RuleRevisionId = revisionId,
            ScheduledDate = new DateOnly(2026, 10, 10), Outcome = "Recorded", FinanceOperationId = operationId,
            RequestId = Guid.NewGuid(), PayloadHash = "occurrence"
        });
        db.HealthcareComponents.Add(new HealthcareComponent
        {
            Id = componentId, WorkspaceId = operation.WorkspaceId, RuleRevisionId = revisionId,
            OccurrenceActionId = actionId, Kind = "PayrollWithholding", Amount = 300m, AppliesToProviderBalance = true
        });
        await db.SaveChangesAsync();

        var wrongCurrency = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements", new
        {
            requestId = Guid.NewGuid(), operationVersion = 1, currency = "USD", amount = "300.00", healthcareComponentId = componentId, cardPurchaseId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrency.StatusCode);
        var tooLargeComponentId = Guid.NewGuid();
        db.HealthcareComponents.Add(new HealthcareComponent
        {
            Id = tooLargeComponentId, WorkspaceId = operation.WorkspaceId, RuleRevisionId = revisionId,
            OccurrenceActionId = actionId, Kind = "PayrollWithholding", Amount = 1001m, AppliesToProviderBalance = true
        });
        await db.SaveChangesAsync();
        var overpayment = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements", new
        {
            requestId = Guid.NewGuid(), operationVersion = 1, currency = "ARS", amount = "1001.00", healthcareComponentId = tooLargeComponentId, cardPurchaseId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, overpayment.StatusCode);
        Assert.Empty(await db.NonCashSettlements.Where(x => x.WorkspaceId == operation.WorkspaceId && x.OperationId == operationId).ToArrayAsync());
        Assert.Equal(1, (await db.FinanceOperations.SingleAsync(x => x.Id == operationId)).Version);

        var requestId = Guid.NewGuid();
        var payload = new { requestId, operationVersion = 1, currency = "ARS", amount = "300.00", healthcareComponentId = componentId, cardPurchaseId = (Guid?)null };
        var created = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements", payload);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var replay = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements", payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using (var overview = await client.GetAsync("/api/finance?period=2026-10"))
        {
            Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
            using var overviewJson = System.Text.Json.JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
            Assert.Equal("1000.00", overviewJson.RootElement.GetProperty("budget").GetProperty("operationExpensesArs").GetString());
        }
        using (var projection = await client.GetAsync("/api/projection?through=2026-10-25&scenario=Confirmed"))
        {
            Assert.Equal(HttpStatusCode.OK, projection.StatusCode);
            using var projectionJson = System.Text.Json.JsonDocument.Parse(await projection.Content.ReadAsStringAsync());
            var pending = Assert.Single(projectionJson.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "FinanceOperation" && x.GetProperty("sourceId").GetGuid() == operationId);
            Assert.Equal("700.00", pending.GetProperty("amount").GetString());
        }
        using var duplicateSource = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements", new
        {
            requestId = Guid.NewGuid(), operationVersion = 2, currency = "ARS", amount = "300.00", healthcareComponentId = componentId, cardPurchaseId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateSource.StatusCode);

        var paymentId = Guid.NewGuid();
        var payment = new
        {
            id = paymentId, accountId, date = "2026-10-11", amount = "700.00", operationVersion = 2
        };
        var cash = await client.PostAsJsonAsync($"/api/operations/{operationId}/payments", payment);
        Assert.Equal(HttpStatusCode.OK, cash.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/payments", payment)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/operations/{operationId}/payments", new
        {
            id = paymentId, accountId, date = "2026-10-11", amount = "699.00", operationVersion = 2
        })).StatusCode);
        var reversed = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements/{(await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("nonCashSettlements")[0].GetProperty("id").GetGuid()}/reverse", new
        {
            requestId = Guid.NewGuid(), operationVersion = 3, reason = "Corrección de origen"
        });
        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);
        var reapply = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements", new
        {
            requestId = Guid.NewGuid(), operationVersion = 4, currency = "ARS", amount = "300.00", healthcareComponentId = componentId, cardPurchaseId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.OK, reapply.StatusCode);
        var voidRequest = new { id = Guid.NewGuid(), version = 5, reason = "Anulación de prueba" };
        var voided = await client.PostAsJsonAsync($"/api/operations/{operationId}/void", voidRequest);
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/void", voidRequest)).StatusCode);
        using var list = await client.GetAsync("/api/operations?period=2026-10");
        var dto = (await list.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>())!.Single(x => x.GetProperty("id").GetGuid() == operationId);
        Assert.Equal("700.00", dto.GetProperty("paidAmount").GetString());
        Assert.Equal("700.00", dto.GetProperty("cashPaidAmount").GetString());
        Assert.Equal("0.00", dto.GetProperty("nonCashSettledAmount").GetString());
        Assert.Equal("0.00", dto.GetProperty("pendingAmount").GetString());
        Assert.Equal(4, dto.GetProperty("nonCashSettlements").GetArrayLength());
        using (var overview = await client.GetAsync("/api/finance?period=2026-10"))
        {
            using var overviewJson = System.Text.Json.JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
            var overviewOperation = Assert.Single(overviewJson.RootElement.GetProperty("operations").EnumerateArray(), x => x.GetProperty("id").GetGuid() == operationId);
            Assert.Equal(dto.GetProperty("paidAmount").GetString(), overviewOperation.GetProperty("paidAmount").GetString());
            Assert.Equal(dto.GetProperty("nonCashSettledAmount").GetString(), overviewOperation.GetProperty("nonCashSettledAmount").GetString());
        }
        Assert.Equal(1, await db.LedgerEntries.CountAsync(x => x.WorkspaceId == operation.WorkspaceId && x.Source == "operation-payment"));
        Assert.Equal(1, await db.LedgerEntries.CountAsync(x => x.WorkspaceId == operation.WorkspaceId && x.Source == "operation-void"));
        Assert.Equal(1, await db.FinancePayments.CountAsync(x => x.WorkspaceId == operation.WorkspaceId && x.OperationId == operationId));
        Assert.Equal(4, await db.NonCashSettlements.CountAsync(x => x.WorkspaceId == operation.WorkspaceId && x.OperationId == operationId));
        Assert.Equal(userId, (await db.NonCashSettlements.SingleAsync(x => x.WorkspaceId == operation.WorkspaceId && x.RequestId == requestId)).ActorUserId);
    }

    [Fact]
    public async Task linked_card_charge_moves_operation_budget_to_real_installment_and_void_keeps_installment()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "0.00", openingDate = "2026-01-01"
        })).StatusCode);
        var operationId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var expense = await client.GetAsync("/api/finance/catalog");
        Assert.Equal(HttpStatusCode.OK, expense.StatusCode);
        using var catalogJson = System.Text.Json.JsonDocument.Parse(await expense.Content.ReadAsStringAsync());
        var categoryId = catalogJson.RootElement.GetProperty("categories").EnumerateArray().First(x => x.GetProperty("kind").GetString() == "Expense" && x.GetProperty("isDefault").GetBoolean()).GetProperty("itemId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = operationId, kind = "Expense", accountId, date = "2026-10-10", description = "Compra médica", category = "Salud",
            categoryId, amount = "1000.00", paid = false
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cards", new
        {
            id = cardId, name = "Tarjeta", bankName = "Banco", closingDay = 10, dueDay = 10,
            statementDebtArs = "0.00", statementDebtUsd = "0.00", asOfDate = "2026-10-01"
        })).StatusCode);
        var purchaseId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/purchases", new
        {
            id = purchaseId, cardId, date = "2026-10-10", description = "Compra médica", category = "Salud",
            categoryId, currency = "ARS", totalAmount = "700.00", installmentCount = 1, firstPeriod = "2026-11", groupId = (Guid?)null
        })).StatusCode);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        var purchase = await db.Purchases.SingleAsync(x => x.Id == purchaseId);
        purchase.ExpenseOperationId = operationId;
        await db.SaveChangesAsync();
        var settlement = await client.PostAsJsonAsync($"/api/operations/{operationId}/non-cash-settlements", new
        {
            requestId = Guid.NewGuid(), operationVersion = 1, currency = "ARS", amount = "700.00",
            healthcareComponentId = (Guid?)null, cardPurchaseId = purchaseId
        });
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);

        async Task<System.Text.Json.JsonElement[]> Budget(string period) =>
            await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/budgets?period={period}&currency=ARS") ?? [];
        var october = await Budget("2026-10");
        var octoberSource = Assert.Single(october.SelectMany(x => x.GetProperty("sources").EnumerateArray()), x => x.GetProperty("sourceKind").GetString() == "Operation" && x.GetProperty("sourceId").GetGuid() == operationId);
        Assert.Equal("300.00", octoberSource.GetProperty("amount").GetString());
        var november = await Budget("2026-11");
        var installmentSource = Assert.Single(november.SelectMany(x => x.GetProperty("sources").EnumerateArray()), x => x.GetProperty("sourceKind").GetString() == "Installment" && x.GetProperty("amount").GetString() == "700.00");
        Assert.Equal("2026-10-10", installmentSource.GetProperty("date").GetString());
        using (var overview = await client.GetAsync("/api/finance?period=2026-10"))
        {
            using var json = System.Text.Json.JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
            Assert.Equal("300.00", json.RootElement.GetProperty("budget").GetProperty("operationExpensesArs").GetString());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/void", new { id = Guid.NewGuid(), version = 2, reason = "Anulación" })).StatusCode);
        october = await Budget("2026-10");
        Assert.DoesNotContain(october.SelectMany(x => x.GetProperty("sources").EnumerateArray()), x => x.GetProperty("sourceKind").GetString() == "Operation" && x.GetProperty("sourceId").GetGuid() == operationId);
        november = await Budget("2026-11");
        Assert.Contains(november.SelectMany(x => x.GetProperty("sources").EnumerateArray()), x => x.GetProperty("sourceKind").GetString() == "Installment" && x.GetProperty("amount").GetString() == "700.00");
    }
}
