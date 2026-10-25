using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Workspaces;

public sealed class PrivacyGrantService(AppDbContext db)
{
    public async Task<PersonalSharingDto[]> GetSharingAsync(Guid personalWorkspaceId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var households = await db.WorkspaceMemberships.AsNoTracking()
            .Where(x => x.UserId == ownerUserId && x.Active && x.Workspace.Kind == "Household")
            .Select(x => new { x.WorkspaceId, x.Workspace.Name })
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
        var result = new List<PersonalSharingDto>(households.Length);
        foreach (var household in households)
        {
            var grant = await db.PersonalReadGrants.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PersonalWorkspaceId == personalWorkspaceId && x.HouseholdWorkspaceId == household.WorkspaceId, cancellationToken);
            var active = await db.WorkspaceMemberships.AsNoTracking()
                .Where(x => x.WorkspaceId == household.WorkspaceId && x.Active && x.UserId != ownerUserId)
                .Select(x => new { x.UserId, x.ActivationGeneration })
                .ToArrayAsync(cancellationToken);
            var audience = grant?.Enabled == true
                ? await db.PersonalReadGrantAudience.AsNoTracking()
                    .Where(x => x.PersonalWorkspaceId == personalWorkspaceId && x.HouseholdWorkspaceId == household.WorkspaceId)
                    .ToDictionaryAsync(x => x.UserId, x => x.MembershipGeneration, cancellationToken)
                : new Dictionary<Guid, Guid>();
            var covered = active.Where(x => audience.TryGetValue(x.UserId, out var generation) && generation == x.ActivationGeneration)
                .Select(x => x.UserId).Order().ToArray();
            var needsRenewal = grant?.Enabled == true && active.Any(x => !audience.TryGetValue(x.UserId, out var generation) || generation != x.ActivationGeneration);
            result.Add(new PersonalSharingDto(household.WorkspaceId, household.Name, grant?.Enabled == true, grant?.Version ?? 1, covered, needsRenewal));
        }
        return result.ToArray();
    }

    public async Task RenewAudienceAsync(Guid personalWorkspaceId, Guid householdWorkspaceId, Guid ownerUserId, bool enabled, CancellationToken cancellationToken)
    {
        var grant = await db.PersonalReadGrants.SingleOrDefaultAsync(x => x.PersonalWorkspaceId == personalWorkspaceId && x.HouseholdWorkspaceId == householdWorkspaceId, cancellationToken);
        if (grant is null)
        {
            grant = new PersonalReadGrant { PersonalWorkspaceId = personalWorkspaceId, HouseholdWorkspaceId = householdWorkspaceId, Enabled = enabled, Version = 2 };
            db.PersonalReadGrants.Add(grant);
        }
        else
        {
            grant.Enabled = enabled;
            grant.Version++;
        }
        db.PersonalReadGrantAudience.RemoveRange(db.PersonalReadGrantAudience.Where(x => x.PersonalWorkspaceId == personalWorkspaceId && x.HouseholdWorkspaceId == householdWorkspaceId));
        if (enabled)
        {
            var members = await db.WorkspaceMemberships.AsNoTracking()
                .Where(x => x.WorkspaceId == householdWorkspaceId && x.Active && x.UserId != ownerUserId)
                .Select(x => new { x.UserId, x.ActivationGeneration })
                .ToArrayAsync(cancellationToken);
            db.PersonalReadGrantAudience.AddRange(members.Select(x => new PersonalReadGrantAudience
            {
                PersonalWorkspaceId = personalWorkspaceId,
                HouseholdWorkspaceId = householdWorkspaceId,
                UserId = x.UserId,
                MembershipGeneration = x.ActivationGeneration,
            }));
        }
    }
}
