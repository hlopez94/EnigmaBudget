using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EnigmaBudget.Api.Workspaces;

public sealed class WorkspaceAuditInterceptor(IHttpContextAccessor httpContextAccessor, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> FinancialTypes = new(StringComparer.Ordinal)
    {
        "BankAccount", "PaymentCard", "OpeningBalanceRecord", "FinanceOperation", "FinancePayment",
        "LedgerEntry", "TransferRecord", "SharedGroup", "GroupParticipant", "MonthlyRule", "RuleWeight",
        "CardPurchase", "CardInstallment", "SharedCollection", "CardStatement", "StatementAdjustment", "CardPaymentRecord",
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var db = eventData.Context;
        var context = httpContextAccessor.HttpContext is { } http ? FinanceWorkspaceFilter.Get(http) : null;
        if (db is not null && context is not null)
        {
            var events = db.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Where(entry => FinancialTypes.Contains(entry.Metadata.ClrType.Name))
                .Select(entry => new WorkspaceAuditEvent
                {
                    WorkspaceId = context.Workspace.Id,
                    ActorUserId = context.User.Id,
                    Action = $"finance.{entry.Metadata.ClrType.Name.ToLowerInvariant()}.{entry.State.ToString().ToLowerInvariant()}",
                    ResourceType = entry.Metadata.ClrType.Name,
                    ResourceId = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id")?.CurrentValue?.ToString(),
                    OccurredAt = clock.GetUtcNow(),
                    ChangesJson = "{}",
                }).ToArray();
            if (events.Length > 0) db.Set<WorkspaceAuditEvent>().AddRange(events);
        }
        return ValueTask.FromResult(result);
    }
}
