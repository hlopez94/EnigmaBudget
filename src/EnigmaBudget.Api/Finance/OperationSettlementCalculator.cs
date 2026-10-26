using EnigmaBudget.Api.Data;

namespace EnigmaBudget.Api.Finance;

internal sealed record OperationSettlementCalculation(
    decimal GrossConsumption,
    decimal CashPaidAmount,
    decimal NonCashSettledAmount,
    decimal CardChargeSettledAmount,
    decimal TotalSettledAmount,
    decimal PendingAmount,
    decimal OperationMonthBudgetContribution,
    string Status
);

internal static class OperationSettlementCalculator
{
    internal static OperationSettlementCalculation Calculate(
        FinanceOperation operation,
        IReadOnlyList<FinancePayment> cashPayments,
        IReadOnlyList<NonCashSettlement> nonCashSettlements)
    {
        if (cashPayments.Any(payment => payment.WorkspaceId != operation.WorkspaceId || payment.OperationId != operation.Id || payment.Currency != operation.Currency))
            throw new InvalidOperationException("Cash payments must belong to this operation and currency.");
        if (nonCashSettlements.Any(settlement => settlement.WorkspaceId != operation.WorkspaceId || settlement.OperationId != operation.Id || settlement.Currency != operation.Currency))
            throw new InvalidOperationException("Noncash settlements must belong to this operation and currency.");

        var cashPaid = cashPayments.Sum(payment => payment.Amount);
        if (operation.IsVoided)
            return new(0m, cashPaid, 0m, 0m, 0m, 0m, 0m, "Voided");

        var roots = nonCashSettlements.Where(settlement => settlement.Kind != "Reversal").ToArray();
        var reversals = nonCashSettlements.Where(settlement => settlement.Kind == "Reversal").ToArray();
        var amountsByRoot = roots.ToDictionary(settlement => settlement.Id, settlement => settlement.Amount);
        foreach (var reversal in reversals)
        {
            if (reversal.ReversesSettlementId is not { } rootId || !amountsByRoot.TryGetValue(rootId, out var rootAmount))
                throw new InvalidOperationException("A reversal must reference a settlement root for this operation.");
            if (reversal.Amount != rootAmount)
                throw new InvalidOperationException("A reversal must fully compensate its root settlement.");
            amountsByRoot[rootId] = 0m;
        }

        var netSettlementAmounts = roots.ToDictionary(root => root.Id, root => amountsByRoot[root.Id]);
        if (netSettlementAmounts.Values.Any(amount => amount < 0m))
            throw new InvalidOperationException("Reversals cannot exceed their source settlement.");

        var nonCash = netSettlementAmounts.Values.Sum();
        var cardCharge = roots.Where(root => root.Kind == "CardCharge").Sum(root => netSettlementAmounts[root.Id]);
        var total = cashPaid + nonCash;
        if (operation.Kind == "Income" && nonCash != 0m)
            throw new InvalidOperationException("Income operations permit cash settlement only.");
        if (total > operation.Amount)
            throw new InvalidOperationException("The operation cannot be settled above its amount.");

        var gross = operation.Kind == "Expense" ? operation.Amount : 0m;
        var pending = operation.Amount - total;
        var contribution = operation.Kind == "Expense" ? operation.Amount - cardCharge : 0m;
        var status = total == 0m ? "Pending" : total < operation.Amount ? "Partial" : "Settled";
        return new(gross, cashPaid, nonCash, cardCharge, total, pending, contribution, status);
    }
}
