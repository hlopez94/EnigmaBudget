using System.Globalization;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Finance;

public sealed record CardObligationInput(
    PaymentCard Card,
    IReadOnlyList<CardInstallment> Installments,
    IReadOnlyList<CardStatement> Statements,
    IReadOnlyList<StatementAdjustment> Adjustments,
    IReadOnlyList<CardPaymentRecord> Payments);

public sealed record CardObligationComponent(
    Guid SourceId,
    string ComponentKind,
    string Currency,
    string Description,
    string? Period,
    DateOnly ComponentOrderDate,
    DateOnly? DueDate,
    string DateCertainty,
    decimal OriginalAmount,
    decimal AppliedAmount,
    decimal RemainingAmount,
    Guid? SummaryId);

public sealed record CardObligationTotals(string Currency, decimal Debt, decimal Credit);

public sealed record CardObligationResult(
    IReadOnlyList<CardObligationComponent> Components,
    IReadOnlyList<CardObligationTotals> Totals)
{
    public CardObligationTotals ForCurrency(string currency) =>
        Totals.FirstOrDefault(x => x.Currency == currency) ?? new(currency, 0m, 0m);
}

public sealed record CardStatementBreakdown(decimal Carry, decimal Expected, decimal Pending, decimal Credit);

/// <summary>Builds the one derived card liability stream used by operational views and projections.</summary>
public static class CardObligationCalculator
{
    private sealed class MutableComponent(Guid sourceId, string kind, string currency, string description,
        string? period, DateOnly orderDate, DateOnly? dueDate, string certainty, decimal original, Guid? summaryId)
    {
        public Guid SourceId { get; } = sourceId;
        public string Kind { get; } = kind;
        public string Currency { get; } = currency;
        public string Description { get; } = description;
        public string? Period { get; } = period;
        public DateOnly OrderDate { get; } = orderDate;
        public DateOnly? DueDate { get; } = dueDate;
        public string Certainty { get; } = certainty;
        public decimal Original { get; } = original;
        public Guid? SummaryId { get; } = summaryId;
        public decimal Applied { get; set; }
        public decimal Remaining { get; set; } = original;
    }

    public static async Task<CardObligationInput> LoadAsync(AppDbContext db, Guid workspaceId, Guid cardId,
        CancellationToken ct = default)
    {
        var card = await db.Cards.AsNoTracking().SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == cardId, ct);
        var installments = await db.Installments.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.CardId == cardId).ToArrayAsync(ct);
        var statements = await db.CardStatements.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.CardId == cardId).ToArrayAsync(ct);
        var statementIds = statements.Select(x => x.Id).ToArray();
        var adjustments = await db.StatementAdjustments.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && statementIds.Contains(x.StatementId)).ToArrayAsync(ct);
        var payments = await db.CardPayments.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.CardId == cardId).ToArrayAsync(ct);
        return new(card, installments, statements, adjustments, payments);
    }

    public static CardObligationResult Build(CardObligationInput input, DateOnly asOf)
    {
        var card = input.Card;
        var confirmed = input.Statements.Where(x => x.State == "Confirmed")
            .GroupBy(x => (x.Period, x.Currency)).ToDictionary(x => x.Key, x => x.OrderBy(y => y.Id).First());
        var components = new List<MutableComponent>();
        foreach (var currency in new[] { "ARS", "USD" })
        {
            var opening = currency == "ARS" ? card.StatementDebtArs : card.StatementDebtUsd;
            if (opening != 0m && card.AsOfDate <= asOf)
            {
                var period = card.AsOfDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                confirmed.TryGetValue((period, currency), out var statement);
                components.Add(new(card.Id, "OpeningDebt", currency, "Deuda inicial de tarjeta", period,
                    card.AsOfDate, statement?.DueDate, statement?.DueDate is null ? "Unknown" : "Confirmed",
                    opening, statement?.Id));
            }
        }

        foreach (var installment in input.Installments)
        {
            if (!TryPeriodStart(installment.Period, out var orderDate)) continue;
            confirmed.TryGetValue((installment.Period, installment.Currency), out var statement);
            var dueDate = statement?.DueDate ?? EstimatedDue(card, orderDate);
            var certainty = statement?.DueDate is not null ? "Confirmed" : dueDate is null ? "Unknown" : "Estimated";
            components.Add(new(installment.Id, "Installment", installment.Currency, installment.Description,
                installment.Period, orderDate, dueDate, certainty, installment.Amount, statement?.Id));
        }

        foreach (var adjustment in input.Adjustments)
        {
            var statement = input.Statements.FirstOrDefault(x => x.Id == adjustment.StatementId && x.State == "Confirmed");
            if (statement is null || !TryPeriodStart(statement.Period, out var orderDate)) continue;
            components.Add(new(adjustment.Id, "Adjustment", statement.Currency, adjustment.Description,
                statement.Period, orderDate, statement.DueDate, statement.DueDate is null ? "Unknown" : "Confirmed",
                adjustment.Amount, statement.Id));
        }

        var ordered = components.OrderBy(x => x.OrderDate)
            .ThenBy(x => KindOrder(x.Kind)).ThenBy(x => x.SourceId).ToArray();
        var payments = input.Payments.Where(x => x.Date <= asOf).OrderBy(x => x.Date).ThenBy(x => x.Id).ToArray();
        var results = new List<CardObligationComponent>(ordered.Length);
        var unusedPaymentCredits = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var currency in new[] { "ARS", "USD" })
        {
            var creditLots = ordered.Where(x => x.Currency == currency && x.Original < 0m)
                .Select(x => (Source: x, Amount: -x.Original)).ToArray();
            foreach (var item in ordered.Where(x => x.Currency == currency && x.Original > 0m))
            {
                for (var i = 0; i < creditLots.Length; i++)
                {
                    if (item.Remaining == 0m) break;
                    var credit = creditLots[i];
                    if (credit.Amount <= 0m || string.CompareOrdinal(credit.Source.Period, item.Period) > 0) continue;
                    var applied = Math.Min(item.Remaining, credit.Amount);
                    item.Remaining -= applied;
                    item.Applied += applied;
                    credit.Source.Applied -= applied;
                    credit.Source.Remaining += applied;
                    creditLots[i] = (credit.Source, credit.Amount - applied);
                }
            }
            var remainingPayments = payments.Where(x => x.Currency == currency).Sum(x => x.AppliedAmount);
            foreach (var item in ordered.Where(x => x.Currency == currency && x.Original > 0m))
            {
                if (remainingPayments <= 0m) break;
                var applied = Math.Min(item.Remaining, remainingPayments);
                item.Remaining -= applied;
                item.Applied += applied;
                remainingPayments -= applied;
            }
            unusedPaymentCredits[currency] = remainingPayments;
            foreach (var item in ordered.Where(x => x.Currency == currency))
                results.Add(new(item.SourceId, item.Kind, item.Currency, item.Description, item.Period,
                    item.OrderDate, item.DueDate, item.Certainty, item.Original, item.Applied,
                    item.Remaining, item.SummaryId));
        }
        var orderedResults = results.OrderBy(x => x.ComponentOrderDate).ThenBy(x => KindOrder(x.ComponentKind))
            .ThenBy(x => x.SourceId).ToArray();
        var totals = new[] { "ARS", "USD" }.Select(currency =>
        {
            var currencyRows = orderedResults.Where(x => x.Currency == currency).ToArray();
            var net = currencyRows.Sum(x => x.RemainingAmount) - unusedPaymentCredits.GetValueOrDefault(currency);
            return new CardObligationTotals(currency, Math.Max(0m, net), Math.Max(0m, -net));
        }).ToArray();
        return new(orderedResults, totals);
    }

    public static CardStatementBreakdown GetStatementBreakdown(CardObligationInput input, string period, DateOnly asOf)
    {
        var currency = input.Statements.FirstOrDefault(x => x.Period == period)?.Currency
            ?? input.Installments.FirstOrDefault(x => x.Period == period)?.Currency
            ?? input.Installments.FirstOrDefault()?.Currency
            ?? "ARS";
        return GetStatementBreakdown(input, period, asOf, currency);
    }

    public static CardStatementBreakdown GetStatementBreakdown(CardObligationInput input, string period, DateOnly asOf, string currency)
    {
        if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return new(0m, 0m, 0m, 0m);
        var statementIds = input.Statements.Where(x => string.CompareOrdinal(x.Period, period) <= 0).Select(x => x.Id).ToHashSet();
        var periodInput = input with
        {
            Installments = input.Installments.Where(x => string.CompareOrdinal(x.Period, period) <= 0).ToArray(),
            Statements = input.Statements.Where(x => statementIds.Contains(x.Id)).ToArray(),
            Adjustments = input.Adjustments.Where(x => statementIds.Contains(x.StatementId)).ToArray(),
        };
        var beforePeriod = Build(periodInput, start.AddDays(-1));
        var current = Build(periodInput, asOf);
        var carry = beforePeriod.Components.Where(x => x.Currency == currency && x.Period is not null
            && string.CompareOrdinal(x.Period, period) < 0).Sum(x => x.RemainingAmount);
        var expected = Math.Max(0m, beforePeriod.Components.Where(x => x.Currency == currency && x.Period is not null
            && string.CompareOrdinal(x.Period, period) <= 0).Sum(x => x.RemainingAmount));
        var pending = Math.Max(0m, current.Components.Where(x => x.Currency == currency && x.Period is not null
            && string.CompareOrdinal(x.Period, period) <= 0).Sum(x => x.RemainingAmount));
        var periodPayments = input.Payments.Where(x => x.Currency == currency && x.Date >= start && x.Date <= asOf)
            .Sum(x => x.AppliedAmount);
        // This period-cut credit is retained for the existing statement response. It is display metadata;
        // global debt and projection rows come only from Build's once-applied component remainders.
        var periodCredit = Math.Max(0m, periodPayments - expected);
        return new(carry, expected, pending, periodCredit);
    }

    private static DateOnly? EstimatedDue(PaymentCard card, DateOnly periodStart)
    {
        if (card.DueDay is not { } day) return null;
        return new DateOnly(periodStart.Year, periodStart.Month,
            Math.Min(day, DateTime.DaysInMonth(periodStart.Year, periodStart.Month)));
    }

    private static int KindOrder(string kind) => kind switch { "OpeningDebt" => 0, "Installment" => 1, _ => 2 };

    private static bool TryPeriodStart(string period, out DateOnly date) =>
        DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
