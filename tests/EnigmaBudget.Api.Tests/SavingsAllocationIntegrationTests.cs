using System.Net;
using System.Net.Http.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Savings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnigmaBudget.Api.Tests;

public sealed class SavingsAllocationIntegrationTests
{
    private readonly ApiFactory _factory = new(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));

    [Fact]
    public async Task Reserving_cash_keeps_ledger_unchanged_and_replay_does_not_duplicate_movement()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01"
        })).StatusCode);
        var goalId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/goals", new
        {
            id = goalId, name = "Viaje", targetCurrency = "ARS", targetAmount = "1500.00", desiredDate = (string?)null,
            monthlyContribution = (string?)null, nextContributionDate = (string?)null
        })).StatusCode);

        var request = new { id = Guid.NewGuid(), goalId, accountId, amount = "600.00" };
        var first = await client.PostAsJsonAsync("/api/savings/allocations", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/allocations", request)).StatusCode);

        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        Assert.Equal(1000m, await db.OpeningBalances.Where(x => x.WorkspaceId == db.SavingsGoals.Where(g => g.Id == goalId).Select(g => g.WorkspaceId).Single()).SumAsync(x => x.Amount));
        var allocation = Assert.Single(await db.SavingsAllocations.Where(x => x.GoalId == goalId).ToArrayAsync());
        Assert.Equal(600m, allocation.ReservedAmount);
        var movement = Assert.Single(await db.SavingsMovements.Where(x => x.AllocationId == allocation.Id && x.Kind == "Allocate").ToArrayAsync());
        var workspace = await db.Workspaces.SingleAsync(x => x.Id == movement.WorkspaceId);
        Assert.Equal(workspace.OwnerUserId, movement.ActorUserId);
        Assert.Empty(await db.LedgerEntries.Where(x => x.WorkspaceId == movement.WorkspaceId).ToArrayAsync());
        Assert.Equal("1000.00", (await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances"))!.Single().GetProperty("cashBalance").GetString());
        Assert.Equal("400.00", (await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances"))!.Single().GetProperty("freeBalance").GetString());
    }

    [Fact]
    public async Task Database_rejects_unknown_savings_movement_kinds()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "100.00", openingDate = "2026-10-01" })).StatusCode);
        var goalId = await CreateGoalAsync(client); var allocationId = await ReserveAsync(client, goalId, accountId, "50.00");
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        var source = await db.SavingsMovements.SingleAsync(x => x.AllocationId == allocationId);
        var requestId = Guid.NewGuid();
        db.SavingsCommands.Add(new SavingsCommand { WorkspaceId = source.WorkspaceId, RequestId = requestId, CommandKind = "InvalidMovementTest", PayloadHash = new string('A', 64), ResultResourceId = allocationId, OccurredAt = DateTimeOffset.UtcNow });
        db.SavingsMovements.Add(new SavingsMovement { Id = Guid.NewGuid(), WorkspaceId = source.WorkspaceId, AllocationId = allocationId, CommandId = requestId, Kind = "Unknown", Currency = source.Currency, Amount = 1m, ActorUserId = source.ActorUserId, OccurredAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Reservations_cannot_exceed_nominal_cash_and_liquidity_date_blocks_new_reservations()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01" })).StatusCode);
        var firstGoal = await CreateGoalAsync(client);
        var secondGoal = await CreateGoalAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId = firstGoal, accountId, amount = "600.00" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId = secondGoal, accountId, amount = "500.00" })).StatusCode);
        var liquidity = await client.PutAsJsonAsync($"/api/savings/accounts/{accountId}/liquidity", new { requestId = Guid.NewGuid(), version = 1, fundsAvailableFrom = "2026-11-01" });
        Assert.Equal(HttpStatusCode.OK, liquidity.StatusCode);
        var balance = Assert.Single(await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances") ?? []);
        Assert.Equal("1000.00", balance.GetProperty("cashBalance").GetString());
        Assert.Equal("0.00", balance.GetProperty("availableCash").GetString());
        Assert.Equal("0.00", balance.GetProperty("backingDeficit").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId = secondGoal, accountId, amount = "1.00" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/savings/accounts/{accountId}/liquidity", new { requestId = Guid.NewGuid(), version = 2, fundsAvailableFrom = "2026-10-25" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId = secondGoal, accountId, amount = "400.00" })).StatusCode);
        balance = Assert.Single(await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances") ?? []);
        Assert.Equal("1000.00", balance.GetProperty("availableCash").GetString());
        Assert.Equal("0.00", balance.GetProperty("freeBalance").GetString());
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        Assert.Equal(1000m, await db.SavingsAllocations.Where(x => x.AccountId == accountId).SumAsync(x => x.ReservedAmount));
        Assert.Equal(2, await db.SavingsMovements.Where(x => x.Kind == "Allocate").CountAsync());
    }

    [Fact]
    public async Task Concurrent_reservations_serialize_before_spending_the_same_cash()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01" })).StatusCode);
        var goalA = await CreateGoalAsync(client); var goalB = await CreateGoalAsync(client);
        var one = client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId = goalA, accountId, amount = "600.00" });
        var two = client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId = goalB, accountId, amount = "600.00" });
        var responses = await Task.WhenAll(one, two);
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        Assert.Equal(600m, await db.SavingsAllocations.Where(x => x.AccountId == accountId).SumAsync(x => x.ReservedAmount));
        Assert.Single(await db.SavingsMovements.Where(x => x.Kind == "Allocate").ToArrayAsync());
    }

    [Fact]
    public async Task Confirmed_application_reversal_restores_only_reserve_once_and_records_reason()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01" })).StatusCode);
        var goalId = await CreateGoalAsync(client); await ReserveAsync(client, goalId, accountId, "600.00");
        var details = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/savings/goals/{goalId}/details");
        var allocationId = details!.GetProperty("allocations")[0].GetProperty("id").GetGuid();
        var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = operationId, kind = "Expense", accountId, date = "2026-10-20", description = "Compra", category = "Casa", amount = "100.00", paid = false })).StatusCode);
        var paymentId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/payments", new { id = paymentId, accountId, date = "2026-10-21", amount = "100.00" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = paymentId, amount = "100.00" })).StatusCode);
        var refreshed = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/savings/goals/{goalId}/details");
        var apply = refreshed!.GetProperty("movements").EnumerateArray().Single(x => x.GetProperty("financePaymentId").ValueKind == System.Text.Json.JsonValueKind.String && x.GetProperty("financePaymentId").GetGuid() == paymentId && x.GetProperty("kind").GetString() == "Apply");
        var applyId = apply.GetProperty("id").GetGuid();
        var endpoint = $"/api/savings/applications/{applyId}/reverse";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(endpoint, new { requestId = Guid.NewGuid(), reason = "Corrección", confirm = false })).StatusCode);
        var reversalRequest = new { requestId = Guid.NewGuid(), reason = "Pago aplicado por error", confirm = true };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(endpoint, reversalRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(endpoint, reversalRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(endpoint, new { requestId = Guid.NewGuid(), reason = "Otra razón", confirm = true })).StatusCode);
        var balance = Assert.Single(await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances") ?? []);
        Assert.Equal("900.00", balance.GetProperty("cashBalance").GetString());
        Assert.Equal("600.00", balance.GetProperty("totalReserved").GetString());
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        Assert.Equal(1, await db.LedgerEntries.Where(x => x.Source == "operation-payment" && x.SourceId == paymentId).CountAsync());
        var reversal = Assert.Single(await db.SavingsMovements.Where(x => x.ReversesMovementId == applyId).ToArrayAsync());
        Assert.Equal("Pago aplicado por error", reversal.Reason);
    }

    [Fact]
    public async Task Applications_reject_wrong_workspace_income_voided_future_and_currency_mismatched_payments()
    {
        using var client = await _factory.AuthenticatedClientAsync("payment-owner");
        using var other = await _factory.AuthenticatedClientAsync("payment-other-owner");
        var accountId = Guid.NewGuid(); var usdAccountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = usdAccountId, name = "Dólares", bankName = "Banco", currency = "USD", openingBalance = "100.00", openingDate = "2026-10-01" })).StatusCode);
        var goalId = await CreateGoalAsync(client); var allocationId = await ReserveAsync(client, goalId, accountId, "200.00");

        var incomeId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = incomeId, kind = "Income", accountId, date = "2026-10-20", description = "Ingreso", category = "Trabajo", amount = "20.00", paid = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = incomeId, amount = "1.00" })).StatusCode);

        var voidId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = voidId, kind = "Expense", accountId, date = "2026-10-20", description = "Anulado", category = "Casa", amount = "20.00", paid = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{voidId}/void", new { id = Guid.NewGuid(), version = 1, reason = "Anulado" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = voidId, amount = "1.00" })).StatusCode);

        var usdExpenseId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = usdExpenseId, kind = "Expense", accountId = usdAccountId, date = "2026-10-20", description = "Compra USD", category = "Casa", amount = "20.00", paid = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = usdExpenseId, amount = "1.00" })).StatusCode);

        var otherAccountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/api/accounts", new { id = otherAccountId, name = "Otra caja", bankName = "Banco", currency = "ARS", openingBalance = "100.00", openingDate = "2026-10-01" })).StatusCode);
        var otherExpenseId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/api/operations", new { id = otherExpenseId, kind = "Expense", accountId = otherAccountId, date = "2026-10-20", description = "Ajeno", category = "Casa", amount = "20.00", paid = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = otherExpenseId, amount = "1.00" })).StatusCode);

        var limitedPaymentId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = limitedPaymentId, kind = "Expense", accountId, date = "2026-10-20", description = "Pago limitado", category = "Casa", amount = "50.00", paid = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = limitedPaymentId, amount = "50.00" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = limitedPaymentId, amount = "1.00" })).StatusCode);

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        var workspaceId = await db.SavingsGoals.Where(x => x.Id == goalId).Select(x => x.WorkspaceId).SingleAsync();
        var futureId = Guid.NewGuid();
        db.FinanceOperations.Add(new FinanceOperation { Id = futureId, WorkspaceId = workspaceId, Kind = "Expense", AccountId = accountId, Date = new DateOnly(2026, 10, 30), Description = "Futuro", Category = "Casa", Currency = "ARS", Amount = 10m });
        db.FinancePayments.Add(new FinancePayment { Id = futureId, WorkspaceId = workspaceId, OperationId = futureId, AccountId = accountId, Date = new DateOnly(2026, 10, 30), Amount = 10m, Currency = "ARS" });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/applications", new { id = Guid.NewGuid(), allocationId, financePaymentId = futureId, amount = "1.00" })).StatusCode);
    }

    [Fact]
    public async Task Queued_http_reservation_rechecks_membership_after_workspace_lock_wait()
    {
        const string subject = "queued-reserve-revoked";
        using var client = await _factory.AuthenticatedClientAsync(subject);
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01" })).StatusCode);
        var goalId = await CreateGoalAsync(client);
        Guid workspaceId; Guid userId;
        await using (var workspaceDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options))
        {
            workspaceId = await workspaceDb.SavingsGoals.Where(x => x.Id == goalId).Select(x => x.WorkspaceId).SingleAsync();
            userId = await workspaceDb.Workspaces.Where(x => x.Id == workspaceId).Select(x => x.OwnerUserId).SingleAsync();
        }
        _factory.WorkspaceLockBarrier.Arm();
        var reserveRequest = client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId, accountId, amount = "600.00" });
        try
        {
            await _factory.WorkspaceLockBarrier.WaitUntilBlockedAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await using var revokeDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
            var membership = await revokeDb.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId);
            membership.Active = false; membership.Version++;
            await revokeDb.SaveChangesAsync();
        }
        finally { _factory.WorkspaceLockBarrier.Release(); }
        var response = await reserveRequest;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        Assert.Empty(await verify.SavingsMovements.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
        Assert.Empty(await verify.SavingsAllocations.Where(x => x.WorkspaceId == workspaceId).ToArrayAsync());
    }

    [Fact]
    public async Task Voiding_applied_expense_restores_reserve_and_cash_once()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01" })).StatusCode);
        var goalId = await CreateGoalAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId, accountId, amount = "600.00" })).StatusCode);
        var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = operationId, kind = "Expense", accountId, date = "2026-10-20", description = "Compra", category = "Comida", amount = "100.00", paid = false })).StatusCode);
        var paymentId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/payments", new { id = paymentId, accountId, date = "2026-10-21", amount = "100.00" })).StatusCode);
        var allocations = await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances");
        Assert.Equal("900.00", Assert.Single(allocations!).GetProperty("cashBalance").GetString());
        var allocationId = await AllocationIdAsync(goalId);
        var applicationId = Guid.NewGuid();
        var application = new { id = applicationId, allocationId, financePaymentId = paymentId, amount = "100.00" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/applications", application)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/applications", application)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/applications", application with { amount = "50.00" })).StatusCode);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        var apply = Assert.Single(await db.SavingsMovements.Where(x => x.FinancePaymentId == paymentId && x.Kind == "Apply").ToArrayAsync());
        Assert.Equal("400.00", (await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances"))!.Single().GetProperty("freeBalance").GetString());
        var voidRequest = new { id = Guid.NewGuid(), version = 2, reason = "Compra anulada" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/void", voidRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/void", voidRequest)).StatusCode);
        var balance = Assert.Single(await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/balances") ?? []);
        Assert.Equal("1000.00", balance.GetProperty("cashBalance").GetString());
        Assert.Equal("600.00", balance.GetProperty("totalReserved").GetString());
        Assert.Equal(1, await db.SavingsMovements.Where(x => x.ReversesMovementId == apply.Id).CountAsync());
        Assert.Equal(1, await db.LedgerEntries.Where(x => x.Source == "operation-void" && x.SourceId == paymentId).CountAsync());
        Assert.Equal("Compensación automática por anulación del pago", (await db.SavingsMovements.SingleAsync(x => x.ReversesMovementId == apply.Id)).Reason);
    }

    [Fact]
    public async Task Under_backed_goal_hides_estimates_until_reserve_is_released()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "1000.00", openingDate = "2026-10-01" })).StatusCode);
        var goalId = await CreateGoalAsync(client);
        await ReserveAsync(client, goalId, accountId, "600.00");
        var reloaded = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/savings/goals/{goalId}/details");
        var allocationId = reloaded!.GetProperty("allocations")[0].GetProperty("id").GetGuid();
        var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = operationId, kind = "Expense", accountId, date = "2026-10-20", description = "Gasto", category = "Casa", amount = "1100.00", paid = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/operations/{operationId}/payments", new { id = Guid.NewGuid(), accountId, date = "2026-10-21", amount = "1000.00" })).StatusCode);
        var goal = Assert.Single(await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/goals") ?? []);
        Assert.Equal("BackingDeficit", goal.GetProperty("estimateStatus").GetString());
        Assert.False(goal.GetProperty("reached").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, goal.GetProperty("savedAmount").ValueKind);
        Assert.Equal("600.00", goal.GetProperty("backingAmounts")[0].GetProperty("amount").GetString());
        var secondGoal = await CreateGoalAsync(client);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId = secondGoal, accountId, amount = "1.00" })).StatusCode);
        var release = await client.PostAsJsonAsync($"/api/savings/allocations/{allocationId}/release", new { id = Guid.NewGuid(), amount = "600.00" });
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        goal = (await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/savings/goals") ?? []).Single(x => x.GetProperty("id").GetGuid() == goalId);
        Assert.NotEqual("BackingDeficit", goal.GetProperty("estimateStatus").GetString());
    }

    private async Task<Guid> CreateGoalAsync(HttpClient client)
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/savings/goals", new { id, name = "Meta", targetCurrency = "ARS", targetAmount = "1500.00", desiredDate = (string?)null, monthlyContribution = "75.00", nextContributionDate = "2026-11-10" })).StatusCode);
        return id;
    }

    private async Task<Guid> ReserveAsync(HttpClient client, Guid goalId, Guid accountId, string amount)
    {
        var response = await client.PostAsJsonAsync("/api/savings/allocations", new { id = Guid.NewGuid(), goalId, accountId, amount });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        return await db.SavingsAllocations.Where(x => x.GoalId == goalId).Select(x => x.Id).SingleAsync();
    }

    private async Task<Guid> AllocationIdAsync(Guid goalId)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options);
        return await db.SavingsAllocations.Where(x => x.GoalId == goalId).Select(x => x.Id).SingleAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
