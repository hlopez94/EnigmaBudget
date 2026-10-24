using System.Globalization;
using System.Security.Claims;
using System.Data;
using EnigmaBudget.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Finance;

public static partial class FinanceEndpoints
{
    private static async Task<IResult> FinanceOverview(HttpContext http, AppDbContext db, string? period = null)
    {
        if (!MoneyRules.IsPeriod(period ?? "")) return Problem(400, "Período inválido.");
        var ws = await Workspace(db, Subject(http));
        var accounts = await db.Accounts.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync();
        var ledgerRows = await db.LedgerEntries.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync();
        var ledger = ledgerRows.GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var accountDtos = accounts.Select(x => new AccountDto(x.Id, x.Name, x.BankName, x.Currency, F(x.OpeningBalance), x.OpeningDate.ToString("yyyy-MM-dd"), F(x.OpeningBalance + (ledger.TryGetValue(x.Id, out var b) ? b : 0m)), x.IsArchived, x.Version)).ToArray();
        var cards = await db.Cards.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync();
        var confirmedAdjustments = await (from adjustment in db.StatementAdjustments
            join statement in db.CardStatements on new { adjustment.WorkspaceId, StatementId = adjustment.StatementId } equals new { statement.WorkspaceId, StatementId = statement.Id }
            where adjustment.WorkspaceId == ws.Id && statement.State == "Confirmed"
            select new { statement.CardId, statement.Currency, statement.Period, adjustment.Amount }).ToArrayAsync();
        var cardDtos = new List<object>();
        decimal cardDebtArs = 0, cardDebtUsd = 0, cardCreditArs = 0, cardCreditUsd = 0;
        foreach (var card in cards)
        {
            var installmentsForCard = await db.Installments.Where(x => x.WorkspaceId == ws.Id && x.CardId == card.Id).ToArrayAsync();
            var paymentsForCard = await db.CardPayments.Where(x => x.WorkspaceId == ws.Id && x.CardId == card.Id).ToArrayAsync();
            var ars = card.StatementDebtArs + installmentsForCard.Where(x => x.Currency == "ARS").Sum(x => x.Amount) + confirmedAdjustments.Where(x => x.CardId == card.Id && x.Currency == "ARS").Sum(x => x.Amount) - paymentsForCard.Where(x => x.Currency == "ARS").Sum(x => x.AppliedAmount);
            var usd = card.StatementDebtUsd + installmentsForCard.Where(x => x.Currency == "USD").Sum(x => x.Amount) + confirmedAdjustments.Where(x => x.CardId == card.Id && x.Currency == "USD").Sum(x => x.Amount) - paymentsForCard.Where(x => x.Currency == "USD").Sum(x => x.AppliedAmount);
            cardDebtArs += Math.Max(0, ars); cardDebtUsd += Math.Max(0, usd); cardCreditArs += Math.Max(0, -ars); cardCreditUsd += Math.Max(0, -usd);
            cardDtos.Add(new { id = card.Id, name = card.Name, bankName = card.BankName, closingDay = card.ClosingDay, dueDay = card.DueDay, statementDebtArs = F(card.StatementDebtArs), statementDebtUsd = F(card.StatementDebtUsd), asOfDate = card.AsOfDate.ToString("yyyy-MM-dd"), debtArs = F(Math.Max(0, ars)), debtUsd = F(Math.Max(0, usd)), creditArs = F(Math.Max(0, -ars)), creditUsd = F(Math.Max(0, -usd)), isArchived = card.IsArchived, version = card.Version });
        }
        var year = int.Parse(period![..4]); var month = int.Parse(period[5..7]);
        var ops = new List<object>();
        foreach (var op in await db.FinanceOperations.Where(x => x.WorkspaceId == ws.Id && x.Date.Year == year && x.Date.Month == month).ToArrayAsync()) ops.Add(await Operation(db, op));
        var installmentRows = await db.Installments.Where(x => x.WorkspaceId == ws.Id && x.Period == period).ToArrayAsync();
        var installments = installmentRows.Select(x => new { id = x.Id, purchaseId = x.PurchaseId, cardId = x.CardId, description = x.Description, currency = x.Currency, number = x.Number, count = x.Count, period = x.Period, amount = F(x.Amount), groupId = x.GroupId }).ToArray();
        var balanceArs = accounts.Where(x => x.Currency == "ARS").Sum(x => x.OpeningBalance) + ledgerRows.Where(x => x.Currency == "ARS").Sum(x => x.Amount);
        var balanceUsd = accounts.Where(x => x.Currency == "USD").Sum(x => x.OpeningBalance) + ledgerRows.Where(x => x.Currency == "USD").Sum(x => x.Amount);
        var expenses = await db.FinanceOperations.Where(x => x.WorkspaceId == ws.Id && x.Date.Year == year && x.Date.Month == month && x.Kind == "Expense").ToArrayAsync();
        var expenseArs = expenses.Where(x => x.Currency == "ARS").Sum(x => x.Amount); var expenseUsd = expenses.Where(x => x.Currency == "USD").Sum(x => x.Amount);
        var cardChargesArs = confirmedAdjustments.Where(x => x.Period == period && x.Currency == "ARS").Sum(x => x.Amount);
        var cardChargesUsd = confirmedAdjustments.Where(x => x.Period == period && x.Currency == "USD").Sum(x => x.Amount);
        var groupRows = new List<object>(); var receivableRows = new List<object>();
        foreach (var group in await db.Groups.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync())
        {
            var participants = await db.Participants.Where(x => x.WorkspaceId == ws.Id && x.GroupId == group.Id).Select(x => new { id = x.Id, name = x.Name, isSelf = x.IsSelf }).ToArrayAsync();
            groupRows.Add(new { id = group.Id, name = group.Name, version = group.Version, participants, rule = await RuleDto(db, ws.Id, group.Id, period) });
            // Use the same query contract for a group's projected and collectible shares.
            var shares = await GetReceivables(http, db, group.Id, period);
            if (shares is IValueHttpResult<List<object>> value && value.Value is not null) receivableRows.AddRange(value.Value);
        }
        return Results.Ok(new { period, accounts = accountDtos, cards = cardDtos, operations = ops, groups = groupRows, installments, receivables = receivableRows,
            budget = new { cashExpensesArs = F(expenseArs), cashExpensesUsd = F(expenseUsd), cardInstallmentsArs = F(installmentRows.Where(x => x.Currency == "ARS").Sum(x => x.Amount)), cardInstallmentsUsd = F(installmentRows.Where(x => x.Currency == "USD").Sum(x => x.Amount)), cardChargesArs = F(cardChargesArs), cardChargesUsd = F(cardChargesUsd) },
            totals = new { balanceArs = F(balanceArs), balanceUsd = F(balanceUsd), cardDebtArs = F(cardDebtArs), cardDebtUsd = F(cardDebtUsd), cardCreditArs = F(cardCreditArs), cardCreditUsd = F(cardCreditUsd) } });
    }
}
