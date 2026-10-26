using System.Data;
using System.Globalization;
using System.Security.Claims;
using EnigmaBudget.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.Workspaces;

namespace EnigmaBudget.Api.Finance;

public static partial class FinanceEndpoints
{
    private static async Task<IResult> CreateOperation(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        WorkspaceAccessService access,
        WorkspaceMutationGate mutationGate,
        OperationCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            r.Kind is not ("Income" or "Expense")
            || !MoneyRules.Money(r.Amount, false, out var amount)
            || amount <= 0
            || string.IsNullOrWhiteSpace(r.Description)
            || r.Description.Trim().Length > 240
            || (r.Category?.Length ?? 0) > 80
            || !DateOnly.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
        )
            return Problem(400, "Revisá los datos del movimiento.");
        var initial = FinanceWorkspaceFilter.Get(http)!;
        WorkspaceMutationLease? lease;
        int lockStatus;
        try
        {
            (lease, lockStatus) = await mutationGate.AcquireAsync(initial.Workspace.Id, http.RequestAborted);
        }
        catch (Exception ex) when (IsPersistenceConflict(ex)) { return Problem(409, "El espacio cambió durante el movimiento."); }
        if (lease is null) return Problem(lockStatus, "El espacio no existe.");
        await using var mutation = lease;
        var (context, authorizationStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.RecordFinance, http.RequestAborted);
        if (context is null) return Problem(authorizationStatus, "No tenés permiso para registrar movimientos.");
        var ws = mutation.Workspace;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == r.AccountId, http.RequestAborted);
        if (account is null) return Results.NotFound();
        var selectionHash = CatalogSelectionResolver.SelectionHash(r.CategoryId, r.MerchantId, r.IncomeSourceId);
        var prior = await db.FinanceOperations.Include(x => x.Payments).SingleOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == r.Id, http.RequestAborted);
        if (prior is not null)
        {
            var categoryMatches = r.CategoryId.HasValue || prior.Category == (r.Category?.Trim() ?? "");
            return prior.Amount == amount && prior.AccountId == r.AccountId && prior.Kind == r.Kind
                && prior.Date == date && prior.Description == r.Description.Trim()
                && prior.InitiallyPaid == r.Paid && categoryMatches
                && prior.CatalogSelectionHash == selectionHash
                ? Results.Ok(await Operation(db, prior))
                : Problem(409, "El identificador ya se usó con otros datos.");
        }
        if (account.IsArchived) return Problem(409, "La cuenta está archivada.");
        if (r.Paid && (date > clock.Today || date < account.OpeningDate))
            return Problem(400, "La fecha de un movimiento confirmado debe estar entre el alta de la cuenta y hoy.");
        var resolved = await CatalogSelectionResolver.ResolveOperationAsync(db, ws.Id, r.Kind, r.Category, r.CategoryId, r.MerchantId, r.IncomeSourceId, http.RequestAborted);
        if (resolved.FailureStatus != 0) return Problem(resolved.FailureStatus, resolved.FailureDetail!);
        var op = new FinanceOperation
        {
            Id = r.Id,
            WorkspaceId = ws.Id,
            Kind = r.Kind,
            AccountId = r.AccountId,
            Date = date,
            Description = r.Description.Trim(),
            Category = resolved.CategoryName,
            CategoryId = resolved.CategoryId,
            MerchantId = resolved.MerchantId,
            MerchantNameSnapshot = resolved.MerchantName,
            IncomeSourceId = resolved.IncomeSourceId,
            IncomeSourceNameSnapshot = resolved.IncomeSourceName,
            CatalogSelectionHash = resolved.SelectionHash,
            Currency = account.Currency,
            Amount = amount,
            InitiallyPaid = r.Paid,
        };
        FinanceRecordWriter.AddOperation(db, op);
        if (r.Paid)
        {
            FinanceRecordWriter.AddPayment(db, op, account, date, amount, r.Id);
        }
        try
        {
            mutation.MarkChanged();
            await db.SaveChangesAsync(http.RequestAborted);
            await mutation.CommitAsync(http.RequestAborted);
        }
        catch (Exception ex) when (IsPersistenceConflict(ex)) { return Problem(409, "El espacio cambió durante el movimiento."); }
        return Results.Ok(await Operation(db, op));
    }

    private static async Task<IResult> Operations(
        HttpContext http,
        AppDbContext db,
        string? period = null,
        Guid? accountId = null
    )
    {
        var ws = await Workspace(db, http);
        var query = db.FinanceOperations.Where(x => x.WorkspaceId == ws.Id);
        if (accountId.HasValue)
            query = query.Where(x => x.AccountId == accountId);
        if (!string.IsNullOrEmpty(period))
        {
            var y = int.Parse(period[..4]);
            var m = int.Parse(period[5..7]);
            query = query.Where(x => x.Date.Year == y && x.Date.Month == m);
        }
        var rows = await query.OrderByDescending(x => x.Date).ToArrayAsync();
        var paymentRows = await db
            .FinancePayments.Where(x =>
                x.WorkspaceId == ws.Id && rows.Select(o => o.Id).Contains(x.OperationId)
            )
            .ToArrayAsync();
        var settlementRows = await db.NonCashSettlements.Where(x => x.WorkspaceId == ws.Id && rows.Select(o => o.Id).Contains(x.OperationId)).ToArrayAsync();
        var output = rows.Select(x =>
            {
                var ps = paymentRows.Where(p => p.OperationId == x.Id).ToArray();
                return OperationSettlementMapping.Map(x, ps, settlementRows.Where(s => s.OperationId == x.Id).ToArray());
            })
            .ToArray();
        return Results.Ok(output);
    }

    private static async Task<IResult> PayOperation(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        WorkspaceAccessService access,
        WorkspaceMutationGate mutationGate,
        Guid id,
        PaymentCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !MoneyRules.Money(r.Amount, false, out var amount)
            || amount <= 0
            || !DateOnly.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
            || date > clock.Today
        )
            return Problem(400, "Revisá el importe y la fecha del pago.");
        if (r.OperationVersion < 1 || r.Id == Guid.Empty) return Problem(400, "La versión del movimiento no es válida.");
        var initial = FinanceWorkspaceFilter.Get(http)!;
        var (lease, lockStatus) = await mutationGate.AcquireAsync(initial.Workspace.Id, http.RequestAborted);
        if (lease is null) return Problem(lockStatus, "El espacio no está disponible.");
        await using var mutation = lease;
        var (context, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.RecordFinance, http.RequestAborted);
        if (context is null) return Problem(authStatus, "No tenés permiso para registrar movimientos.");
        var ws = mutation.Workspace;
        var op = await OperationSettlementService.LockOperationAsync(db, ws.Id, id, http.RequestAborted);
        if (op is null)
            return Results.NotFound();
        var nonCash = await OperationSettlementService.LockSettlementsAsync(db, ws.Id, id, http.RequestAborted);
        var payments = await db.FinancePayments.Where(x => x.WorkspaceId == ws.Id && x.OperationId == id).ToArrayAsync(http.RequestAborted);
        var hash = OperationSettlementMapping.Hash(new { r.Id, r.AccountId, Date = date.ToString("yyyy-MM-dd", Invariant), Amount = F(amount), r.OperationVersion });
        var replay = await db.FinancePayments.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (replay is not null)
            return replay.OperationId == id && (replay.CommandPayloadHash is not null
                ? replay.CommandPayloadHash == hash
                : replay.Amount == amount && replay.AccountId == r.AccountId && replay.Date == date)
                ? Results.Ok(OperationSettlementMapping.Map(op, payments, nonCash)) : Problem(409, "El identificador ya se usó con otros datos.");
        if (op.IsVoided) return Problem(409, "El movimiento está anulado.");
        if (op.Version != r.OperationVersion) return Problem(409, "El movimiento cambió. Actualizá la pantalla.");
        var account = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.AccountId
        );
        if (account is null)
            return Results.NotFound();
        if (account.IsArchived || account.Currency != op.Currency || date < account.OpeningDate)
            return Problem(400, "La cuenta no admite este pago.");
        var calculation = OperationSettlementCalculator.Calculate(op, payments, nonCash);
        if (amount > calculation.PendingAmount)
            return Problem(400, "El importe supera el saldo pendiente.");
        if (op.AccountId is null) op.AccountId = account.Id;
        FinanceRecordWriter.AddPayment(db, op, account, date, amount, r.Id, hash);
        op.Version++;
        try
        {
            mutation.MarkChanged();
            await db.SaveChangesAsync(http.RequestAborted);
            await mutation.CommitAsync(http.RequestAborted);
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            return Problem(409, "El movimiento cambió durante el pago.");
        }
        return Results.Ok(OperationSettlementMapping.Map(op, [.. payments, new FinancePayment { Id = r.Id, WorkspaceId = ws.Id, OperationId = id, AccountId = account.Id, Date = date, Amount = amount, Currency = account.Currency, CommandPayloadHash = hash }], nonCash));
    }

    private static async Task<IResult> VoidOperation(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        WorkspaceMutationGate mutationGate,
        WorkspaceAccessService access,
        TimeProvider timeProvider,
        Guid id,
        OperationVoid r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 240)
            return Problem(400, "Indicá el motivo de la anulación.");
        var ws = await Workspace(db, http);
        var (lease, gateStatus) = await mutationGate.AcquireAsync(ws.Id, http.RequestAborted);
        if (lease is null) return Problem(gateStatus, "El espacio no está disponible.");
        await using (lease)
        {
        var (fresh, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, lease.Workspace, WorkspacePermissions.CorrectHistory, http.RequestAborted);
        if (fresh is null) return Problem(authStatus, "Ya no tenés permiso para anular movimientos en este espacio.");
        var op = await OperationSettlementService.LockOperationAsync(db, ws.Id, id, http.RequestAborted);
        if (op is null)
            return Results.NotFound();
        var nonCash = await OperationSettlementService.LockSettlementsAsync(db, ws.Id, id, http.RequestAborted);
        if (op.IsVoided)
            return op.VoidRequestId == r.Id && op.VoidReason == r.Reason.Trim()
                ? Results.Ok(await Operation(db, op))
                : Problem(409, "El movimiento ya fue anulado con otros datos.");
        if (op.Version != r.Version)
            return Problem(409, "El movimiento cambió.");
        var paymentIds = await db.FinancePayments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id && x.OperationId == id).Select(x => x.Id).ToArrayAsync();
        var allocationRows = await db.SavingsMovements.AsNoTracking().Where(x => x.WorkspaceId == ws.Id && paymentIds.Contains(x.FinancePaymentId!.Value) && x.Kind == "Apply")
            .Join(db.SavingsAllocations.AsNoTracking(), m => m.AllocationId, a => a.Id, (m, a) => new { a.Id, a.AccountId, a.GoalId }).Distinct().ToArrayAsync();
        var accountIds = await db.FinancePayments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id && x.OperationId == id).Select(x => x.AccountId).Distinct().ToArrayAsync();
        accountIds = accountIds.Concat(allocationRows.Select(x => x.AccountId)).Distinct().ToArray();
        var allocationIds = allocationRows.Select(x => x.Id).Distinct().ToArray();
        await SavingsMutationLocks.LockAccountsThenAllocationsAsync(db, ws.Id, accountIds, allocationIds, http.RequestAborted);
        foreach (var goalId in allocationRows.Select(x => x.GoalId).Distinct().OrderBy(x => x))
            _ = await SavingsMutationLocks.LockGoalAsync(db, ws.Id, goalId, http.RequestAborted);
        var payments = await db.FinancePayments.FromSqlInterpolated($"SELECT * FROM \"FinancePayments\" WHERE \"WorkspaceId\" = {ws.Id} AND \"OperationId\" = {id} ORDER BY \"Id\" FOR UPDATE").ToArrayAsync(http.RequestAborted);
        foreach (var p in payments)
        {
            await SavingsAllocationService.ReverseApplicationsForPaymentAsync(db, ws.Id, p, fresh.User.Id, http.RequestAborted);
            var account = await db.Accounts.SingleAsync(x =>
                x.WorkspaceId == ws.Id && x.Id == p.AccountId
            );
            var already = await db.LedgerEntries.AnyAsync(x =>
                x.WorkspaceId == ws.Id && x.Source == "operation-void" && x.SourceId == p.Id
            );
            if (!already)
                db.LedgerEntries.Add(
                    FinanceRecordWriter.Entry(
                        ws.Id,
                        account,
                        p.Date,
                        op.Kind == "Income" ? -p.Amount : p.Amount,
                        "operation-void",
                        p.Id
                    )
                );
        }
        foreach (var root in nonCash.Where(x => x.Kind != "Reversal" && !nonCash.Any(y => y.ReversesSettlementId == x.Id)))
        {
            var requestId = DeterministicGuid($"{r.Id:D}:{root.Id:D}:void-settlement");
            var compensation = new NonCashSettlement
            {
                Id = DeterministicGuid($"{requestId:D}:row"), WorkspaceId = ws.Id, OperationId = id,
                Currency = root.Currency, Kind = "Reversal", Amount = root.Amount, ReversesSettlementId = root.Id,
                CauseVoidRequestId = r.Id, RequestId = requestId,
                PayloadHash = OperationSettlementMapping.Hash(new { voidRequestId = r.Id, settlementId = root.Id, reason = r.Reason.Trim() }),
                ActorUserId = fresh.User.Id, CreatedAt = timeProvider.GetUtcNow()
            };
            db.NonCashSettlements.Add(compensation);
            if (!await OperationSettlementSourceValidator.ValidateAsync(db, op, compensation, http.RequestAborted))
                return Problem(409, "No se pudo compensar una liquidación no monetaria.");
        }
        op.IsVoided = true;
        op.VoidRequestId = r.Id;
        op.VoidReason = r.Reason.Trim();
        op.Version++;
        lease.MarkChanged();
        await db.SaveChangesAsync(http.RequestAborted);
        await lease.CommitAsync(http.RequestAborted);
        return Results.Ok(await Operation(db, op));
        }
    }
    private static async Task<IResult> Transfer(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        TransferCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !MoneyRules.Money(r.Amount, false, out var amount)
            || !MoneyRules.Money(r.DestinationAmount, false, out var dest)
            || amount <= 0
            || dest <= 0
            || r.FromAccountId == r.ToAccountId
            || !DateOnly.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
            || date > clock.Today
        )
            return Problem(400, "Revisá los datos de la transferencia.");
        var ws = await Workspace(db, http);
        var old = await db.Transfers.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (old is not null)
            return
                old.Amount == amount
                && old.DestinationAmount == dest
                && old.FromAccountId == r.FromAccountId
                && old.ToAccountId == r.ToAccountId
                && old.Date == date
                ? Results.Ok(new { id = old.Id })
                : Problem(409, "El identificador ya se usó.");
        var from = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.FromAccountId
        );
        var to = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.ToAccountId
        );
        if (from is null || to is null)
            return Results.NotFound();
        if (
            from.IsArchived
            || to.IsArchived
            || date < from.OpeningDate
            || date < to.OpeningDate
            || from.Currency == to.Currency && amount != dest
        )
            return Problem(400, "Las cuentas o importes no son compatibles.");
        db.Transfers.Add(
            new TransferRecord
            {
                Id = r.Id,
                WorkspaceId = ws.Id,
                FromAccountId = from.Id,
                ToAccountId = to.Id,
                Date = date,
                Amount = amount,
                DestinationAmount = dest,
            }
        );
        db.LedgerEntries.Add(FinanceRecordWriter.Entry(ws.Id, from, date, -amount, "transfer-out", r.Id));
        db.LedgerEntries.Add(FinanceRecordWriter.Entry(ws.Id, to, date, dest, "transfer-in", r.Id));
        await db.SaveChangesAsync();
        return Results.Ok(new { id = r.Id });
    }

    private static async Task<OperationDto> Operation(AppDbContext db, FinanceOperation op)
    {
        var ps = await db
            .FinancePayments.Where(x => x.WorkspaceId == op.WorkspaceId && x.OperationId == op.Id)
            .ToArrayAsync();
        var settlements = await db.NonCashSettlements.Where(x => x.WorkspaceId == op.WorkspaceId && x.OperationId == op.Id).ToArrayAsync();
        return OperationSettlementMapping.Map(op, ps, settlements);
    }

    private static LedgerEntry Entry(Guid ws, BankAccount a, DateOnly date, decimal amount, string source, Guid id) =>
        FinanceRecordWriter.Entry(ws, a, date, amount, source, id);

    private static Guid DeterministicGuid(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
