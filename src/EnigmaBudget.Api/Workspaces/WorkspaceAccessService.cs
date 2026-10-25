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
        var permissions = WorkspacePermissions.For(membership.Role,
            membership.Workspace.Kind == "Personal" && membership.Role == "Owner" && membership.Workspace.OwnerUserId == user.Id);
        if (!permissions.Contains(requiredPermission)) return (null, StatusCodes.Status403Forbidden);
        if (membership.Workspace.IsArchived && !allowArchivedManagement && requiredPermission != WorkspacePermissions.Read && requiredPermission != WorkspacePermissions.ReadGroups)
            return (null, StatusCodes.Status403Forbidden);
        return (new WorkspaceRequestContext(user, membership.Workspace, membership.Role, permissions), 0);
    }

}
