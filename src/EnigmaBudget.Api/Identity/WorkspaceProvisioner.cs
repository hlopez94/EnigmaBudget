using System.Security.Claims;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Identity;

public sealed class WorkspaceProvisioner(AppDbContext db, TimeProvider clock)
{
    public Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        db.ApplicationUsers.SingleAsync(x => x.Subject == CurrentIdentity.Subject(principal), cancellationToken);

    public async Task<Workspace> EnsurePersonalAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var subject = CurrentIdentity.Subject(principal);
        var user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.Subject == subject, cancellationToken);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Subject = subject,
                DisplayName = principal.Identity?.Name,
                Email = principal.FindFirstValue(ClaimTypes.Email),
                EmailVerified = string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase),
                CreatedAt = clock.GetUtcNow(),
            };
            db.ApplicationUsers.Add(user);
        }

        var personal = await db.Workspaces
            .Include(x => x.Draft)
            .SingleOrDefaultAsync(x => x.OwnerSubject == subject, cancellationToken);
        if (personal is not null)
        {
            if (!await db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == personal.Id && x.UserId == user.Id && x.Active, cancellationToken))
            {
                db.WorkspaceMemberships.Add(new WorkspaceMembership { Workspace = personal, User = user, Role = "Owner", JoinedAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(cancellationToken);
            }
            return personal;
        }

        personal = new Workspace
        {
            OwnerSubject = subject,
            Name = "Personal",
            Kind = "Personal",
            OwnerUser = user,
            OwnerUserId = user.Id,
            Draft = new OnboardingDraft(),
        };
        db.Workspaces.Add(personal);
        db.WorkspaceMemberships.Add(new WorkspaceMembership { Workspace = personal, User = user, Role = "Owner", JoinedAt = clock.GetUtcNow() });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return personal;
        }
        catch (DbUpdateException ex) when (IsProvisioningCollision(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await db.Workspaces.Include(x => x.Draft).SingleOrDefaultAsync(x => x.OwnerSubject == subject, cancellationToken);
            if (winner is not null) return winner;
            throw;
        }
    }

    private static bool IsProvisioningCollision(DbUpdateException ex) =>
        ex.GetBaseException() is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName is "IX_Workspaces_OwnerSubject" or "IX_ApplicationUsers_Subject";
}
