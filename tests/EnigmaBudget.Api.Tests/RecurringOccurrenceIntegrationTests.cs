using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class RecurringOccurrenceIntegrationTests
{
    [Fact]
    public async Task recorded_occurrence_creates_one_gross_operation_and_settlement_parts()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-gross");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-gross");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var accountId = await CreateAccount(client);
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, accountId)).StatusCode);

        var actionRequestId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", new
        {
            requestId = actionRequestId, ruleVersion = 1, actualDate = "2026-10-25", actualAmount = "1000.00",
            cashAccountId = accountId, preferredAccountId = (Guid?)null, actualCashAmount = "700.00",
            cardId = (Guid?)null, cardFirstPeriod = (string?)null, cardChargeAmount = "0.00",
            actualHealthcareComponents = new[] { new { id = Guid.NewGuid(), kind = "PayrollWithholding", amount = "300.00", appliesToProviderBalance = true, participantId = (Guid?)null } },
            catalogOverride = (object?)null, clearPreferredAccount = false
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var operation = await db.FinanceOperations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == action.FinanceOperationId);
        Assert.Equal(1000m, operation.Amount);
        Assert.Equal("2026-10-25", operation.Date.ToString("yyyy-MM-dd"));
        Assert.Equal(accountId, operation.AccountId);
        var payment = await db.FinancePayments.SingleAsync(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id);
        Assert.Equal(700m, payment.Amount);
        Assert.Equal(accountId, payment.AccountId);
        Assert.Equal(-700m, await db.LedgerEntries.Where(x => x.WorkspaceId == workspaceId && x.Source == "operation-payment").SumAsync(x => x.Amount));
        var component = await db.HealthcareComponents.SingleAsync(x => x.WorkspaceId == workspaceId && x.OccurrenceActionId == action.Id);
        Assert.Equal(action.RuleRevisionId, component.RuleRevisionId);
        var settlement = await db.NonCashSettlements.SingleAsync(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id);
        Assert.Equal("PayrollWithholding", settlement.Kind);
        Assert.Equal(300m, settlement.Amount);
        Assert.Equal(component.Id, settlement.HealthcareComponentId);
        Assert.Equal(0m, OperationSettlementCalculator.Calculate(operation, [payment], [settlement]).PendingAmount);
    }

    [Fact]
    public async Task skip_suppresses_only_that_scheduled_date()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-skip");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-skip");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);

        var requestId = Guid.NewGuid();
        var skipped = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/skip", new { requestId, ruleVersion = 1, reason = "No corresponde" });
        Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-10-01&through=2026-12-31"));
        var rows = response.RootElement.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("ruleId").GetGuid() == ruleId).ToArray();
        Assert.Equal(["Skipped", "Estimated", "Estimated"], rows.Select(x => x.GetProperty("status").GetString()!).ToArray());
        Assert.Equal(["2026-10-01", "2026-11-01", "2026-12-01"], rows.Select(x => x.GetProperty("scheduledDate").GetString()!).ToArray());
    }

    [Fact]
    public async Task recorded_income_changes_cash_only_when_collected_and_preserves_projection_remainder()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-income");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-income");
        var categoryId = Guid.NewGuid(); var sourceId = Guid.NewGuid(); var accountId = await CreateAccount(client);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = categoryId, name = "Sueldo", kind = "Income" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/income-sources", new { id = sourceId, name = "Trabajo", kind = "Salary" })).StatusCode);
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateIncomeRule(client, ruleId, categoryId, sourceId, accountId)).StatusCode);
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-25", actualAmount = "3000.00",
            cashAccountId = accountId, preferredAccountId = (Guid?)null, actualCashAmount = "2000.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = false };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var operation = await db.FinanceOperations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == action.FinanceOperationId);
        Assert.Equal("Income", operation.Kind); Assert.Equal(3000m, operation.Amount);
        Assert.Equal(2000m, await db.FinancePayments.Where(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id).SumAsync(x => x.Amount));
        Assert.Equal(2000m, await db.LedgerEntries.Where(x => x.WorkspaceId == workspaceId && x.Source == "operation-payment").SumAsync(x => x.Amount));
        Assert.Equal(0, await db.NonCashSettlements.CountAsync(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id));
        Assert.Equal(1000m, OperationSettlementCalculator.Calculate(operation,
            await db.FinancePayments.Where(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id).ToArrayAsync(), []).PendingAmount);
        using var projection = JsonDocument.Parse(await client.GetStringAsync("/api/projection?through=2026-10-31"));
        var pending = Assert.Single(projection.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "FinanceOperation" && x.GetProperty("sourceId").GetGuid() == operation.Id);
        Assert.Equal("Incoming", pending.GetProperty("direction").GetString());
        Assert.Equal("1000.00", pending.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task uncollected_income_can_clear_an_archived_preferred_account()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-clear-archived-income");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-clear-archived-income");
        var accountId = await CreateAccount(client); var categoryId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = categoryId, name = "Honorarios", kind = "Income" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/income-sources", new { id = sourceId, name = "Clientes", kind = "Other" })).StatusCode);
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateIncomeRule(client, ruleId, categoryId, sourceId, accountId)).StatusCode);
        using (var account = JsonDocument.Parse(await client.GetStringAsync("/api/accounts")))
        {
            var version = account.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == accountId).GetProperty("version").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/accounts/{accountId}", new { version, name = "Caja", bankName = "Banco", isArchived = true })).StatusCode);
        }
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "3000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var operation = await db.FinanceOperations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == action.FinanceOperationId);
        Assert.Null(operation.AccountId);
        Assert.Empty(await db.FinancePayments.Where(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id).ToArrayAsync());
        Assert.Empty(await db.LedgerEntries.Where(x => x.WorkspaceId == workspaceId && x.Source == "operation-payment").ToArrayAsync());
    }

    [Fact]
    public async Task actual_date_differs_without_changing_stable_scheduled_id_or_estimate()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-actual-metadata");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-actual-metadata");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        var stableId = RecurringCalendar.StableOccurrenceId(workspaceId, ruleId, new DateOnly(2026, 10, 1));
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "1200.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/state", new { requestId = Guid.NewGuid(), version = 1, effectiveFrom = "2026-11-01", state = "Paused" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        var changedReplay = request with { actualAmount = "1300.00" };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", changedReplay)).StatusCode);
        using var calendar = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-10-01&through=2026-10-31"));
        var item = calendar.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("ruleId").GetGuid() == ruleId);
        Assert.Equal(stableId, item.GetProperty("occurrenceId").GetGuid());
        Assert.Equal("1000.00", item.GetProperty("amount").GetString());
        Assert.Equal("1200.00", item.GetProperty("actualAmount").GetString());
        Assert.Equal("2026-10-20", item.GetProperty("actualDate").GetString());
        Assert.False(item.GetProperty("operationVoided").GetBoolean());
        Assert.Equal("2026-10-01", item.GetProperty("scheduledDate").GetString());
        Assert.NotEqual(Guid.Empty, item.GetProperty("revisionId").GetGuid());
        using var projection = JsonDocument.Parse(await client.GetStringAsync("/api/projection?through=2026-10-31"));
        var operationRows = projection.RootElement.GetProperty("rows").EnumerateArray().Where(x => x.GetProperty("sourceKind").GetString() == "FinanceOperation").ToArray();
        Assert.Single(operationRows);
        Assert.DoesNotContain(projection.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "RecurringOccurrence");
        await using var db = Db(factory.TestConnection);
        Assert.Null(await db.FinanceOperations.Where(x => x.WorkspaceId == workspaceId && x.Description == "Servicio recurrente").Select(x => x.AccountId).SingleAsync());
    }

    [Fact]
    public async Task healthcare_record_uses_actual_gross_and_distinct_actual_component_snapshots()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-healthcare-gross");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-healthcare-gross");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense"); var accountId = await CreateAccount(client);
        var ruleId = Guid.NewGuid(); var forecastChargeId = Guid.NewGuid(); var forecastDiscountId = Guid.NewGuid(); var forecastPayrollId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync("/api/recurrences", new
        {
            id = ruleId, flow = "Expense", description = "Cobertura médica", categoryId, incomeSourceId = (Guid?)null,
            merchantId = (Guid?)null, amount = "1000.00", amountMode = "Estimated", currency = "ARS", frequency = "Monthly",
            interval = 1, startDate = "2026-10-01", endDate = (string?)null, dueDay = (int?)null,
            preferredAccountId = accountId, preferredCardId = (Guid?)null,
            healthcareComponents = new[]
            {
                new { id = forecastChargeId, kind = "ProviderCharge", amount = "1200.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = forecastDiscountId, kind = "ProviderDiscount", amount = "200.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = forecastPayrollId, kind = "PayrollWithholding", amount = "300.00", appliesToProviderBalance = false, participantId = (Guid?)null }
            }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "1000.00",
            cashAccountId = accountId, preferredAccountId = (Guid?)null, actualCashAmount = "700.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = new[]
            {
                new { id = Guid.NewGuid(), kind = "ProviderCharge", amount = "1200.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = Guid.NewGuid(), kind = "ProviderDiscount", amount = "200.00", appliesToProviderBalance = false, participantId = (Guid?)null },
                new { id = Guid.NewGuid(), kind = "PayrollWithholding", amount = "300.00", appliesToProviderBalance = true, participantId = (Guid?)null }
            }, catalogOverride = (object?)null, clearPreferredAccount = false };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        using var budget = JsonDocument.Parse(await client.GetStringAsync("/api/budgets?period=2026-10&currency=ARS"));
        Assert.Equal("1000.00", budget.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == categoryId).GetProperty("spentAmount").GetString());
        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var operation = await db.FinanceOperations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == action.FinanceOperationId);
        Assert.Equal(1000m, operation.Amount);
        Assert.Equal(700m, await db.FinancePayments.Where(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id).SumAsync(x => x.Amount));
        Assert.Equal(-700m, await db.LedgerEntries.Where(x => x.WorkspaceId == workspaceId && x.Source == "operation-payment").SumAsync(x => x.Amount));
        var planned = await db.HealthcareComponents.Where(x => x.WorkspaceId == workspaceId && x.RuleRevisionId == action.RuleRevisionId && x.OccurrenceActionId == null).ToArrayAsync();
        Assert.Equal(3, planned.Length);
        var actual = await db.HealthcareComponents.Where(x => x.WorkspaceId == workspaceId && x.OccurrenceActionId == action.Id).ToArrayAsync();
        Assert.Equal(3, actual.Length);
        Assert.Empty(planned.Select(x => x.Id).Intersect(actual.Select(x => x.Id)));
        Assert.Equal(300m, await db.NonCashSettlements.Where(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id).SumAsync(x => x.Amount));
    }

    [Fact]
    public async Task archived_revision_catalog_requires_override_and_snapshot_preserves_original_revision()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-catalog-override");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-catalog-override");
        var oldCategoryId = Guid.NewGuid(); var newCategoryId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = oldCategoryId, name = "Servicios", kind = "Expense" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = newCategoryId, name = "Hogar", kind = "Expense" })).StatusCode);
        var ruleId = Guid.NewGuid();
        var created = await client.PostAsJsonAsync("/api/recurrences", new { id = ruleId, flow = "Expense", description = "Servicio", categoryId = oldCategoryId,
            incomeSourceId = (Guid?)null, merchantId = (Guid?)null, amount = "100.00", amountMode = "Fixed", currency = "ARS", frequency = "Monthly",
            interval = 1, startDate = "2026-10-01", endDate = (string?)null, dueDay = (int?)null, preferredAccountId = (Guid?)null,
            preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await using (var seed = Db(factory.TestConnection))
        {
            var category = await seed.FinanceCategories.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == oldCategoryId);
            category.IsArchived = true;
            await seed.SaveChangesAsync();
        }
        var rejected = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "100.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", rejected)).StatusCode);
        var overridden = rejected with { requestId = Guid.NewGuid(), catalogOverride = new { categoryId = newCategoryId, merchantId = (Guid?)null, incomeSourceId = (Guid?)null } };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", overridden)).StatusCode);
        using var calendar = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-10-01&through=2026-10-31"));
        var item = calendar.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("ruleId").GetGuid() == ruleId);
        Assert.Equal(RecurringCalendar.StableOccurrenceId(workspaceId, ruleId, new DateOnly(2026, 10, 1)), item.GetProperty("occurrenceId").GetGuid());
        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var originalRevision = await db.RecurringRuleRevisions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId && x.Version == 1);
        var operation = await db.FinanceOperations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == action.FinanceOperationId);
        Assert.Equal(originalRevision.Id, action.RuleRevisionId);
        Assert.Equal(oldCategoryId, originalRevision.CategoryId);
        Assert.Equal(newCategoryId, operation.CategoryId);
        Assert.Equal("Hogar", operation.Category);
    }

    [Fact]
    public async Task clearing_preference_does_not_bypass_actual_cash_account_requirement()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-clear-requires-cash");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-clear-requires-cash");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var accountId = await CreateAccount(client); var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, accountId)).StatusCode);
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "100.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "10.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        await using var db = Db(factory.TestConnection);
        Assert.Empty(await db.RecurringOccurrenceActions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).ToArrayAsync());
        Assert.Empty(await db.FinanceOperations.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task recorded_card_service_has_card_debt_without_bank_debit()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-card");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-card");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var cardId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cards", new { id = cardId, name = "Visa", bankName = "Banco", statementDebtArs = "0.00", statementDebtUsd = "0.00", asOfDate = "2026-10-01" })).StatusCode);
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "1000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId,
            cardFirstPeriod = "2026-11", cardChargeAmount = "700.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var operation = await db.FinanceOperations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == action.FinanceOperationId);
        var purchase = await db.Purchases.SingleAsync(x => x.WorkspaceId == workspaceId && x.ExpenseOperationId == operation.Id);
        Assert.Equal(action.CardPurchaseId, purchase.Id);
        Assert.Equal(700m, purchase.TotalAmount);
        Assert.Equal(1, purchase.InstallmentCount);
        var linkedInstallmentId = await db.Installments.Where(x => x.WorkspaceId == workspaceId && x.PurchaseId == purchase.Id).Select(x => x.Id).SingleAsync();
        Assert.Equal(700m, await db.Installments.Where(x => x.WorkspaceId == workspaceId && x.PurchaseId == purchase.Id).SumAsync(x => x.Amount));
        Assert.Equal(0, await db.FinancePayments.CountAsync(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id));
        Assert.Equal(0, await db.LedgerEntries.CountAsync(x => x.WorkspaceId == workspaceId && x.Source == "operation-payment"));
        var cardSettlement = await db.NonCashSettlements.SingleAsync(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id);
        Assert.Equal("CardCharge", cardSettlement.Kind);
        Assert.Equal(purchase.Id, cardSettlement.CardPurchaseId);
        Assert.Equal(300m, OperationSettlementCalculator.Calculate(operation, [], [cardSettlement]).PendingAmount);
        using (var october = JsonDocument.Parse(await client.GetStringAsync("/api/budgets?period=2026-10&currency=ARS")))
            Assert.Equal("300.00", october.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == categoryId).GetProperty("spentAmount").GetString());
        using (var november = JsonDocument.Parse(await client.GetStringAsync("/api/budgets?period=2026-11&currency=ARS")))
            Assert.Equal("700.00", november.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == categoryId).GetProperty("committedCardAmount").GetString());
        using (var projectionBeforeVoid = JsonDocument.Parse(await client.GetStringAsync("/api/projection?through=2026-11-30")))
        {
            var rows = projectionBeforeVoid.RootElement.GetProperty("rows").EnumerateArray().ToArray();
            var pendingOperation = Assert.Single(rows, x => x.GetProperty("sourceKind").GetString() == "FinanceOperation" && x.GetProperty("sourceId").GetGuid() == operation.Id);
            Assert.Equal("300.00", pendingOperation.GetProperty("amount").GetString());
            var linkedCardObligation = Assert.Single(rows, x => x.GetProperty("sourceKind").GetString() == "CardObligation" && x.GetProperty("sourceId").GetGuid() == linkedInstallmentId);
            Assert.Equal("700.00", linkedCardObligation.GetProperty("amount").GetString());
        }

        var voidResponse = await client.PostAsJsonAsync($"/api/operations/{operation.Id}/void", new { id = Guid.NewGuid(), version = operation.Version, reason = "Corrección" });
        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        using var voidNovember = JsonDocument.Parse(await client.GetStringAsync("/api/budgets?period=2026-11&currency=ARS"));
        Assert.Equal("700.00", voidNovember.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == categoryId).GetProperty("committedCardAmount").GetString());
        using var projection = JsonDocument.Parse(await client.GetStringAsync("/api/projection?through=2026-11-30"));
        var remainingCardObligation = Assert.Single(projection.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "CardObligation" && x.GetProperty("sourceId").GetGuid() == linkedInstallmentId);
        Assert.Equal("700.00", remainingCardObligation.GetProperty("amount").GetString());
        using var calendarAfterVoid = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-10-01&through=2026-10-31"));
        var recordedAfterVoid = calendarAfterVoid.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("ruleId").GetGuid() == ruleId);
        Assert.True(recordedAfterVoid.GetProperty("operationVoided").GetBoolean());
    }

    [Fact]
    public async Task estimated_or_ambiguous_withholding_does_not_create_settlement()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-unapplied-withholding");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-unapplied-withholding");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense"); var accountId = await CreateAccount(client); var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "1000.00",
            cashAccountId = accountId, preferredAccountId = (Guid?)null, actualCashAmount = "700.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = new[]
            { new { id = Guid.NewGuid(), kind = "PayrollWithholding", amount = "300.00", appliesToProviderBalance = false, participantId = (Guid?)null } },
            catalogOverride = (object?)null, clearPreferredAccount = false };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        var operation = await db.FinanceOperations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == action.FinanceOperationId);
        var payment = await db.FinancePayments.SingleAsync(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id);
        Assert.Empty(await db.NonCashSettlements.Where(x => x.WorkspaceId == workspaceId && x.OperationId == operation.Id).ToArrayAsync());
        Assert.Equal(300m, OperationSettlementCalculator.Calculate(operation, [payment], []).PendingAmount);
    }

    [Fact]
    public async Task concurrent_record_request_creates_one_action_and_operation()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-concurrent");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-concurrent");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense"); var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "1000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request),
            client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in responses) response.Dispose();
        await using var db = Db(factory.TestConnection);
        Assert.Single(await db.RecurringOccurrenceActions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).ToArrayAsync());
        Assert.Single(await db.FinanceOperations.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task foreign_card_or_account_is_rejected_without_finance_writes()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var owner = await factory.AuthenticatedClientAsync("recurring-occurrence-foreign-owner");
        using var foreign = await factory.AuthenticatedClientAsync("recurring-occurrence-foreign-source");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-foreign-owner");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense"); var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(owner, ruleId, categoryId, null)).StatusCode);
        var foreignAccountId = await CreateAccount(foreign); var foreignCardId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await foreign.PostAsJsonAsync("/api/cards", new { id = foreignCardId, name = "Otra", bankName = "Otro banco", statementDebtArs = "0.00", statementDebtUsd = "0.00", asOfDate = "2026-10-01" })).StatusCode);
        var cash = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "100.00",
            cashAccountId = (Guid?)foreignAccountId, preferredAccountId = (Guid?)null, actualCashAmount = "100.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = false };
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", cash)).StatusCode);
        var card = cash with { requestId = Guid.NewGuid(), cashAccountId = null, actualCashAmount = "0.00", cardId = foreignCardId,
            cardFirstPeriod = "2026-11", cardChargeAmount = "100.00", clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", card)).StatusCode);
        await using var db = Db(factory.TestConnection);
        Assert.Empty(await db.RecurringOccurrenceActions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).ToArrayAsync());
        Assert.Empty(await db.FinanceOperations.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
        Assert.Empty(await db.FinancePayments.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
        Assert.Empty(await db.Purchases.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
        Assert.Empty(await db.NonCashSettlements.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task paused_or_ended_interval_does_not_create_new_occurrence_action()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-ineligible-interval");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-ineligible-interval");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense"); var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/state", new { requestId = Guid.NewGuid(), version = 1, effectiveFrom = "2026-11-01", state = "Paused" })).StatusCode);
        var november = new { requestId = Guid.NewGuid(), ruleVersion = 2, actualDate = "2026-10-20", actualAmount = "1000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-11-01/record", november)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/state", new { requestId = Guid.NewGuid(), version = 2, effectiveFrom = "2026-12-01", state = "Ended" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-12-01/skip", new { requestId = Guid.NewGuid(), ruleVersion = 3, reason = "No corresponde" })).StatusCode);
        await using var db = Db(factory.TestConnection);
        Assert.Empty(await db.RecurringOccurrenceActions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).ToArrayAsync());
        Assert.Empty(await db.FinanceOperations.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task ended_rule_allows_attending_prior_active_unhandled_occurrence()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-ended-history");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-ended-history");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/state", new { requestId = Guid.NewGuid(), version = 1, effectiveFrom = "2026-11-01", state = "Ended" })).StatusCode);
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 2, actualDate = "2026-10-25", actualAmount = "1000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        using var calendar = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-10-01&through=2026-12-31"));
        var items = calendar.RootElement.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("ruleId").GetGuid() == ruleId).ToArray();
        Assert.Single(items);
        Assert.Equal("Recorded", items[0].GetProperty("status").GetString());
        Assert.Equal("2026-10-01", items[0].GetProperty("scheduledDate").GetString());
        await using var db = Db(factory.TestConnection);
        var action = await db.RecurringOccurrenceActions.SingleAsync(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId);
        Assert.Equal(1, await db.RecurringRuleRevisions.Where(x => x.WorkspaceId == workspaceId && x.Id == action.RuleRevisionId).Select(x => x.Version).SingleAsync());
    }

    [Fact]
    public async Task recorded_history_keeps_original_revision_after_same_start_schedule_change()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-same-start-history");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-same-start-history");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        Guid oldRevisionId;
        await using (var db = Db(factory.TestConnection))
            oldRevisionId = await db.RecurringRuleRevisions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).Select(x => x.Id).SingleAsync();
        var request = new { requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "1000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", request)).StatusCode);
        var correction = await client.PutAsJsonAsync($"/api/recurrences/{ruleId}", new
        {
            requestId = Guid.NewGuid(), version = 1, effectiveFrom = "2026-10-01", description = "Calendario actualizado",
            categoryId, incomeSourceId = (Guid?)null, merchantId = (Guid?)null, amount = "1500.00", amountMode = "Fixed",
            currency = "ARS", frequency = "Monthly", interval = 1, scheduleAnchorDate = "2026-10-10", endDate = (string?)null,
            dueDay = (int?)null, preferredAccountId = (Guid?)null, preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        using var calendar = JsonDocument.Parse(await client.GetStringAsync("/api/recurrences/occurrences?from=2026-10-01&through=2026-11-30"));
        var rows = calendar.RootElement.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("ruleId").GetGuid() == ruleId).ToArray();
        var action = Assert.Single(rows, x => x.GetProperty("status").GetString() == "Recorded");
        Assert.Equal(oldRevisionId, action.GetProperty("revisionId").GetGuid());
        Assert.Equal("2026-10-01", action.GetProperty("scheduledDate").GetString());
        Assert.Contains(rows, x => x.GetProperty("status").GetString() == "Estimated" && x.GetProperty("scheduledDate").GetString() == "2026-10-10");
    }

    [Fact]
    public async Task same_date_conflicting_action_returns_409_and_replay_is_idempotent()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var client = await factory.AuthenticatedClientAsync("recurring-occurrence-replay");
        var workspaceId = await WorkspaceId(factory, "recurring-occurrence-replay");
        var categoryId = await DefaultCategoryId(factory, workspaceId, "Expense");
        var ruleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await CreateRule(client, ruleId, categoryId, null)).StatusCode);
        var requestId = Guid.NewGuid();
        var skip = new { requestId, ruleVersion = 1, reason = "No corresponde" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/skip", skip)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/skip", skip)).StatusCode);
        var conflict = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/skip", new { requestId = Guid.NewGuid(), ruleVersion = 1, reason = "Otro motivo" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var differentAction = await client.PostAsJsonAsync($"/api/recurrences/{ruleId}/occurrences/2026-10-01/record", new
        {
            requestId = Guid.NewGuid(), ruleVersion = 1, actualDate = "2026-10-20", actualAmount = "1000.00",
            cashAccountId = (Guid?)null, preferredAccountId = (Guid?)null, actualCashAmount = "0.00", cardId = (Guid?)null,
            cardFirstPeriod = (string?)null, cardChargeAmount = "0.00", actualHealthcareComponents = Array.Empty<object>(),
            catalogOverride = (object?)null, clearPreferredAccount = true
        });
        Assert.Equal(HttpStatusCode.Conflict, differentAction.StatusCode);
        await using var db = Db(factory.TestConnection);
        Assert.Single(await db.RecurringOccurrenceActions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).ToArrayAsync());
    }

    private static async Task<HttpResponseMessage> CreateRule(HttpClient client, Guid ruleId, Guid categoryId, Guid? preferredAccountId)
    {
        return await client.PostAsJsonAsync("/api/recurrences", new
        {
            id = ruleId, flow = "Expense", description = "Servicio recurrente", categoryId, incomeSourceId = (Guid?)null,
            merchantId = (Guid?)null, amount = "1000.00", amountMode = "Fixed", currency = "ARS", frequency = "Monthly",
            interval = 1, startDate = "2026-10-01", endDate = (string?)null, dueDay = (int?)null, preferredAccountId,
            preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
        });
    }

    private static Task<HttpResponseMessage> CreateIncomeRule(HttpClient client, Guid ruleId, Guid categoryId, Guid sourceId, Guid? preferredAccountId) =>
        client.PostAsJsonAsync("/api/recurrences", new
        {
            id = ruleId, flow = "Income", description = "Ingreso mensual", categoryId, incomeSourceId = sourceId,
            merchantId = (Guid?)null, amount = "3000.00", amountMode = "Fixed", currency = "ARS", frequency = "Monthly",
            interval = 1, startDate = "2026-10-01", endDate = (string?)null, dueDay = (int?)null, preferredAccountId,
            preferredCardId = (Guid?)null, healthcareComponents = Array.Empty<object>()
        });

    private static async Task<Guid> CreateAccount(HttpClient client)
    {
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            id, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "5000.00", openingDate = "2026-01-01"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private static async Task<Guid> WorkspaceId(ApiFactory factory, string subject)
    {
        await using var db = Db(factory.TestConnection);
        return await db.Workspaces.Where(x => x.OwnerSubject == subject).Select(x => x.Id).SingleAsync();
    }

    private static async Task<Guid> DefaultCategoryId(ApiFactory factory, Guid workspaceId, string kind)
    {
        await using var db = Db(factory.TestConnection);
        return await db.FinanceCategories.Where(x => x.WorkspaceId == workspaceId && x.Kind == kind && x.IsDefault).Select(x => x.Id).SingleAsync();
    }

    private static AppDbContext Db(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
