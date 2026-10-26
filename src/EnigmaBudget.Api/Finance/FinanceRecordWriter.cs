using System.Globalization;
using EnigmaBudget.Api.Data;

namespace EnigmaBudget.Api.Finance;

/// <summary>Low-level finance writers. The caller owns authorization, replay checks, transaction, save and commit.</summary>
internal static class FinanceRecordWriter
{
    internal static void AddOperation(AppDbContext db, FinanceOperation operation) => db.FinanceOperations.Add(operation);

    internal static void AddPayment(AppDbContext db, FinanceOperation operation, BankAccount account, DateOnly date,
        decimal amount, Guid paymentId, string? commandPayloadHash = null, Guid? ledgerEntryId = null)
    {
        var payment = new FinancePayment
        {
            Id = paymentId, WorkspaceId = operation.WorkspaceId, OperationId = operation.Id,
            AccountId = account.Id, Date = date, Amount = amount, Currency = account.Currency,
            CommandPayloadHash = commandPayloadHash
        };
        db.FinancePayments.Add(payment);
        db.LedgerEntries.Add(Entry(operation.WorkspaceId, account, date,
            operation.Kind == "Income" ? amount : -amount, "operation-payment", paymentId, ledgerEntryId));
    }

    internal static void AddPurchase(AppDbContext db, CardPurchase purchase, Func<int, Guid> installmentId)
    {
        db.Purchases.Add(purchase);
        var cents = decimal.ToInt64(purchase.TotalAmount * 100m);
        var first = DateOnly.ParseExact(purchase.FirstPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        for (var i = 0; i < purchase.InstallmentCount; i++)
        {
            var centsForInstallment = cents / purchase.InstallmentCount + (i < cents % purchase.InstallmentCount ? 1 : 0);
            var month = first.AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            db.Installments.Add(new CardInstallment
            {
                Id = installmentId(i + 1), WorkspaceId = purchase.WorkspaceId, PurchaseId = purchase.Id,
                CardId = purchase.CardId, GroupId = purchase.GroupId, Number = i + 1, Count = purchase.InstallmentCount,
                Period = month, Amount = centsForInstallment / 100m, Currency = purchase.Currency,
                Description = purchase.Description
            });
        }
    }

    internal static LedgerEntry Entry(Guid workspaceId, BankAccount account, DateOnly date, decimal amount, string source, Guid sourceId, Guid? entryId = null) =>
        new()
        {
            Id = entryId ?? Guid.NewGuid(), WorkspaceId = workspaceId, AccountId = account.Id, Date = date,
            Currency = account.Currency, Amount = amount, Source = source, SourceId = sourceId
        };
}
