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
        if (selectedId is not null
            && (!Guid.TryParse(selectedId, out workspaceId) || workspaceId == Guid.Empty))
            return (null, StatusCodes.Status404NotFound);

        var user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.Subject == CurrentIdentity.Subject(principal), cancellationToken);
        if (user is null) return (null, StatusCodes.Status404NotFound);
        var membership = await db.WorkspaceMemberships
            .Include(x => x.Workspace)
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == user.Id && x.Active, cancellationToken);
        if (membership is not null) return ResolveMembership(user, membership.Workspace, membership.Role, requiredPermission, allowArchivedManagement);
        var targetPersonal = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workspaceId && x.Kind == "Personal", cancellationToken);
        if (targetPersonal is null || targetPersonal.OwnerUserId == user.Id) return (null, StatusCodes.Status404NotFound);
        var hasGrant = await (
            from grant in db.PersonalReadGrants.AsNoTracking()
            join audience in db.PersonalReadGrantAudience.AsNoTracking()
                on new { grant.PersonalWorkspaceId, grant.HouseholdWorkspaceId } equals new { audience.PersonalWorkspaceId, audience.HouseholdWorkspaceId }
            join membershipRow in db.WorkspaceMemberships.AsNoTracking()
                on new { WorkspaceId = audience.HouseholdWorkspaceId, audience.UserId } equals new { membershipRow.WorkspaceId, membershipRow.UserId }
            where grant.PersonalWorkspaceId == workspaceId && grant.Enabled && audience.UserId == user.Id
                && membershipRow.Active && membershipRow.ActivationGeneration == audience.MembershipGeneration
            select grant.PersonalWorkspaceId
        ).AnyAsync(cancellationToken);
        if (!hasGrant) return (null, StatusCodes.Status404NotFound);
        if (requiredPermission != WorkspacePermissions.Read) return (null, StatusCodes.Status403Forbidden);
        var permissions = new HashSet<string>([WorkspacePermissions.Read], StringComparer.Ordinal);
        return (new WorkspaceRequestContext(user, targetPersonal, "Reader", permissions, IsSharedPersonalRead: true), StatusCodes.Status200OK);
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
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.UserId == user.Id && x.Active)
            .Select(x => x.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (membership is null) return (null, StatusCodes.Status404NotFound);
        return ResolveMembership(user, workspace, membership, requiredPermission, allowArchivedManagement);
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
