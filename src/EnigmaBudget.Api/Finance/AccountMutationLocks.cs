using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Finance;

/// <summary>Serializes ledger mutations and savings availability reads on bank accounts.</summary>
public static class AccountMutationLocks
{
    public static async Task LockAsync(AppDbContext db, Guid workspaceId, IEnumerable<Guid> accountIds, CancellationToken cancellationToken = default)
    {
        var ordered = accountIds.Distinct().OrderBy(id => id).ToArray();
        if (ordered.Length == 0) return;
        // Match savings' workspace-before-account order. KEY SHARE does not mutate the
        // workspace but prevents its foreign-key lock from being acquired after an account lock.
        _ = await db.Workspaces.FromSqlInterpolated($"SELECT * FROM \"Workspaces\" WHERE \"Id\" = {workspaceId} FOR KEY SHARE")
            .AsNoTracking().ToListAsync(cancellationToken);
        foreach (var accountId in ordered)
            _ = await db.Accounts.FromSqlInterpolated($"SELECT * FROM \"Accounts\" WHERE \"WorkspaceId\" = {workspaceId} AND \"Id\" = {accountId} FOR UPDATE")
                .AsNoTracking().ToListAsync(cancellationToken);
    }
}
