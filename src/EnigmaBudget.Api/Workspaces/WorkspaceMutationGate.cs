using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EnigmaBudget.Api.Workspaces;

/// <summary>
/// Serializes household membership, invitation-seat, archive, and ownership mutations on the workspace row.
/// Callers must use the returned lease's transaction for their checks and writes, mark it changed, save, then commit.
/// </summary>
public sealed class WorkspaceMutationGate(AppDbContext db)
{
    public async Task<(WorkspaceMutationLease? Lease, int FailureStatus)> AcquireAsync(Guid workspaceId, CancellationToken cancellationToken = default, System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted)
    {
        var transaction = await db.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        try
        {
            var workspace = await db.Workspaces
                .FromSqlInterpolated($"SELECT * FROM \"Workspaces\" WHERE \"Id\" = {workspaceId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (workspace is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                return (null, StatusCodes.Status404NotFound);
            }

            // The EF context may already track an older copy from preflight resolution. Refresh while holding the row lock.
            await db.Entry(workspace).ReloadAsync(cancellationToken);
            return (new WorkspaceMutationLease(transaction, workspace), StatusCodes.Status200OK);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            throw;
        }
    }
}

public sealed class WorkspaceMutationLease(IDbContextTransaction transaction, Data.Workspace workspace) : IAsyncDisposable
{
    private bool _markedChanged;
    private bool _committed;

    public Data.Workspace Workspace { get; } = workspace;

    public void MarkChanged()
    {
        if (_markedChanged) throw new InvalidOperationException("A workspace mutation lease can be marked changed only once.");
        Workspace.Version++;
        _markedChanged = true;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (!_markedChanged) throw new InvalidOperationException("Mark the workspace mutation before committing the lease.");
        await transaction.CommitAsync(cancellationToken);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_committed) await transaction.RollbackAsync();
        await transaction.DisposeAsync();
    }
}
