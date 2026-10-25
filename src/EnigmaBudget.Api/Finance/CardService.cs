using System.Data;
using System.Globalization;
using System.Security.Claims;
using EnigmaBudget.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Finance;

public static partial class FinanceEndpoints
{
    private static async Task<IResult> CreatePurchase(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        PurchaseCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            r.InstallmentCount is < 1 or > 120
            || !MoneyRules.Money(r.TotalAmount, false, out var total)
            || total <= 0
            || !MoneyRules.IsPeriod(r.FirstPeriod)
            || !DateOnly.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
            || string.IsNullOrWhiteSpace(r.Description)
            || r.Description.Length > 240
            || r.Category?.Length > 80
            || r.Currency is not ("ARS" or "USD")
        )
            return Problem(400, "Revisá los datos de la compra.");
        var ws = await Workspace(db, http);
        var repeated = await db.Purchases.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (repeated is not null)
            return
                repeated.CardId == r.CardId
                && repeated.TotalAmount == total
                && repeated.Date == date
                && repeated.Description == r.Description.Trim()
                && repeated.Category == (r.Category?.Trim() ?? "")
                && repeated.Currency == r.Currency
                && repeated.InstallmentCount == r.InstallmentCount
                && repeated.FirstPeriod == r.FirstPeriod
                && repeated.GroupId == r.GroupId
                ? Results.Ok(await PurchaseDto(db, repeated))
                : Problem(409, "El identificador ya se usó con otros datos.");
        var card = await db.Cards.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.CardId
        );
        if (card is null)
            return Results.NotFound();
        if (
            card.IsArchived
            || date < card.AsOfDate
            || r.FirstPeriod.CompareTo(card.AsOfDate.ToString("yyyy-MM")) < 0
        )
            return Problem(400, "La tarjeta no admite compras en ese período.");
        if (
            r.GroupId.HasValue
            && !await db.Groups.AnyAsync(x => x.WorkspaceId == ws.Id && x.Id == r.GroupId)
        )
            return Results.NotFound();
        var purchasePeriods = Enumerable
            .Range(0, r.InstallmentCount)
            .Select(i =>
                DateOnly
                    .ParseExact(r.FirstPeriod + "-01", "yyyy-MM-dd", Invariant)
                    .AddMonths(i)
                    .ToString("yyyy-MM", Invariant)
            )
            .ToArray();
        if (
            await db.CardStatements.AnyAsync(x =>
                x.WorkspaceId == ws.Id
                && x.CardId == r.CardId
                && purchasePeriods.Contains(x.Period)
                && x.Currency == r.Currency
                && x.State == "Confirmed"
            )
        )
            return Problem(409, "El resumen de ese período está confirmado.");
        var cents = decimal.ToInt64(total * 100);
        if (cents < r.InstallmentCount)
            return Problem(400, "El total no alcanza para cuotas positivas.");
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        var p = new CardPurchase
        {
            Id = r.Id,
            WorkspaceId = ws.Id,
            CardId = r.CardId,
            GroupId = r.GroupId,
            Date = date,
            Description = r.Description.Trim(),
            Category = r.Category?.Trim() ?? "",
            Currency = r.Currency,
            TotalAmount = total,
            InstallmentCount = r.InstallmentCount,
            FirstPeriod = r.FirstPeriod,
        };
        db.Purchases.Add(p);
        card.Version++;
        for (var i = 0; i < r.InstallmentCount; i++)
        {
            var c = cents / r.InstallmentCount + (i < cents % r.InstallmentCount ? 1 : 0);
            var month = DateOnly
                .ParseExact(r.FirstPeriod + "-01", "yyyy-MM-dd", Invariant)
                .AddMonths(i)
                .ToString("yyyy-MM", Invariant);
            db.Installments.Add(
                new CardInstallment
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = ws.Id,
                    PurchaseId = p.Id,
                    CardId = p.CardId,
                    GroupId = p.GroupId,
                    Number = i + 1,
                    Count = p.InstallmentCount,
                    Period = month,
                    Amount = c / 100m,
                    Currency = p.Currency,
                    Description = p.Description,
                }
            );
        }
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            await transaction.RollbackAsync();
            return Problem(409, "La tarjeta cambió durante la compra.");
        }
        return Results.Ok(await PurchaseDto(db, p));
    }

    private static async Task<IResult> GetPurchase(HttpContext http, AppDbContext db, Guid id)
    {
        var ws = await Workspace(db, http);
        var p = await db.Purchases.SingleOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == id);
        return p is null ? Results.NotFound() : Results.Ok(await PurchaseDto(db, p));
    }

    private static async Task<IResult> GetPurchases(HttpContext http, AppDbContext db, Guid cardId)
    {
        var ws = await Workspace(db, http);
        if (!await db.Cards.AnyAsync(x => x.WorkspaceId == ws.Id && x.Id == cardId))
            return Results.NotFound();
        var ps = await db
            .Purchases.Where(x => x.WorkspaceId == ws.Id && x.CardId == cardId)
            .ToArrayAsync();
        var output = new List<object>();
        foreach (var p in ps)
            output.Add(await PurchaseDto(db, p));
        return Results.Ok(output);
    }

    private static async Task<object> PurchaseDto(AppDbContext db, CardPurchase p)
    {
        var installments = await db
            .Installments.Where(x => x.WorkspaceId == p.WorkspaceId && x.PurchaseId == p.Id)
            .OrderBy(x => x.Number)
            .Select(x => new
            {
                id = x.Id,
                purchaseId = x.PurchaseId,
                cardId = x.CardId,
                description = x.Description,
                currency = x.Currency,
                number = x.Number,
                count = x.Count,
                period = x.Period,
                amount = F(x.Amount),
                groupId = x.GroupId,
            })
            .ToArrayAsync();
        return new
        {
            id = p.Id,
            cardId = p.CardId,
            date = p.Date.ToString("yyyy-MM-dd"),
            description = p.Description,
            category = p.Category,
            currency = p.Currency,
            totalAmount = F(p.TotalAmount),
            installmentCount = p.InstallmentCount,
            firstPeriod = p.FirstPeriod,
            groupId = p.GroupId,
            installments,
        };
    }

    private static async Task<IResult> GetStatement(
        HttpContext http,
        AppDbContext db,
        Guid cardId,
        string? period = null,
        string? currency = null
    )
    {
        var ws = await Workspace(db, http);
        if (!await db.Cards.AnyAsync(x => x.WorkspaceId == ws.Id && x.Id == cardId))
            return Results.NotFound();
        if (!MoneyRules.IsPeriod(period ?? "") || currency is not ("ARS" or "USD"))
            return Problem(400, "Período o moneda inválidos.");

        var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", Invariant);
        var end = start.AddMonths(1);
        var statement = await db.CardStatements.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id
            && x.CardId == cardId
            && x.Period == period
            && x.Currency == currency
        );
        var input = await CardObligationCalculator.LoadAsync(db, ws.Id, cardId, http.RequestAborted);
        if (statement is { State: not "Confirmed" }) input = IncludeStatement(input, statement);
        var installments = await db
            .Installments.Where(x =>
                x.WorkspaceId == ws.Id
                && x.CardId == cardId
                && x.Currency == currency
                && string.Compare(x.Period, period) <= 0
            )
            .OrderBy(x => x.Period)
            .ToArrayAsync();
        var paymentRows = input.Payments.Where(x => x.Currency == currency && x.Date < end).ToArray();
        var currentAdjustments = statement is null
            ? []
            : await db
                .StatementAdjustments.Where(x =>
                    x.WorkspaceId == ws.Id && x.StatementId == statement.Id
                )
                .ToArrayAsync();
        var breakdown = CardObligationCalculator.GetStatementBreakdown(input, period!, end.AddDays(-1), currency);
        var carry = breakdown.Carry;
        var expected = breakdown.Expected;
        var pending = breakdown.Pending;
        var creditBalance = breakdown.Credit;
        var statementInstallments = installments
            .Where(x => x.Period == period)
            .Select(x => new
            {
                id = x.Id,
                purchaseId = x.PurchaseId,
                cardId = x.CardId,
                description = x.Description,
                currency = x.Currency,
                number = x.Number,
                count = x.Count,
                period = x.Period,
                amount = F(x.Amount),
                groupId = x.GroupId,
            });
        return Results.Ok(
            new
            {
                id = statement?.Id,
                cardId,
                period,
                currency,
                version = statement?.Version ?? 0,
                state = statement?.State ?? "Estimated",
                dueDate = statement?.DueDate?.ToString("yyyy-MM-dd"),
                actualTotal = statement?.ActualTotal is null
                    ? null
                    : F(statement.ActualTotal.Value),
                expectedAmount = F(expected),
                carryAmount = F(carry),
                pendingAmount = F(pending),
                creditBalance = F(creditBalance),
                installments = statementInstallments,
                adjustments = currentAdjustments.Select(x => new
                {
                    id = x.Id,
                    description = x.Description,
                    category = x.Category,
                    amount = F(x.Amount),
                }),
                payments = paymentRows
                    .Where(x => x.Date >= start)
                    .Select(x => new
                    {
                        id = x.Id,
                        cardId = x.CardId,
                        accountId = x.AccountId,
                        date = x.Date.ToString("yyyy-MM-dd"),
                        amount = F(x.Amount),
                        accountCurrency = x.AccountCurrency,
                        currency = x.Currency,
                        appliedAmount = F(x.AppliedAmount),
                        statementId = x.StatementId,
                    }),
            }
        );
    }

    private static async Task<IResult> SaveStatement(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        Guid cardId,
        StatementSave r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !MoneyRules.IsPeriod(r.Period)
            || r.Currency is not ("ARS" or "USD")
            || !MoneyRules.Money(r.ActualTotal, false, out var total)
            || r.Adjustments is null
            || r.Adjustments.Any(x =>
                string.IsNullOrWhiteSpace(x.Description)
                || x.Description.Length > 240
                || x.Category?.Length > 80
                || !MoneyRules.Money(x.Amount, true, out _)
            )
            || r.DueDate is not null
                && !DateOnly.TryParseExact(
                    r.DueDate,
                    "yyyy-MM-dd",
                    Invariant,
                    DateTimeStyles.None,
                    out _
                )
        )
            return Problem(400, "Revisá el resumen y sus ajustes.");
        var ws = await Workspace(db, http);
        if (!await db.Cards.AnyAsync(x => x.WorkspaceId == ws.Id && x.Id == cardId))
            return Results.NotFound();
        var s = await db.CardStatements.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id
            && x.CardId == cardId
            && x.Period == r.Period
            && x.Currency == r.Currency
        );
        if (s?.State == "Confirmed")
            return Problem(409, "El resumen está confirmado.");
        if ((s?.Version ?? 0) != r.Version)
            return Problem(409, "El resumen cambió.");
        if (s is null)
        {
            s = new CardStatement
            {
                Id = r.Id,
                WorkspaceId = ws.Id,
                CardId = cardId,
                Period = r.Period,
                Currency = r.Currency,
                Version = 1,
            };
            db.CardStatements.Add(s);
        }
        else
        {
            s.Version++;
            db.StatementAdjustments.RemoveRange(
                db.StatementAdjustments.Where(x => x.StatementId == s.Id)
            );
        }
        s.State = "Draft";
        s.DueDate = r.DueDate is null
            ? null
            : DateOnly.ParseExact(r.DueDate, "yyyy-MM-dd", Invariant);
        s.ActualTotal = total;
        foreach (var a in r.Adjustments)
            db.StatementAdjustments.Add(
                new StatementAdjustment
                {
                    Id = a.Id,
                    WorkspaceId = ws.Id,
                    StatementId = s.Id,
                    Description = a.Description.Trim(),
                    Category = a.Category?.Trim() ?? "",
                    Amount = decimal.Parse(a.Amount, Invariant),
                }
            );
        await db.SaveChangesAsync();
        return Results.Ok(
            new
            {
                id = s.Id,
                period = s.Period,
                currency = s.Currency,
                version = s.Version,
                state = s.State,
            }
        );
    }

    private static async Task<IResult> ConfirmStatement(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        Guid cardId,
        StatementConfirm r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        var ws = await Workspace(db, http);
        var s = await db.CardStatements.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.CardId == cardId && x.Id == r.Id
        );
        if (s is null)
            return Results.NotFound();
        if (s.State == "Confirmed")
            return Results.Ok(
                new
                {
                    id = s.Id,
                    version = s.Version,
                    state = s.State,
                }
            );
        if (s.Version != r.Version)
            return Problem(409, "El resumen cambió.");
        var current = await GetStatementTotals(db, ws.Id, s, includeDraftStatement: true);
        if (s.ActualTotal != current)
            return Problem(
                400,
                $"El importe real debe coincidir con el estimado ({F(current)})."
            );
        s.State = "Confirmed";
        s.Version++;
        var card = await db.Cards.SingleAsync(x => x.WorkspaceId == ws.Id && x.Id == cardId);
        card.Version++;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            return Problem(409, "El resumen o la tarjeta cambió.");
        }
        return Results.Ok(
            new
            {
                id = s.Id,
                version = s.Version,
                state = s.State,
            }
        );
    }

    private static async Task<decimal> GetStatementTotals(
        AppDbContext db,
        Guid workspaceId,
        CardStatement statement,
        bool includeDraftStatement = false
    )
    {
        var end = DateOnly
            .ParseExact(statement.Period + "-01", "yyyy-MM-dd", Invariant)
            .AddMonths(1);
        var input = await CardObligationCalculator.LoadAsync(db, workspaceId, statement.CardId);
        if (includeDraftStatement && input.Statements.Any(x => x.Id == statement.Id)) input = IncludeStatement(input, statement);
        return CardObligationCalculator.GetStatementBreakdown(input, statement.Period, end.AddDays(-1), statement.Currency).Pending;
    }

    private static CardObligationInput IncludeStatement(CardObligationInput input, CardStatement statement) =>
        input with { Statements = input.Statements.Select(x => x.Id == statement.Id
            ? new CardStatement { Id = x.Id, WorkspaceId = x.WorkspaceId, CardId = x.CardId, Period = x.Period,
                Currency = x.Currency, Version = x.Version, State = "Confirmed", DueDate = x.DueDate,
                ActualTotal = x.ActualTotal }
            : x).ToArray() };

    private static async Task<IResult> PayCard(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        Guid cardId,
        CardPaymentCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !MoneyRules.Money(r.Amount, false, out var amount)
            || !MoneyRules.Money(r.AppliedAmount, false, out var applied)
            || amount <= 0
            || applied <= 0
            || r.Currency is not ("ARS" or "USD")
            || !DateOnly.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
            || date > clock.Today
        )
            return Problem(400, "Revisá los datos del pago.");

        var ws = await Workspace(db, http);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        var replay = await db.CardPayments.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (replay is not null)
        {
            var same =
                replay.CardId == cardId
                && replay.AppliedAmount == applied
                && replay.Amount == amount
                && replay.AccountId == r.AccountId
                && replay.Currency == r.Currency
                && replay.StatementId == r.StatementId
                && replay.Date == date;
            return same
                ? Results.Ok(new { id = replay.Id })
                : Problem(409, "El identificador ya se usó con otros datos.");
        }

        var card = await db.Cards.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == cardId
        );
        if (card is null)
            return Results.NotFound();
        var account = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.AccountId
        );
        if (account is null)
            return Results.NotFound();
        if (account.IsArchived || (account.Currency == r.Currency && amount != applied))
            return Problem(400, "El pago o la cuenta no son compatibles.");
        if (
            r.StatementId.HasValue
            && !await db.CardStatements.AnyAsync(x =>
                x.WorkspaceId == ws.Id
                && x.Id == r.StatementId
                && x.CardId == cardId
                && x.Currency == r.Currency
                && x.State == "Confirmed"
            )
        )
            return Problem(400, "El resumen no está confirmado o no corresponde a la tarjeta.");
        if (date < account.OpeningDate || date < card.AsOfDate)
            return Problem(400, "La fecha es anterior al alta de la cuenta o tarjeta.");

        var period = date.ToString("yyyy-MM", Invariant);
        var obligation = await GetStatementTotals(
            db,
            ws.Id,
            new CardStatement
            {
                CardId = cardId,
                Currency = r.Currency,
                Period = period,
            }
        );
        if (obligation <= 0)
            return Problem(400, "No hay una obligación de tarjeta para pagar en este período.");

        db.CardPayments.Add(
            new CardPaymentRecord
            {
                Id = r.Id,
                WorkspaceId = ws.Id,
                CardId = cardId,
                AccountId = account.Id,
                Date = date,
                Amount = amount,
                AccountCurrency = account.Currency,
                Currency = r.Currency,
                AppliedAmount = applied,
                StatementId = r.StatementId,
            }
        );
        db.LedgerEntries.Add(Entry(ws.Id, account, date, -amount, "card-payment", r.Id));
        card.Version++;
        if (r.StatementId.HasValue)
        {
            var statement = await db.CardStatements.SingleAsync(x =>
                x.WorkspaceId == ws.Id && x.Id == r.StatementId.Value
            );
            statement.Version++;
        }
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            await transaction.RollbackAsync();
            return Problem(409, "La tarjeta cambió durante el pago.");
        }
        return Results.Ok(
            new
            {
                id = r.Id,
                cardId,
                accountId = account.Id,
                date = r.Date,
                amount = F(amount),
                accountCurrency = account.Currency,
                currency = r.Currency,
                appliedAmount = F(applied),
                statementId = r.StatementId,
            }
        );
    }
}
