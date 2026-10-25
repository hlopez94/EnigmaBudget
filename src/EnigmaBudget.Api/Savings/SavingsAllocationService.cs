using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Savings;

/// <summary>Single source for nominal cash backing, availability and signed free cash.</summary>
public sealed class SavingsAccountBalanceQuery(AppDbContext db, BusinessClock clock)
{
    public async Task<SavingsAccountBalanceDto[]> GetAsync(Guid workspaceId, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Id).ToArrayAsync(ct);
        var ledger = await db.LedgerEntries.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.Date <= clock.Today)
            .Join(db.Accounts.AsNoTracking().Where(a => a.WorkspaceId == workspaceId), e => e.AccountId, a => a.Id,
                (e, a) => new { Entry = e, Account = a }).Where(x => x.Entry.Date >= x.Account.OpeningDate)
            .GroupBy(x => x.Entry.AccountId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Entry.Amount) })
            .ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var reserved = await db.SavingsAllocations.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
            .GroupBy(x => x.AccountId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.ReservedAmount) })
            .ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        return accounts.Select(a =>
        {
            var cash = (a.OpeningDate <= clock.Today ? a.OpeningBalance : 0m) + (ledger.GetValueOrDefault(a.Id));
            var total = reserved.GetValueOrDefault(a.Id);
            var available = a.FundsAvailableFrom is null || a.FundsAvailableFrom <= clock.Today ? cash : 0m;
            var deficit = Math.Max(0m, total - cash);
            return new SavingsAccountBalanceDto(a.Id, a.Currency, F(cash), F(available), F(cash - available), F(total), F(deficit), F(available - total), a.IsArchived, D(a.FundsAvailableFrom));
        }).ToArray();
    }

    public async Task<bool> GoalHasDeficitAsync(Guid workspaceId, Guid goalId, CancellationToken ct)
    {
        var accountIds = await db.SavingsAllocations.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.GoalId == goalId)
            .Select(x => x.AccountId).Distinct().ToArrayAsync(ct);
        if (accountIds.Length == 0) return false;
        var balances = await GetAsync(workspaceId, ct);
        return balances.Where(x => accountIds.Contains(x.AccountId)).Any(x => decimal.Parse(x.BackingDeficit, CultureInfo.InvariantCulture) > 0m);
    }

    internal static string F(decimal x) => x.ToString("0.00", CultureInfo.InvariantCulture);
    internal static string? D(DateOnly? x) => x?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed class SavingsAllocationService(AppDbContext db, BusinessClock clock, WorkspaceMutationGate gate,
    WorkspaceAccessService access, SavingsAccountBalanceQuery balances)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string RecordFinance = WorkspacePermissions.RecordFinance;
    private const string CorrectHistory = WorkspacePermissions.CorrectHistory;
    private const string ManageResources = WorkspacePermissions.ManageResources;

    public Task<(SavingsAllocationDto? Result, int Status, string? Error)> AllocateAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, SavingsAllocationRequest r, CancellationToken ct)
    {
        if (!ValidId(r.Id) || !ValidId(r.GoalId) || !ValidId(r.AccountId) || Amount(r.Amount) is not > 0m) return Task.FromResult<(SavingsAllocationDto?, int, string?)>((null, 400, "Revisá la cuenta, la meta y el monto de la reserva."));
        return RunAsync(ctx, principal, RecordFinance, new[] { r.AccountId }, Array.Empty<Guid>(), async fresh =>
        {
            var old = await CommandAsync(ctx.Workspace.Id, r.Id, ct);
            var hash = Hash(new { kind = "Allocate", r });
            if (old is not null) return await ReplayAsync(ctx.Workspace.Id, old, "Allocate", hash, ct);
            var goal = await db.SavingsGoals.SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == r.GoalId, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == r.AccountId, ct);
            if (goal is null || account is null) return (null, 404, "La cuenta o la meta no existe en este espacio.");
            if (goal.State != "Active" || account.IsArchived || account.FundsAvailableFrom is { } date && date > clock.Today || account.OpeningDate > clock.Today)
                return (null, 409, "La meta o la cuenta no está disponible para una nueva reserva.");
            var amount = Amount(r.Amount)!.Value;
            var balance = (await balances.GetAsync(ctx.Workspace.Id, ct)).Single(x => x.AccountId == account.Id);
            if (decimal.Parse(balance.FreeBalance, Inv) < amount) return (null, 409, "El efectivo disponible no alcanza para esta reserva.");
            var allocation = await db.SavingsAllocations.SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.GoalId == goal.Id && x.AccountId == account.Id, ct);
            if (allocation is not null)
                await SavingsMutationLocks.LockAccountsThenAllocationsAsync(db, ctx.Workspace.Id, new[] { account.Id }, new[] { allocation.Id }, ct);
            goal = await SavingsMutationLocks.LockGoalAsync(db, ctx.Workspace.Id, goal.Id, ct);
            if (goal is null || goal.State != "Active") return (null, 409, "La meta no está disponible para una nueva reserva.");
            if (allocation is null) { allocation = new SavingsAllocation { Id = Guid.NewGuid(), WorkspaceId = ctx.Workspace.Id, GoalId = goal.Id, AccountId = account.Id, Currency = account.Currency }; db.SavingsAllocations.Add(allocation); }
            allocation.ReservedAmount += amount; allocation.Version++;
            AddCommand(ctx.Workspace.Id, r.Id, "Allocate", hash, allocation.Id);
            AddMovement(ctx.Workspace.Id, allocation.Id, r.Id, "Allocate", amount, fresh.User.Id);
            return await CommitDtoAsync(allocation, ct);
        }, ct);
    }

    public Task<(SavingsAllocationDto? Result, int Status, string? Error)> ReleaseAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, Guid allocationId, SavingsReleaseRequest r, CancellationToken ct)
    {
        if (!ValidId(r.Id) || Amount(r.Amount) is not > 0m) return Task.FromResult<(SavingsAllocationDto?, int, string?)>((null, 400, "El monto de retiro no es válido."));
        return ReleaseCoreAsync(ctx, principal, allocationId, r, ct);
    }

    private async Task<(SavingsAllocationDto? Result, int Status, string? Error)> ReleaseCoreAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, Guid allocationId, SavingsReleaseRequest r, CancellationToken ct)
    {
        var accountId = await db.SavingsAllocations.AsNoTracking().Where(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == allocationId).Select(x => (Guid?)x.AccountId).SingleOrDefaultAsync(ct);
        if (accountId is null) return (null, 404, "La reserva no existe en este espacio.");
        return await RunAsync(ctx, principal, RecordFinance, new[] { accountId.Value }, new[] { allocationId }, async fresh =>
        {
            var hash = Hash(new { kind = "Release", allocationId, r }); var old = await CommandAsync(ctx.Workspace.Id, r.Id, ct);
            if (old is not null) return await ReplayAsync(ctx.Workspace.Id, old, "Release", hash, ct);
            var a = await db.SavingsAllocations.SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == allocationId, ct);
            if (a is null) return (null, 404, "La reserva no existe en este espacio.");
            var goal = await SavingsMutationLocks.LockGoalAsync(db, ctx.Workspace.Id, a.GoalId, ct);
            if (goal is null || goal.State == "Closed") return (null, 409, "La meta está cerrada.");
            var amount = Amount(r.Amount)!.Value; if (amount > a.ReservedAmount) return (null, 409, "El monto supera la reserva disponible.");
            a.ReservedAmount -= amount; a.Version++; AddCommand(ctx.Workspace.Id, r.Id, "Release", hash, a.Id); AddMovement(ctx.Workspace.Id, a.Id, r.Id, "Release", amount, fresh.User.Id);
            return await CommitDtoAsync(a, ct);
        }, ct);
    }

    public Task<(SavingsAllocationDto? Result, int Status, string? Error)> ApplyAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, SavingsApplicationRequest r, CancellationToken ct)
    {
        if (!ValidId(r.Id) || !ValidId(r.AllocationId) || !ValidId(r.FinancePaymentId) || Amount(r.Amount) is not > 0m) return Task.FromResult<(SavingsAllocationDto?, int, string?)>((null, 400, "Revisá la reserva, el pago y el monto aplicado."));
        return ApplyCoreAsync(ctx, principal, r, ct);
    }

    private async Task<(SavingsAllocationDto? Result, int Status, string? Error)> ApplyCoreAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, SavingsApplicationRequest r, CancellationToken ct)
    {
        var allocationPre = await db.SavingsAllocations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == r.AllocationId, ct);
        if (allocationPre is null) return (null, 404, "La reserva no existe en este espacio.");
        return await RunAsync(ctx, principal, RecordFinance, new[] { allocationPre.AccountId }, new[] { r.AllocationId }, async fresh =>
        {
            var hash = Hash(new { kind = "Apply", r }); var old = await CommandAsync(ctx.Workspace.Id, r.Id, ct);
            if (old is not null) return await ReplayAsync(ctx.Workspace.Id, old, "Apply", hash, ct);
            var a = await db.SavingsAllocations.SingleAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == r.AllocationId, ct);
            var goal = await SavingsMutationLocks.LockGoalAsync(db, ctx.Workspace.Id, a.GoalId, ct);
            if (goal is null || goal.State == "Closed") return (null, 409, "La meta está cerrada.");
            var payment = await LockPaymentAsync(ctx.Workspace.Id, r.FinancePaymentId, ct);
            var op = payment is null ? null : await db.FinanceOperations.SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == payment.OperationId, ct);
            if (payment is null || op is null || op.Kind != "Expense" || op.IsVoided || payment.Date > clock.Today) return (null, 409, "Elegí un pago de un gasto vigente, confirmado y con fecha no futura.");
            var amount = Amount(r.Amount)!.Value;
            var priorApplications = await db.SavingsMovements.Where(x => x.WorkspaceId == ctx.Workspace.Id && x.FinancePaymentId == payment.Id && x.Kind == "Apply").SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            var priorReversals = await (from rev in db.SavingsMovements where rev.WorkspaceId == ctx.Workspace.Id && rev.Kind == "Reversal"
                join original in db.SavingsMovements on new { rev.WorkspaceId, Id = rev.ReversesMovementId!.Value } equals new { original.WorkspaceId, original.Id }
                where original.FinancePaymentId == payment.Id && original.Kind == "Apply" select rev.Amount).SumAsync(ct);
            if (a.AccountId != payment.AccountId || a.Currency != payment.Currency || amount > a.ReservedAmount || amount > payment.Amount - priorApplications + priorReversals)
                return (null, 409, "El pago y la reserva deben coincidir en cuenta y moneda y tener saldo suficiente.");
            a.ReservedAmount -= amount; a.Version++; AddCommand(ctx.Workspace.Id, r.Id, "Apply", hash, a.Id); AddMovement(ctx.Workspace.Id, a.Id, r.Id, "Apply", amount, fresh.User.Id, payment.Id);
            return await CommitDtoAsync(a, ct);
        }, ct);
    }

    public Task<(SavingsAllocationDto? Result, int Status, string? Error)> ReverseAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, Guid movementId, SavingsApplicationReverse r, CancellationToken ct)
    {
        if (!ValidId(r.RequestId) || !r.Confirm || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length > 240)
            return Task.FromResult<(SavingsAllocationDto?, int, string?)>((null, 400, "Confirmá la reversión e indicá un motivo de hasta 240 caracteres."));
        return ReverseCoreAsync(ctx, principal, movementId, r, ct);
    }

    private async Task<(SavingsAllocationDto? Result, int Status, string? Error)> ReverseCoreAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, Guid movementId, SavingsApplicationReverse r, CancellationToken ct)
    {
        var pre = await db.SavingsMovements.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == movementId, ct);
        if (pre is null) return (null, 404, "La aplicación no existe en este espacio.");
        return await RunAsync(ctx, principal, CorrectHistory, new[] { (await db.SavingsAllocations.AsNoTracking().SingleAsync(x => x.Id == pre.AllocationId, ct)).AccountId }, new[] { pre.AllocationId }, async fresh =>
        {
            var hash = Hash(new { kind = "ApplicationReverse", movementId, reason = r.Reason.Trim(), confirm = true }); var old = await CommandAsync(ctx.Workspace.Id, r.RequestId, ct);
            if (old is not null) return await ReplayAsync(ctx.Workspace.Id, old, "ApplicationReverse", hash, ct);
            var original = await db.SavingsMovements.SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == movementId && x.Kind == "Apply", ct);
            if (original is null) return (null, 409, "Solo se puede revertir una aplicación de reserva.");
            _ = await SavingsMutationLocks.LockGoalAsync(db, ctx.Workspace.Id, (await db.SavingsAllocations.SingleAsync(x => x.Id == original.AllocationId, ct)).GoalId, ct);
            if (original.FinancePaymentId is { } paymentId) _ = await LockPaymentAsync(ctx.Workspace.Id, paymentId, ct);
            if (await db.SavingsMovements.AnyAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.ReversesMovementId == movementId, ct)) return (null, 409, "La aplicación ya fue revertida.");
            var a = await db.SavingsAllocations.SingleAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == original.AllocationId, ct);
            a.ReservedAmount += original.Amount; a.Version++; AddCommand(ctx.Workspace.Id, r.RequestId, "ApplicationReverse", hash, a.Id);
            AddMovement(ctx.Workspace.Id, a.Id, r.RequestId, "Reversal", original.Amount, fresh.User.Id, original.FinancePaymentId, movementId, r.Reason.Trim());
            return await CommitDtoAsync(a, ct);
        }, ct);
    }

    public async Task<SavingsAccountBalanceDto[]> GetBalancesAsync(WorkspaceRequestContext context, CancellationToken ct) => await balances.GetAsync(context.Workspace.Id, ct);

    public Task<(SavingsAccountBalanceDto? Result, int Status, string? Error)> UpdateLiquidityAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, Guid accountId, SavingsLiquidityUpdate r, CancellationToken ct)
    {
        if (!ValidId(r.RequestId) || r.Version < 1 || (r.FundsAvailableFrom is not null && !DateOnly.TryParseExact(r.FundsAvailableFrom, "yyyy-MM-dd", Inv, DateTimeStyles.None, out _)))
            return Task.FromResult<(SavingsAccountBalanceDto?, int, string?)>((null, 400, "La fecha de disponibilidad no es válida."));
        return RunLiquidityAsync(ctx, principal, accountId, r, ct);
    }

    private async Task<(SavingsAccountBalanceDto? Result, int Status, string? Error)> RunLiquidityAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, Guid accountId, SavingsLiquidityUpdate r, CancellationToken ct)
    {
        var (lease, status) = await gate.AcquireAsync(ctx.Workspace.Id, ct); if (lease is null) return (null, status, "El espacio no está disponible.");
        await using (lease)
        {
            await SavingsMutationLocks.LockAccountsThenAllocationsAsync(db, ctx.Workspace.Id, new[] { accountId }, Array.Empty<Guid>(), ct);
            var (fresh, failure) = await access.ResolveWithinLockedWorkspaceAsync(principal, lease.Workspace, ManageResources, ct); if (fresh is null) return (null, failure, "No tenés permiso para cambiar la disponibilidad de esta cuenta.");
            var a = await db.Accounts.SingleOrDefaultAsync(x => x.WorkspaceId == ctx.Workspace.Id && x.Id == accountId, ct); if (a is null) return (null, 404, "La cuenta no existe en este espacio.");
            var hash = Hash(new { kind = "Liquidity", accountId, r }); var old = await CommandAsync(ctx.Workspace.Id, r.RequestId, ct);
            if (old is not null) return old.CommandKind == "Liquidity" && old.PayloadHash == hash ? ((await balances.GetAsync(ctx.Workspace.Id, ct)).SingleOrDefault(x => x.AccountId == accountId), 200, null) : (null, 409, "El identificador ya se usó con otros datos.");
            if (a.Version != r.Version) return (null, 409, "La cuenta cambió. Actualizá la pantalla antes de guardar.");
            a.FundsAvailableFrom = r.FundsAvailableFrom is null ? null : DateOnly.ParseExact(r.FundsAvailableFrom, "yyyy-MM-dd", Inv); a.Version++;
            AddCommand(ctx.Workspace.Id, r.RequestId, "Liquidity", hash, accountId); lease.MarkChanged(); await db.SaveChangesAsync(ct); await lease.CommitAsync(ct);
            return ((await balances.GetAsync(ctx.Workspace.Id, ct)).Single(x => x.AccountId == accountId), 200, null);
        }
    }

    private async Task<(SavingsAllocationDto? Result, int Status, string? Error)> RunAsync(WorkspaceRequestContext ctx, ClaimsPrincipal principal, string permission,
        Guid[] accountIds, Guid[] allocationIds, Func<WorkspaceRequestContext, Task<(SavingsAllocationDto? Result, int Status, string? Error)>> action, CancellationToken ct)
    {
        var (lease, status) = await gate.AcquireAsync(ctx.Workspace.Id, ct); if (lease is null) return (null, status, "El espacio no está disponible.");
        await using (lease)
        {
            await SavingsMutationLocks.LockAccountsThenAllocationsAsync(db, ctx.Workspace.Id, accountIds, allocationIds, ct);
            var (fresh, failure) = await access.ResolveWithinLockedWorkspaceAsync(principal, lease.Workspace, permission, ct);
            if (fresh is null) return (null, failure, "Ya no tenés permiso para registrar esta operación en el espacio.");
            var result = await action(fresh); if (result.Status != 200) return result;
            if (!db.ChangeTracker.HasChanges()) return result;
            lease.MarkChanged();
            try { await db.SaveChangesAsync(ct); await lease.CommitAsync(ct); return result; }
            catch (DbUpdateException) { return (null, 409, "La reserva cambió durante la operación. Actualizá e intentá de nuevo."); }
        }
    }

    private async Task<(SavingsAllocationDto? Result, int Status, string? Error)> CommitDtoAsync(SavingsAllocation a, CancellationToken ct) =>
        (new(a.Id, a.GoalId, a.AccountId, a.Currency, SavingsAccountBalanceQuery.F(a.ReservedAmount), a.Version), 200, null);

    private async Task<(SavingsAllocationDto? Result, int Status, string? Error)> ReplayAsync(Guid workspaceId, SavingsCommand old, string kind, string hash, CancellationToken ct)
    {
        if (old.CommandKind != kind || old.PayloadHash != hash) return (null, 409, "El identificador de solicitud ya se usó con otros datos.");
        var a = await db.SavingsAllocations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == old.ResultResourceId, ct);
        return a is null ? (null, 409, "La reserva ya no está disponible.") : (new(a.Id, a.GoalId, a.AccountId, a.Currency, SavingsAccountBalanceQuery.F(a.ReservedAmount), a.Version), 200, null);
    }
    private Task<SavingsCommand?> CommandAsync(Guid ws, Guid id, CancellationToken ct) => db.SavingsCommands.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == ws && x.RequestId == id, ct);
    private void AddCommand(Guid ws, Guid id, string kind, string hash, Guid result) => db.SavingsCommands.Add(new SavingsCommand { WorkspaceId = ws, RequestId = id, CommandKind = kind, PayloadHash = hash, ResultResourceId = result, OccurredAt = clock.UtcNow });
    private void AddMovement(Guid ws, Guid allocation, Guid command, string kind, decimal amount, Guid actor, Guid? payment = null, Guid? reverses = null, string? reason = null) =>
        db.SavingsMovements.Add(new SavingsMovement { Id = Guid.NewGuid(), WorkspaceId = ws, AllocationId = allocation, CommandId = command, Kind = kind, Currency = db.SavingsAllocations.Local.First(x => x.Id == allocation).Currency, Amount = amount, ActorUserId = actor, FinancePaymentId = payment, ReversesMovementId = reverses, Reason = reason, OccurredAt = clock.UtcNow });
    private async Task<FinancePayment?> LockPaymentAsync(Guid ws, Guid id, CancellationToken ct) => await db.FinancePayments.FromSqlInterpolated($"SELECT * FROM \"FinancePayments\" WHERE \"WorkspaceId\" = {ws} AND \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    private static decimal? Amount(string value) => value.Length <= 32 && !value.Contains(',') && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, Inv, out var n) && n > 0 && n <= 9999999999999999.99m && decimal.Round(n, 2) == n ? n : null;
    private static bool ValidId(Guid id) => id != Guid.Empty;
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    public static async Task ReverseApplicationsForPaymentAsync(AppDbContext db, Guid workspaceId, FinancePayment payment, Guid actorUserId, CancellationToken ct)
    {
        var applications = await db.SavingsMovements.Where(x => x.WorkspaceId == workspaceId && x.FinancePaymentId == payment.Id && x.Kind == "Apply")
            .OrderBy(x => x.AllocationId).ThenBy(x => x.Id).ToArrayAsync(ct);
        foreach (var application in applications)
        {
            if (await db.SavingsMovements.AnyAsync(x => x.WorkspaceId == workspaceId && x.ReversesMovementId == application.Id, ct)) continue;
            var allocation = await db.SavingsAllocations.SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == application.AllocationId, ct);
            allocation.ReservedAmount += application.Amount; allocation.Version++;
            var systemRequest = DeterministicGuid(application.Id);
            if (!await db.SavingsCommands.AnyAsync(x => x.WorkspaceId == workspaceId && x.RequestId == systemRequest, ct))
                db.SavingsCommands.Add(new SavingsCommand { WorkspaceId = workspaceId, RequestId = systemRequest, CommandKind = "PaymentVoidReverse", PayloadHash = Hash(new { application.Id, reason = "Anulación del pago" }), ResultResourceId = allocation.Id, OccurredAt = DateTimeOffset.UtcNow });
            db.SavingsMovements.Add(new SavingsMovement { Id = Guid.NewGuid(), WorkspaceId = workspaceId, AllocationId = allocation.Id, CommandId = systemRequest,
                Kind = "Reversal", Currency = allocation.Currency, Amount = application.Amount, FinancePaymentId = payment.Id, ReversesMovementId = application.Id,
                ActorUserId = actorUserId, Reason = "Compensación automática por anulación del pago", OccurredAt = DateTimeOffset.UtcNow });
        }
    }
    private static Guid DeterministicGuid(Guid input) { var bytes = SHA256.HashData(input.ToByteArray()); return new Guid(bytes.AsSpan(0, 16)); }
}
