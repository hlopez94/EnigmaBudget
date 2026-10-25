using System.Security.Claims;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Workspaces;

public sealed class WorkspaceAccessService(AppDbContext db, WorkspaceProvisioner provisioner)
{
    public async Task<(WorkspaceRequestContext? Context, int FailureStatus)> ResolveAsync(
        ClaimsPrincipal principal,
        string? selectedId,
        string requiredPermission,
        CancellationToken cancellationToken = default,
        bool allowArchivedManagement = false)
    {
        var personal = await provisioner.EnsurePersonalAsync(principal, cancellationToken);
        var workspaceId = personal.Id;
        if (!string.IsNullOrWhiteSpace(selectedId)
            && (!Guid.TryParse(selectedId, out workspaceId) || workspaceId == Guid.Empty))
            return (null, StatusCodes.Status404NotFound);

        var user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.Subject == CurrentIdentity.Subject(principal), cancellationToken);
        if (user is null) return (null, StatusCodes.Status404NotFound);
        var membership = await db.WorkspaceMemberships
            .Include(x => x.Workspace)
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == user.Id && x.Active, cancellationToken);
        if (membership is null) return (null, StatusCodes.Status404NotFound);
        return ResolveMembership(user, membership.Workspace, membership.Role, requiredPermission, allowArchivedManagement);
    }

    /// <summary>Rechecks the actor's permission after WorkspaceMutationGate has locked and refreshed the workspace row.</summary>
    public async Task<(WorkspaceRequestContext? Context, int FailureStatus)> ResolveWithinLockedWorkspaceAsync(
        ClaimsPrincipal principal,
        Data.Workspace workspace,
        string requiredPermission,
        CancellationToken cancellationToken = default,
        bool allowArchivedManagement = false)
    {
        var user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.Subject == CurrentIdentity.Subject(principal), cancellationToken);
        if (user is null) return (null, StatusCodes.Status404NotFound);
        var membership = await db.WorkspaceMemberships
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.UserId == user.Id && x.Active, cancellationToken);
        if (membership is null) return (null, StatusCodes.Status404NotFound);
        return ResolveMembership(user, workspace, membership.Role, requiredPermission, allowArchivedManagement);
    }

    private static (WorkspaceRequestContext? Context, int FailureStatus) ResolveMembership(
        ApplicationUser user,
        Data.Workspace workspace,
        string role,
        string requiredPermission,
        bool allowArchivedManagement)
    {
        var permissions = WorkspacePermissions.For(role,
            workspace.Kind == "Personal" && role == "Owner" && workspace.OwnerUserId == user.Id);
        if (!permissions.Contains(requiredPermission)) return (null, StatusCodes.Status403Forbidden);
        if (workspace.IsArchived && !allowArchivedManagement && requiredPermission != WorkspacePermissions.Read && requiredPermission != WorkspacePermissions.ReadGroups)
            return (null, StatusCodes.Status403Forbidden);
        return (new WorkspaceRequestContext(user, workspace, role, permissions), StatusCodes.Status200OK);
    }
}
