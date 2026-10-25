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
        var ws = await Workspace(db, http);
        var account = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.AccountId
        );
        if (account is null)
            return Results.NotFound();
        if (account.IsArchived)
            return Problem(409, "La cuenta está archivada.");
        if (r.Paid && (date > clock.Today || date < account.OpeningDate))
            return Problem(
                400,
                "La fecha de un movimiento confirmado debe estar entre el alta de la cuenta y hoy."
            );
        var prior = await db
            .FinanceOperations.Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == r.Id);
        if (prior is not null)
            return
                prior.Amount == amount
                && prior.AccountId == r.AccountId
                && prior.Kind == r.Kind
                && prior.Date == date
                && prior.Description == r.Description.Trim()
                && prior.Category == (r.Category?.Trim() ?? "")
                && prior.InitiallyPaid == r.Paid
                ? Results.Ok(await Operation(db, prior))
                : Problem(409, "El identificador ya se usó con otros datos.");
        var op = new FinanceOperation
        {
            Id = r.Id,
            WorkspaceId = ws.Id,
            Kind = r.Kind,
            AccountId = r.AccountId,
            Date = date,
            Description = r.Description.Trim(),
            Category = r.Category?.Trim() ?? "",
            Currency = account.Currency,
            Amount = amount,
            InitiallyPaid = r.Paid,
        };
        db.FinanceOperations.Add(op);
        if (r.Paid)
        {
            var pay = new FinancePayment
            {
                Id = r.Id,
                WorkspaceId = ws.Id,
                OperationId = r.Id,
                AccountId = account.Id,
                Date = date,
                Amount = amount,
                Currency = account.Currency,
            };
            db.FinancePayments.Add(pay);
            db.LedgerEntries.Add(
                Entry(
                    ws.Id,
                    account,
                    date,
                    r.Kind == "Income" ? amount : -amount,
                    "operation-payment",
                    r.Id
                )
            );
        }
        await db.SaveChangesAsync();
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
        var output = rows.Select(x =>
            {
                var ps = paymentRows.Where(p => p.OperationId == x.Id).ToArray();
                var paid = ps.Sum(p => p.Amount);
                return new OperationDto(
                    x.Id,
                    x.Kind,
                    x.AccountId,
                    x.Date.ToString("yyyy-MM-dd"),
                    x.Description,
                    x.Category,
                    F(x.Amount),
                    x.Currency,
                    F(paid),
                    F(x.Amount - paid),
                    x.IsVoided ? "Voided"
                        : paid == 0 ? "Pending"
                        : paid < x.Amount ? "Partial"
                        : "Settled",
                    x.Version,
                    ps.Select(p => new PaymentDto(
                            p.Id,
                            p.AccountId,
                            p.Date.ToString("yyyy-MM-dd"),
                            F(p.Amount)
                        ))
                        .ToArray()
                );
            })
            .ToArray();
        return Results.Ok(output);
    }

    private static async Task<IResult> PayOperation(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
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
        var ws = await Workspace(db, http);
        await using var tx = await db.Database.BeginTransactionAsync();
        var op = await db
            .FinanceOperations.Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == id);
        if (op is null)
            return Results.NotFound();
        if (op.IsVoided)
            return Problem(409, "El movimiento está anulado.");
        var replay = await db.FinancePayments.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (replay is not null)
            return
                replay.OperationId == id
                && replay.Amount == amount
                && replay.AccountId == r.AccountId
                && replay.Date == date
                ? Results.Ok(await Operation(db, op))
                : Problem(409, "El identificador ya se usó.");
        var account = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.AccountId
        );
        if (account is null)
            return Results.NotFound();
        if (account.IsArchived || account.Currency != op.Currency || date < account.OpeningDate)
            return Problem(400, "La cuenta no admite este pago.");
        var paid =
            await db
                .FinancePayments.Where(x => x.WorkspaceId == ws.Id && x.OperationId == id)
                .SumAsync(x => (decimal?)x.Amount)
            ?? 0;
        if (amount > op.Amount - paid)
            return Problem(400, "El importe supera el saldo pendiente.");
        db.FinancePayments.Add(
            new FinancePayment
            {
                Id = r.Id,
                WorkspaceId = ws.Id,
                OperationId = id,
                AccountId = account.Id,
                Date = date,
                Amount = amount,
                Currency = account.Currency,
            }
        );
        db.LedgerEntries.Add(
            Entry(
                ws.Id,
                account,
                date,
                op.Kind == "Income" ? amount : -amount,
                "operation-payment",
                r.Id
            )
        );
        op.Version++;
        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            await tx.RollbackAsync();
            return Problem(409, "El movimiento cambió durante el pago.");
        }
        return Results.Ok(await Operation(db, op));
    }

    private static async Task<IResult> VoidOperation(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        WorkspaceMutationGate mutationGate,
        WorkspaceAccessService access,
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
        var op = await db.FinanceOperations.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == id
        );
        if (op is null)
            return Results.NotFound();
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
        var (fresh, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, lease.Workspace, WorkspacePermissions.CorrectHistory, http.RequestAborted);
        if (fresh is null) return Problem(authStatus, "Ya no tenés permiso para anular movimientos en este espacio.");
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
                    Entry(
                        ws.Id,
                        account,
                        p.Date,
                        op.Kind == "Income" ? -p.Amount : p.Amount,
                        "operation-void",
                        p.Id
                    )
                );
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
        db.LedgerEntries.Add(Entry(ws.Id, from, date, -amount, "transfer-out", r.Id));
        db.LedgerEntries.Add(Entry(ws.Id, to, date, dest, "transfer-in", r.Id));
        await db.SaveChangesAsync();
        return Results.Ok(new { id = r.Id });
    }

    private static async Task<OperationDto> Operation(AppDbContext db, FinanceOperation op)
    {
        var ps = await db
            .FinancePayments.Where(x => x.WorkspaceId == op.WorkspaceId && x.OperationId == op.Id)
            .ToArrayAsync();
        var paid = ps.Sum(x => x.Amount);
        return new(
            op.Id,
            op.Kind,
            op.AccountId,
            op.Date.ToString("yyyy-MM-dd"),
            op.Description,
            op.Category,
            F(op.Amount),
            op.Currency,
            F(paid),
            F(op.Amount - paid),
            op.IsVoided ? "Voided"
                : paid == 0 ? "Pending"
                : paid < op.Amount ? "Partial"
                : "Settled",
            op.Version,
            ps.Select(x => new PaymentDto(
                    x.Id,
                    x.AccountId,
                    x.Date.ToString("yyyy-MM-dd"),
                    F(x.Amount)
                ))
                .ToArray()
        );
    }

    private static LedgerEntry Entry(
        Guid ws,
        BankAccount a,
        DateOnly date,
        decimal amount,
        string source,
        Guid id
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = ws,
            AccountId = a.Id,
            Date = date,
            Currency = a.Currency,
            Amount = amount,
            Source = source,
            SourceId = id,
        };
}
