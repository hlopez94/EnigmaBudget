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
    private static async Task<IResult> Accounts(HttpContext http, AppDbContext db, BusinessClock clock)
    {
        var workspace = await Workspace(db, http);
        var entries = await db
            .LedgerEntries.Where(x =>
                x.WorkspaceId == workspace.Id
                && x.Date <= clock.Today
                && db.Accounts.Any(a =>
                    a.WorkspaceId == workspace.Id && a.Id == x.AccountId && x.Date >= a.OpeningDate
                )
            )
            .GroupBy(x => x.AccountId)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Id, x => x.Total);
        var accounts = await db
            .Accounts.Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.Name)
            .ToArrayAsync();
        return Results.Ok(
            accounts
                .Select(x => new AccountDto(
                    x.Id,
                    x.Name,
                    x.BankName,
                    x.Currency,
                    F(x.OpeningBalance),
                    x.OpeningDate.ToString("yyyy-MM-dd"),
                    F(
                        (x.OpeningDate <= clock.Today ? x.OpeningBalance : 0m)
                            + (entries.TryGetValue(x.Id, out var balance) ? balance : 0m)
                    ),
                    x.IsArchived,
                    x.Version
                ))
                .ToArray()
        );
    }

    private static async Task<IResult> CreateAccount(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        AccountCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !ValidName(r.Name)
            || !ValidName(r.BankName)
            || r.Currency is not ("ARS" or "USD")
            || !MoneyRules.Money(r.OpeningBalance, true, out var opening)
            || !DateOnly.TryParseExact(
                r.OpeningDate,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
        )
            return Problem(400, "Revisá los datos de la cuenta.");
        var ws = await Workspace(db, http);
        var existing = await db.Accounts.FirstOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (existing is not null)
            return
                existing.Name == r.Name.Trim()
                && existing.BankName == r.BankName.Trim()
                && existing.Currency == r.Currency
                && existing.OpeningBalance == opening
                && existing.OpeningDate == date
                ? Results.Ok(await Account(db, existing, clock))
                : Problem(409, "El identificador ya está usado con otros datos.");
        var account = new BankAccount
        {
            Id = r.Id,
            WorkspaceId = ws.Id,
            Name = r.Name.Trim(),
            BankName = r.BankName.Trim(),
            Currency = r.Currency,
            OpeningBalance = opening,
            OpeningDate = date,
        };
        db.Accounts.Add(account);
        db.OpeningBalances.Add(
            new OpeningBalanceRecord
            {
                WorkspaceId = ws.Id,
                AccountId = r.Id,
                Currency = r.Currency,
                Amount = opening,
                EffectiveDate = date,
            }
        );
        await db.SaveChangesAsync();
        return Results.Ok(await Account(db, account, clock));
    }

    private static async Task<IResult> UpdateAccount(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        Guid id,
        AccountUpdate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        var ws = await Workspace(db, http);
        var a = await db.Accounts.SingleOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == id);
        if (a is null)
            return Results.NotFound();
        if (r.Version != a.Version)
            return Problem(409, "La cuenta cambió. Actualizá la pantalla.");
        if (!ValidName(r.Name) || !ValidName(r.BankName))
            return Problem(400, "Revisá el nombre y el banco.");
        a.Name = r.Name.Trim();
        a.BankName = r.BankName.Trim();
        a.IsArchived = r.IsArchived;
        a.Version++;
        await db.SaveChangesAsync();
        return Results.Ok(await Account(db, a, clock));
    }

    private static async Task<IResult> AdjustAccount(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        Guid id,
        AccountAdjustmentCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !MoneyRules.Money(r.ObservedBalance, true, out var observed)
            || !DateOnly.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
            || string.IsNullOrWhiteSpace(r.Reason)
            || r.Reason.Length > 240
            || date > clock.Today
        )
            return Problem(400, "Revisá los datos del ajuste.");
        var ws = await Workspace(db, http);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        var account = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == id
        );
        if (account is null)
            return Results.NotFound();
        if (date < account.OpeningDate)
            return Problem(400, "La fecha es anterior al alta de la cuenta.");
        var replay = await db.LedgerEntries.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Source == "balance-adjustment" && x.SourceId == r.Id
        );
        if (replay is not null)
            return
                replay.AccountId == id
                && replay.Date == date
                && replay.ObservedBalance == observed
                && replay.Reason == r.Reason.Trim()
                ? Results.Ok(await Account(db, account, clock))
                : Problem(409, "El identificador del ajuste ya se usó con otros datos.");
        var prior =
            await db
                .LedgerEntries.Where(x =>
                    x.WorkspaceId == ws.Id
                    && x.AccountId == id
                    && x.Date >= account.OpeningDate
                    && x.Date <= date
                )
                .SumAsync(x => (decimal?)x.Amount)
            ?? 0;
        var expected = account.OpeningBalance + prior;
        var adjustment = Entry(
            ws.Id,
            account,
            date,
            observed - expected,
            "balance-adjustment",
            r.Id
        );
        adjustment.ObservedBalance = observed;
        adjustment.Reason = r.Reason.Trim();
        db.LedgerEntries.Add(adjustment);
        account.Version++;
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            await transaction.RollbackAsync();
            return Problem(409, "La cuenta cambió durante el ajuste.");
        }
        return Results.Ok(await Account(db, account, clock));
    }

    private static async Task<IResult> Cards(HttpContext http, AppDbContext db, BusinessClock clock)
    {
        var ws = await Workspace(db, http);
        var cards = await db
            .Cards.Where(x => x.WorkspaceId == ws.Id)
            .OrderBy(x => x.Name)
            .ToArrayAsync();
        var output = new List<object>();
        var today = clock.Today;
        foreach (var card in cards)
        {
            var built = CardObligationCalculator.Build(await CardObligationCalculator.LoadAsync(db, ws.Id, card.Id, http.RequestAborted), today);
            var ars = built.ForCurrency("ARS");
            var usd = built.ForCurrency("USD");
            output.Add(
                new
                {
                    id = card.Id,
                    name = card.Name,
                    bankName = card.BankName,
                    closingDay = card.ClosingDay,
                    dueDay = card.DueDay,
                    statementDebtArs = F(card.StatementDebtArs),
                    statementDebtUsd = F(card.StatementDebtUsd),
                    asOfDate = card.AsOfDate.ToString("yyyy-MM-dd"),
                    debtArs = F(ars.Debt),
                    debtUsd = F(usd.Debt),
                    creditArs = F(ars.Credit),
                    creditUsd = F(usd.Credit),
                    isArchived = card.IsArchived,
                    version = card.Version,
                }
            );
        }
        return Results.Ok(output);
    }

    private static async Task<IResult> CreateCard(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        CardCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !ValidName(r.Name)
            || !ValidName(r.BankName)
            || !MoneyRules.Money(r.StatementDebtArs, true, out var ars)
            || !MoneyRules.Money(r.StatementDebtUsd, true, out var usd)
            || r.ClosingDay is < 1 or > 31
            || r.DueDay is < 1 or > 31
            || !DateOnly.TryParseExact(
                r.AsOfDate,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
        )
            return Problem(400, "Revisá los datos de la tarjeta.");
        var ws = await Workspace(db, http);
        var old = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == r.Id);
        if (old is not null)
            return
                old.Name == r.Name.Trim()
                && old.BankName == r.BankName.Trim()
                && old.ClosingDay == r.ClosingDay
                && old.DueDay == r.DueDay
                && old.StatementDebtArs == ars
                && old.StatementDebtUsd == usd
                && old.AsOfDate == date
                ? Results.Ok(
                    new
                    {
                        id = old.Id,
                        name = old.Name,
                        bankName = old.BankName,
                        statementDebtArs = F(ars),
                        statementDebtUsd = F(usd),
                        asOfDate = date.ToString("yyyy-MM-dd"),
                        version = old.Version,
                    }
                )
                : Problem(409, "El identificador de la tarjeta ya se usó con otros datos.");
        var card = new PaymentCard
        {
            Id = r.Id,
            WorkspaceId = ws.Id,
            Name = r.Name.Trim(),
            BankName = r.BankName.Trim(),
            ClosingDay = r.ClosingDay,
            DueDay = r.DueDay,
            StatementDebtArs = ars,
            StatementDebtUsd = usd,
            AsOfDate = date,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return Results.Ok(
            new
            {
                id = card.Id,
                name = card.Name,
                bankName = card.BankName,
                statementDebtArs = F(ars),
                statementDebtUsd = F(usd),
                asOfDate = date.ToString("yyyy-MM-dd"),
                version = 1,
            }
        );
    }

    private static async Task<IResult> UpdateCard(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        Guid id,
        CardUpdate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        var ws = await Workspace(db, http);
        var card = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == ws.Id && x.Id == id);
        if (card is null)
            return Results.NotFound();
        if (card.Version != r.Version)
            return Problem(409, "La tarjeta cambió. Actualizá la pantalla.");
        if (
            !ValidName(r.Name)
            || !ValidName(r.BankName)
            || r.ClosingDay is < 1 or > 31
            || r.DueDay is < 1 or > 31
        )
            return Problem(400, "Revisá el nombre y los días de cierre y vencimiento.");
        card.Name = r.Name.Trim();
        card.BankName = r.BankName.Trim();
        card.ClosingDay = r.ClosingDay;
        card.DueDay = r.DueDay;
        card.IsArchived = r.IsArchived;
        card.Version++;
        await db.SaveChangesAsync();
        return Results.Ok(
            new
            {
                id = card.Id,
                name = card.Name,
                bankName = card.BankName,
                closingDay = card.ClosingDay,
                dueDay = card.DueDay,
                isArchived = card.IsArchived,
                version = card.Version,
            }
        );
    }

    private static async Task<object> Account(AppDbContext db, BankAccount a, BusinessClock clock)
    {
        var delta =
            await db
                .LedgerEntries.Where(x => x.AccountId == a.Id && x.WorkspaceId == a.WorkspaceId && x.Date >= a.OpeningDate && x.Date <= clock.Today)
                .SumAsync(x => (decimal?)x.Amount)
            ?? 0;
        return new AccountDto(
            a.Id,
            a.Name,
            a.BankName,
            a.Currency,
            F(a.OpeningBalance),
            a.OpeningDate.ToString("yyyy-MM-dd"),
            F((a.OpeningDate <= clock.Today ? a.OpeningBalance : 0m) + delta),
            a.IsArchived,
            a.Version
        );
    }
}
