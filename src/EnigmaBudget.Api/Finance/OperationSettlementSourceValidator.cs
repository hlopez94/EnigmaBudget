using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EnigmaBudget.Api.Finance;

internal static class OperationSettlementSourceValidator
{
    internal static async Task<bool> ValidateAsync(
        AppDbContext db,
        FinanceOperation operation,
        NonCashSettlement settlement,
        CancellationToken ct)
    {
        if (operation.Kind != "Expense"
            || settlement.WorkspaceId != operation.WorkspaceId
            || settlement.OperationId != operation.Id
            || settlement.Currency != operation.Currency
            || settlement.Amount <= 0m)
            return false;

        return settlement.Kind switch
        {
            "PayrollWithholding" => await ValidatePayrollAsync(db, operation, settlement, ct),
            "CardCharge" => await ValidateCardChargeAsync(db, operation, settlement, ct),
            "Reversal" => await ValidateReversalAsync(db, operation, settlement, ct),
            _ => false
        };
    }

    private static async Task<bool> ValidatePayrollAsync(
        AppDbContext db,
        FinanceOperation operation,
        NonCashSettlement settlement,
        CancellationToken ct)
    {
        if (settlement.HealthcareComponentId is not { } componentId
            || settlement.CardPurchaseId is not null
            || settlement.ReversesSettlementId is not null)
            return false;

        var component = Added(db.ChangeTracker, (HealthcareComponent row) =>
            row.WorkspaceId == operation.WorkspaceId && row.Id == componentId)
            ?? await db.HealthcareComponents.AsNoTracking().SingleOrDefaultAsync(
                row => row.WorkspaceId == operation.WorkspaceId && row.Id == componentId, ct);
        if (component is null
            || component.WorkspaceId != operation.WorkspaceId
            || component.OccurrenceActionId is not { } actionId
            || component.Kind != "PayrollWithholding"
            || !component.AppliesToProviderBalance
            || component.Amount != settlement.Amount)
            return false;

        var action = Added(db.ChangeTracker, (RecurringOccurrenceAction row) =>
            row.WorkspaceId == operation.WorkspaceId && row.Id == actionId)
            ?? await db.RecurringOccurrenceActions.AsNoTracking().SingleOrDefaultAsync(
                row => row.WorkspaceId == operation.WorkspaceId && row.Id == actionId, ct);
        if (action is null
            || action.WorkspaceId != operation.WorkspaceId
            || action.RuleRevisionId != component.RuleRevisionId
            || action.Outcome != "Recorded"
            || action.FinanceOperationId != operation.Id)
            return false;

        var revision = Added(db.ChangeTracker, (RecurringRuleRevision row) =>
            row.WorkspaceId == operation.WorkspaceId && row.Id == component.RuleRevisionId)
            ?? await db.RecurringRuleRevisions.AsNoTracking().SingleOrDefaultAsync(
                row => row.WorkspaceId == operation.WorkspaceId && row.Id == component.RuleRevisionId, ct);
        return revision is not null
            && revision.WorkspaceId == operation.WorkspaceId
            && revision.Currency == operation.Currency
            && revision.Flow == "Expense";
    }

    private static async Task<bool> ValidateCardChargeAsync(
        AppDbContext db,
        FinanceOperation operation,
        NonCashSettlement settlement,
        CancellationToken ct)
    {
        if (settlement.CardPurchaseId is not { } purchaseId
            || settlement.HealthcareComponentId is not null
            || settlement.ReversesSettlementId is not null)
            return false;

        var purchase = Added(db.ChangeTracker, (CardPurchase row) =>
            row.WorkspaceId == operation.WorkspaceId && row.Id == purchaseId)
            ?? await db.Purchases.AsNoTracking().SingleOrDefaultAsync(
                row => row.WorkspaceId == operation.WorkspaceId && row.Id == purchaseId, ct);
        return purchase is not null
            && purchase.WorkspaceId == operation.WorkspaceId
            && purchase.ExpenseOperationId == operation.Id
            && purchase.Currency == operation.Currency
            && purchase.Currency == settlement.Currency
            && purchase.CategoryId == operation.CategoryId
            && purchase.TotalAmount == settlement.Amount;
    }

    private static async Task<bool> ValidateReversalAsync(
        AppDbContext db,
        FinanceOperation operation,
        NonCashSettlement settlement,
        CancellationToken ct)
    {
        if (settlement.ReversesSettlementId is not { } rootId
            || settlement.HealthcareComponentId is not null
            || settlement.CardPurchaseId is not null)
            return false;

        var root = Added(db.ChangeTracker, (NonCashSettlement row) =>
            row.WorkspaceId == operation.WorkspaceId && row.Id == rootId)
            ?? await db.NonCashSettlements.AsNoTracking().SingleOrDefaultAsync(
                row => row.WorkspaceId == operation.WorkspaceId && row.Id == rootId, ct);
        if (root is null
            || root.WorkspaceId != operation.WorkspaceId
            || root.OperationId != operation.Id
            || root.Currency != operation.Currency
            || root.Currency != settlement.Currency
            || root.Amount != settlement.Amount
            || root.ReversesSettlementId is not null
            || root.Kind is not ("PayrollWithholding" or "CardCharge"))
            return false;

        return await ValidateAsync(db, operation, root, ct);
    }

    private static T? Added<T>(ChangeTracker tracker, Func<T, bool> matches) where T : class =>
        tracker.Entries<T>()
            .Where(entry => entry.State == EntityState.Added && matches(entry.Entity))
            .Select(entry => entry.Entity)
            .SingleOrDefault();
}
