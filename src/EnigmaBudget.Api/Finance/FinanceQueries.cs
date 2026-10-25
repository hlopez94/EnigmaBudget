using System.Data;
using System.Globalization;
using System.Security.Claims;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Finance;

public static partial class FinanceEndpoints
{
    private static async Task<IResult> FinanceOverview(
        HttpContext http,
        AppDbContext db,
        BusinessClock clock,
        string? period = null
    )
    {
        period ??= clock.CurrentPeriod;
        if (!MoneyRules.IsPeriod(period ?? ""))
            return Problem(400, "Período inválido.");
        var ws = await Workspace(db, http);
        var accounts = await db.Accounts.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync();
        var ledgerRows = await db.LedgerEntries.Where(x =>
            x.WorkspaceId == ws.Id
            && x.Date <= clock.Today
            && db.Accounts.Any(a =>
                a.WorkspaceId == ws.Id && a.Id == x.AccountId && x.Date >= a.OpeningDate
            )
        ).ToArrayAsync();
        var ledger = ledgerRows
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var accountDtos = accounts
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.BankName,
                x.Currency,
                F(x.OpeningBalance),
                x.OpeningDate.ToString("yyyy-MM-dd"),
                F((x.OpeningDate <= clock.Today ? x.OpeningBalance : 0m) + (ledger.TryGetValue(x.Id, out var b) ? b : 0m)),
                x.IsArchived,
                x.Version
            ))
            .ToArray();
        var cards = await db.Cards.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync();
        var confirmedAdjustments = await (
            from adjustment in db.StatementAdjustments
            join statement in db.CardStatements
                on new { adjustment.WorkspaceId, StatementId = adjustment.StatementId } equals new
                {
                    statement.WorkspaceId,
                    StatementId = statement.Id,
                }
            where adjustment.WorkspaceId == ws.Id && statement.State == "Confirmed"
            select new
            {
                statement.CardId,
                statement.Currency,
                statement.Period,
                adjustment.Amount,
            }
        ).ToArrayAsync();
        var cardDtos = new List<object>();
        decimal cardDebtArs = 0,
            cardDebtUsd = 0,
            cardCreditArs = 0,
            cardCreditUsd = 0;
        var today = clock.Today;
        foreach (var card in cards)
        {
            var built = CardObligationCalculator.Build(await CardObligationCalculator.LoadAsync(db, ws.Id, card.Id, http.RequestAborted), today);
            var ars = built.ForCurrency("ARS");
            var usd = built.ForCurrency("USD");
            cardDebtArs += ars.Debt;
            cardDebtUsd += usd.Debt;
            cardCreditArs += ars.Credit;
            cardCreditUsd += usd.Credit;
            cardDtos.Add(
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
        var year = int.Parse(period![..4]);
        var month = int.Parse(period[5..7]);
        var ops = new List<object>();
        foreach (
            var op in await db
                .FinanceOperations.Where(x =>
                    x.WorkspaceId == ws.Id && x.Date.Year == year && x.Date.Month == month
                )
                .ToArrayAsync()
        )
            ops.Add(await Operation(db, op));
        var installmentRows = await db
            .Installments.Where(x => x.WorkspaceId == ws.Id && x.Period == period)
            .ToArrayAsync();
        var installments = installmentRows
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
            .ToArray();
        var balanceArs =
            accounts.Where(x => x.Currency == "ARS" && x.OpeningDate <= clock.Today).Sum(x => x.OpeningBalance)
            + ledgerRows.Where(x => x.Currency == "ARS").Sum(x => x.Amount);
        var balanceUsd =
            accounts.Where(x => x.Currency == "USD" && x.OpeningDate <= clock.Today).Sum(x => x.OpeningBalance)
            + ledgerRows.Where(x => x.Currency == "USD").Sum(x => x.Amount);
        var expenses = await db
            .FinanceOperations.Where(x =>
                x.WorkspaceId == ws.Id
                && x.Date.Year == year
                && x.Date.Month == month
                && x.Kind == "Expense"
                && !x.IsVoided
            )
            .ToArrayAsync();
        var expenseArs = expenses.Where(x => x.Currency == "ARS").Sum(x => x.Amount);
        var expenseUsd = expenses.Where(x => x.Currency == "USD").Sum(x => x.Amount);
        var cardChargesArs = confirmedAdjustments
            .Where(x => x.Period == period && x.Currency == "ARS")
            .Sum(x => x.Amount);
        var cardChargesUsd = confirmedAdjustments
            .Where(x => x.Period == period && x.Currency == "USD")
            .Sum(x => x.Amount);
        var groupRows = new List<object>();
        var receivableRows = new List<object>();
        var financeContext = FinanceWorkspaceFilter.Get(http)!;
        if (financeContext.Permissions.Contains(WorkspacePermissions.ReadGroups))
        foreach (var group in await db.Groups.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync())
        {
            var participants = await db
                .Participants.Where(x => x.WorkspaceId == ws.Id && x.GroupId == group.Id)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    isSelf = x.IsSelf,
                })
                .ToArrayAsync();
            groupRows.Add(
                new
                {
                    id = group.Id,
                    name = group.Name,
                    version = group.Version,
                    participants,
                    rule = await RuleDto(db, ws.Id, group.Id, period),
                }
            );
            var shares = await ReceivableProjectionQuery.GetForPeriodAsync(db, ws.Id, group.Id, period, http.RequestAborted);
            receivableRows.AddRange(shares.Select(x => (object)new
            {
                id = x.Id, groupId = x.GroupId, sourceId = x.SourceId, purchaseId = x.PurchaseId, period = x.Period,
                description = x.Description, currency = x.Currency, participantId = x.ParticipantId, participantName = x.ParticipantName,
                isSelf = x.IsSelf, amount = x.Amount is null ? null : F(x.Amount.Value), collectedAmount = F(x.CollectedAmount),
                pendingAmount = x.PendingAmount is null ? null : F(x.PendingAmount.Value), isEstimated = x.IsEstimated,
                ruleVersion = x.RuleVersion, monthConfirmed = x.MonthConfirmed, status = x.Status,
            }));
        }
        var overview = new Dictionary<string, object?>
            {
                ["period"] = period,
                ["accounts"] = accountDtos,
                ["cards"] = cardDtos,
                ["operations"] = ops,
                ["installments"] = installments,
                ["budget"] = new
                {
                    cashExpensesArs = F(expenseArs),
                    cashExpensesUsd = F(expenseUsd),
                    operationExpensesArs = F(expenseArs),
                    operationExpensesUsd = F(expenseUsd),
                    cardInstallmentsArs = F(
                        installmentRows.Where(x => x.Currency == "ARS").Sum(x => x.Amount)
                    ),
                    cardInstallmentsUsd = F(
                        installmentRows.Where(x => x.Currency == "USD").Sum(x => x.Amount)
                    ),
                    cardChargesArs = F(cardChargesArs),
                    cardChargesUsd = F(cardChargesUsd),
                },
                ["totals"] = new
                {
                    balanceArs = F(balanceArs),
                    balanceUsd = F(balanceUsd),
                    cardDebtArs = F(cardDebtArs),
                    cardDebtUsd = F(cardDebtUsd),
                    cardCreditArs = F(cardCreditArs),
                    cardCreditUsd = F(cardCreditUsd),
                },
            };
        if (financeContext.Permissions.Contains(WorkspacePermissions.ReadGroups))
        {
            overview["groups"] = groupRows;
            overview["receivables"] = receivableRows;
        }
        return Results.Ok(overview);
    }

}
