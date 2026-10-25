using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnigmaBudget.Api.Tests;

public sealed class MonthlyBudgetIntegrationTests
{
    [Fact]
    public async Task budget_requires_expense_category_and_valid_currency()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("budget-category-validation");
        var income = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = income, name = "Sueldo", kind = "Income" })).StatusCode);

        using var wrongKind = await client.PostAsJsonAsync("/api/budgets", new { id = Guid.NewGuid(), period = "2026-10", categoryId = income, currency = "ARS", limitAmount = "0.00" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
        using var badCurrency = await client.PostAsJsonAsync("/api/budgets", new { id = Guid.NewGuid(), period = "2026-10", categoryId = Guid.NewGuid(), currency = "ars", limitAmount = "0.00" });
        Assert.Equal(HttpStatusCode.BadRequest, badCurrency.StatusCode);
        using var badPeriod = await client.PostAsJsonAsync("/api/budgets", new { id = Guid.NewGuid(), period = "2026- 1", categoryId = Guid.NewGuid(), currency = "ARS", limitAmount = "0.00" });
        Assert.Equal(HttpStatusCode.BadRequest, badPeriod.StatusCode);
    }

    [Fact]
    public async Task monthly_budget_counts_cash_expense_and_card_installment_once()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("budget-source-aggregation");
        var workspaceId = await WorkspaceIdAsync(factory, "budget-source-aggregation");
        var category = Guid.NewGuid();
        var account = Guid.NewGuid();
        var card = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = category, name = "Comida", kind = "Expense" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = account, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "0.00", openingDate = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cards", new { id = card, name = "Visa", bankName = "Banco", statementDebtArs = "0.00", statementDebtUsd = "0.00", asOfDate = "2026-10-01" })).StatusCode);
        var cashId = Guid.NewGuid();
        using var expense = await client.PostAsJsonAsync("/api/operations", new { id = cashId, kind = "Expense", accountId = account, date = "2026-10-10", description = "Verdulería", category = "Comida", categoryId = category, amount = "30.00", paid = false });
        Assert.Equal(HttpStatusCode.OK, expense.StatusCode);
        using var partialPayment = await client.PostAsJsonAsync($"/api/operations/{cashId}/payments", new { id = Guid.NewGuid(), accountId = account, date = "2026-10-12", amount = "10.00" });
        Assert.Equal(HttpStatusCode.OK, partialPayment.StatusCode);
        var purchaseId = Guid.NewGuid();
        using var purchase = await client.PostAsJsonAsync("/api/purchases", new { id = purchaseId, cardId = card, date = "2026-10-11", description = "Supermercado", category = "Comida", categoryId = category, currency = "ARS", totalAmount = "80.00", installmentCount = 2, firstPeriod = "2026-10", groupId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        var statementId = Guid.NewGuid();
        using var savedStatement = await client.PutAsJsonAsync($"/api/cards/{card}/statement", new { id = statementId, period = "2026-10", currency = "ARS", version = 0, dueDate = (string?)null, actualTotal = "47.00", adjustments = new[] { new { id = Guid.NewGuid(), description = "Interés", category = "Comida", categoryId = category, amount = "10.00" }, new { id = Guid.NewGuid(), description = "Bonificación", category = "Comida", categoryId = category, amount = "-3.00" } } });
        Assert.Equal(HttpStatusCode.OK, savedStatement.StatusCode);
        using var statementJson = JsonDocument.Parse(await savedStatement.Content.ReadAsStringAsync());
        var statementVersion = statementJson.RootElement.GetProperty("version").GetInt32();
        using var confirmed = await client.PostAsJsonAsync($"/api/cards/{card}/statement/confirm", new { id = statementId, version = statementVersion });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        using var cardPayment = await client.PostAsJsonAsync($"/api/cards/{card}/payments", new { id = Guid.NewGuid(), accountId = account, date = paymentDate, amount = "20.00", currency = "ARS", appliedAmount = "20.00", statementId });
        Assert.Equal(HttpStatusCode.OK, cardPayment.StatusCode);
        var budgetId = Guid.NewGuid();
        using var budget = await client.PostAsJsonAsync("/api/budgets", new { id = budgetId, period = "2026-10", categoryId = category, currency = "ARS", limitAmount = "100.00" });
        Assert.Equal(HttpStatusCode.Created, budget.StatusCode);

        using var response = await client.GetAsync("/api/budgets?period=2026-10&currency=ARS");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = json.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == category);
        Assert.Equal("30.00", row.GetProperty("spentAmount").GetString());
        Assert.Equal("47.00", row.GetProperty("committedCardAmount").GetString());
        Assert.Equal("23.00", row.GetProperty("remainingAmount").GetString());
        var sources = row.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(4, sources.Length);
        Assert.Contains(sources, x => x.GetProperty("sourceKind").GetString() == "Operation" && x.GetProperty("sourceId").GetGuid() == cashId);
        Assert.Contains(sources, x => x.GetProperty("sourceKind").GetString() == "Installment" && x.GetProperty("sourceId").GetGuid() != purchaseId);
        Assert.Contains(sources, x => x.GetProperty("sourceKind").GetString() == "Installment" && x.GetProperty("description").GetString() == "Supermercado · Cuota 1/2");
        Assert.Contains(sources, x => x.GetProperty("sourceKind").GetString() == "StatementAdjustment" && x.GetProperty("sourceId").GetGuid() != Guid.Empty && x.GetProperty("amount").GetString() == "10.00");
        Assert.Contains(sources, x => x.GetProperty("sourceKind").GetString() == "StatementAdjustment" && x.GetProperty("amount").GetString() == "-3.00");
        Assert.Equal(workspaceId, await WorkspaceIdAsync(factory, "budget-source-aggregation"));
    }

    [Fact]
    public async Task budget_create_update_are_durable_and_versioned_and_zero_is_configured()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("budget-versioned-write");
        var workspaceId = await WorkspaceIdAsync(factory, "budget-versioned-write");
        var category = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = category, name = "Hogar", kind = "Expense" })).StatusCode);
        var budgetId = Guid.NewGuid();
        var create = new { id = budgetId, period = "2026-10", categoryId = category, currency = "USD", limitAmount = "0.00" };
        using var created = await client.PostAsJsonAsync("/api/budgets", create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(budgetId, createdJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("0.00", createdJson.RootElement.GetProperty("limitAmount").GetString());
        using var beforeUpdate = await client.GetAsync("/api/budgets?period=2026-10&currency=USD");
        using var beforeUpdateJson = JsonDocument.Parse(await beforeUpdate.Content.ReadAsStringAsync());
        var configuredZero = beforeUpdateJson.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == category);
        Assert.True(configuredZero.GetProperty("isConfigured").GetBoolean());
        Assert.Equal("0.00", configuredZero.GetProperty("limitAmount").GetString());
        using var archive = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/finance/catalog/categories/{category}") { Content = JsonContent.Create(new { requestId = Guid.NewGuid(), version = 1 }) });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var replay = await client.PostAsJsonAsync("/api/budgets", create);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var changedReplay = await client.PostAsJsonAsync("/api/budgets", new { id = budgetId, period = "2026-10", categoryId = category, currency = "USD", limitAmount = "1.00" });
        Assert.Equal(HttpStatusCode.Conflict, changedReplay.StatusCode);
        using var archivedNewBudget = await client.PostAsJsonAsync("/api/budgets", new { id = Guid.NewGuid(), period = "2026-11", categoryId = category, currency = "USD", limitAmount = "10.00" });
        Assert.Equal(HttpStatusCode.BadRequest, archivedNewBudget.StatusCode);
        var update = new { requestId = Guid.NewGuid(), version = 1, limitAmount = "25.00" };
        using var updated = await client.PutAsJsonAsync($"/api/budgets/{budgetId}", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var stale = await client.PutAsJsonAsync($"/api/budgets/{budgetId}", new { requestId = Guid.NewGuid(), version = 1, limitAmount = "30.00" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var read = await client.GetAsync("/api/budgets?period=2026-10&currency=USD");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var row = readJson.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == category);
        Assert.Equal(budgetId, row.GetProperty("budgetId").GetGuid());
        Assert.Equal(2, row.GetProperty("version").GetInt32());
        Assert.Equal("25.00", row.GetProperty("limitAmount").GetString());
        Assert.True(row.GetProperty("isConfigured").GetBoolean());
        Assert.Equal(workspaceId, await WorkspaceIdAsync(factory, "budget-versioned-write"));
    }

    [Fact]
    public async Task budget_write_requires_manage_resources_and_reads_are_workspace_scoped()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var owner = await factory.AuthenticatedClientAsync("budget-reader-owner");
        using var reader = await factory.AuthenticatedClientAsync("budget-reader-member");
        using var foreign = await factory.AuthenticatedClientAsync("budget-reader-foreign");
        var workspaceId = await WorkspaceIdAsync(factory, "budget-reader-owner");
        var category = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/finance/catalog/categories", new { id = category, name = "Comida", kind = "Expense" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "budget-reader-member");
            db.WorkspaceMemberships.Add(new WorkspaceMembership { WorkspaceId = workspaceId, UserId = user.Id, Role = "Reader", JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        reader.DefaultRequestHeaders.Add("X-Workspace-Id", workspaceId.ToString());
        using var denied = await reader.PostAsJsonAsync("/api/budgets", new { id = Guid.NewGuid(), period = "2026-10", categoryId = category, currency = "ARS", limitAmount = "50.00" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var ownRead = await owner.GetAsync("/api/budgets?period=2026-10&currency=ARS");
        using var foreignRead = await foreign.GetAsync("/api/budgets?period=2026-10&currency=ARS");
        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, foreignRead.StatusCode);
        using var foreignJson = JsonDocument.Parse(await foreignRead.Content.ReadAsStringAsync());
        Assert.DoesNotContain(foreignJson.RootElement.EnumerateArray(), x => x.GetProperty("categoryId").GetGuid() == category);
    }

    [Fact]
    public async Task budget_write_rechecks_manage_permission_after_workspace_gate()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var owner = await factory.AuthenticatedClientAsync("budget-gate-owner");
        using var administrator = await factory.AuthenticatedClientAsync("budget-gate-admin");
        var workspaceId = await WorkspaceIdAsync(factory, "budget-gate-owner");
        var category = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/finance/catalog/categories", new { id = category, name = "Hogar", kind = "Expense" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "budget-gate-admin");
            db.WorkspaceMemberships.Add(new WorkspaceMembership { WorkspaceId = workspaceId, UserId = user.Id, Role = "Admin", JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        administrator.DefaultRequestHeaders.Add("X-Workspace-Id", workspaceId.ToString());
        var budgetId = Guid.NewGuid();
        factory.WorkspaceLockBarrier.Arm();
        var request = administrator.PostAsJsonAsync("/api/budgets", new { id = budgetId, period = "2026-10", categoryId = category, currency = "ARS", limitAmount = "25.00" });
        await factory.WorkspaceLockBarrier.WaitUntilBlockedAsync().WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "budget-gate-admin");
            var membership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspaceId && x.UserId == user.Id);
            membership.Role = "Reader";
            await db.SaveChangesAsync();
        }
        finally { factory.WorkspaceLockBarrier.Release(); }
        using var response = await request;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.MonthlyBudgets.AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == budgetId));
        Assert.False(await verifyDb.BudgetCommands.AnyAsync(x => x.WorkspaceId == workspaceId && x.RequestId == budgetId));
    }

    [Fact]
    public async Task overview_operation_expenses_alias_equals_legacy_value()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("budget-overview-alias");
        var account = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = account, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "0.00", openingDate = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = Guid.NewGuid(), kind = "Expense", accountId = account, date = "2026-10-10", description = "Gasto", category = "", amount = "12.34", paid = false })).StatusCode);
        using var response = await client.GetAsync("/api/finance?period=2026-10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var budget = json.RootElement.GetProperty("budget");
        Assert.Equal(budget.GetProperty("cashExpensesArs").GetString(), budget.GetProperty("operationExpensesArs").GetString());
        Assert.Equal(budget.GetProperty("cashExpensesUsd").GetString(), budget.GetProperty("operationExpensesUsd").GetString());
    }

    [Fact]
    public async Task budget_period_is_exact_and_duplicate_category_key_conflicts()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("budget-period-key");
        var category = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = category, name = "Hogar", kind = "Expense" })).StatusCode);
        using var invalidRead = await client.GetAsync("/api/budgets?period=2026- 1&currency=ARS");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRead.StatusCode);
        using var validEdge = await client.GetAsync("/api/budgets?period=9999-12&currency=ARS");
        Assert.Equal(HttpStatusCode.OK, validEdge.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/budgets", new { id = Guid.NewGuid(), period = "2026-10", categoryId = category, currency = "ARS", limitAmount = "12.00" })).StatusCode);
        using var duplicate = await client.PostAsJsonAsync("/api/budgets", new { id = Guid.NewGuid(), period = "2026-10", categoryId = category, currency = "ARS", limitAmount = "15.00" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task uncategorized_expense_remains_visible_and_ars_usd_are_never_combined()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("budget-currency-default");
        var arsAccount = Guid.NewGuid();
        var usdAccount = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = arsAccount, name = "Caja ARS", bankName = "Banco", currency = "ARS", openingBalance = "0.00", openingDate = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = usdAccount, name = "Caja USD", bankName = "Banco", currency = "USD", openingBalance = "0.00", openingDate = "2026-10-01" })).StatusCode);
        var arsId = Guid.NewGuid();
        var usdId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = arsId, kind = "Expense", accountId = arsAccount, date = "2026-10-10", description = "Sin categoría ARS", category = "", amount = "13.00", paid = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = usdId, kind = "Expense", accountId = usdAccount, date = "2026-10-11", description = "Sin categoría USD", category = "", amount = "27.00", paid = false })).StatusCode);
        using var catalog = await client.GetAsync("/api/finance/catalog");
        using var catalogJson = JsonDocument.Parse(await catalog.Content.ReadAsStringAsync());
        var defaultCategory = catalogJson.RootElement.GetProperty("categories").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "Expense" && x.GetProperty("isDefault").GetBoolean()).GetProperty("itemId").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FinanceOperations.Single(x => x.Id == arsId).CategoryId = null;
            db.FinanceOperations.Single(x => x.Id == usdId).CategoryId = null;
            await db.SaveChangesAsync();
        }
        using var ars = await client.GetAsync("/api/budgets?period=2026-10&currency=ARS");
        using var usd = await client.GetAsync("/api/budgets?period=2026-10&currency=USD");
        using var arsJson = JsonDocument.Parse(await ars.Content.ReadAsStringAsync());
        using var usdJson = JsonDocument.Parse(await usd.Content.ReadAsStringAsync());
        var arsRow = arsJson.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == defaultCategory);
        var usdRow = usdJson.RootElement.EnumerateArray().Single(x => x.GetProperty("categoryId").GetGuid() == defaultCategory);
        Assert.Equal("13.00", arsRow.GetProperty("spentAmount").GetString());
        Assert.Equal("27.00", usdRow.GetProperty("spentAmount").GetString());
        Assert.Contains(arsRow.GetProperty("sources").EnumerateArray(), x => x.GetProperty("sourceId").GetGuid() == arsId);
        Assert.Contains(usdRow.GetProperty("sources").EnumerateArray(), x => x.GetProperty("sourceId").GetGuid() == usdId);
        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.All(await verifyDb.FinanceOperations.Where(x => x.Id == arsId || x.Id == usdId).Select(x => x.CategoryId).ToArrayAsync(), x => Assert.Null(x));
    }

    private static async Task MigrateAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
    private static async Task<Guid> WorkspaceIdAsync(ApiFactory factory, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == subject);
        return await db.Workspaces.Where(x => x.OwnerUserId == user.Id && x.Kind == "Personal").Select(x => x.Id).SingleAsync();
    }
}
