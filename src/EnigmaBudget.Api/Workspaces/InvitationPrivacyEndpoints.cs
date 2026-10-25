using System.Net.Mail;
using System.Security.Claims;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Workspaces;

public static class InvitationPrivacyEndpoints
{
    public static void MapInvitationPrivacyEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api").RequireAuthorization();
        api.MapPost("/workspaces/{id:guid}/invitations", CreateInvitation);
        api.MapGet("/workspaces/{id:guid}/invitations", ListInvitations);
        api.MapDelete("/workspaces/{id:guid}/invitations/{invitationId:guid}", RevokeInvitation);
        api.MapPost("/invitations/preview", PreviewInvitation);
        api.MapPost("/invitations/accept", AcceptInvitation);
        api.MapGet("/personal-sharing", GetPersonalSharing);
        api.MapPut("/personal-sharing/{householdId:guid}", UpdatePersonalSharing);
    }

    private static async Task<IResult> CreateInvitation(HttpContext http, AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, InvitationService invitations, IAntiforgery csrf, Guid id, CreateInvitationRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        if (request.Id == Guid.Empty || !ValidEmail(request.Email) || request.Role is not ("Admin" or "Member" or "Reader")) return Problem(400, "Revisá el correo y el rol de invitación.");
        var (preflight, failure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (preflight is null) return AccessFailure(failure);
        if (preflight.Workspace.Kind != "Household") return Problem(404, "El recurso no existe o no está disponible.");
        var (lease, lockFailure) = await gate.AcquireAsync(id, http.RequestAborted);
        if (lease is null) return AccessFailure(lockFailure);
        await using var mutation = lease;
        var (context, actorFailure) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (context is null) return AccessFailure(actorFailure);
        if (context.Workspace.Kind != "Household") return Problem(404, "El recurso no existe o no está disponible.");
        var normalized = InvitationService.NormalizeEmail(request.Email);
        var existing = await db.WorkspaceInvitations.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.Id == request.Id, http.RequestAborted);
        if (existing is not null)
        {
            if (existing.Email != normalized || existing.Role != request.Role) return Problem(409, "El identificador de invitación ya se usa con otros datos.");
            return Results.Ok(ToDto(existing, shareUrl: null));
        }
        var now = invitations.Now;
        if (mutation.Workspace.IsArchived) return Problem(403, "El Hogar está archivado.");
        var reserved = await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == id && x.Active, http.RequestAborted)
            + await db.WorkspaceInvitations.CountAsync(x => x.WorkspaceId == id && x.Status == "Pending" && x.ExpiresAt > now, http.RequestAborted);
        if (reserved >= 4) return Problem(409, "El Hogar ya alcanzó el límite de cuatro integrantes e invitaciones pendientes.");
        var (token, hash) = InvitationService.CreateToken();
        var invitation = new WorkspaceInvitation { Id = request.Id, WorkspaceId = id, Email = normalized, Role = request.Role, TokenHash = hash, Status = "Pending", ExpiresAt = invitations.ExpiresAt };
        db.WorkspaceInvitations.Add(invitation);
        db.WorkspaceAuditEvents.Add(Audit(context.User.Id, id, "invitation.created", invitation.Id));
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); }
        catch (DbUpdateException) { return Problem(409, "No se pudo crear la invitación. Actualizá y volvé a intentar."); }
        return Results.Ok(ToDto(invitation, "/#invite=" + token));
    }

    private static async Task<IResult> ListInvitations(HttpContext http, AppDbContext db, WorkspaceAccessService access, InvitationService invitations, Guid id)
    {
        var (context, failure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (context is null) return AccessFailure(failure);
        if (context.Workspace.Kind != "Household") return Problem(404, "El recurso no existe o no está disponible.");
        var rows = await db.WorkspaceInvitations.AsNoTracking().Where(x => x.WorkspaceId == id)
            .OrderByDescending(x => x.ExpiresAt).Select(x => new InvitationDto(x.Id, x.Email, x.Role,
                x.Status == "Pending" && x.ExpiresAt <= invitations.Now ? "Expired" : x.Status, x.ExpiresAt, x.Version, null))
            .ToArrayAsync(http.RequestAborted);
        return Results.Ok(rows);
    }

    private static async Task<IResult> RevokeInvitation(HttpContext http, HttpRequest request, AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, Guid id, Guid invitationId)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        var body = await request.ReadFromJsonAsync<InvitationMutationRequest>(http.RequestAborted);
        if (body is null) return Results.BadRequest();
        var (preflight, failure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (preflight is null) return AccessFailure(failure);
        var (lease, lockFailure) = await gate.AcquireAsync(id, http.RequestAborted);
        if (lease is null) return AccessFailure(lockFailure);
        await using var mutation = lease;
        var (context, actorFailure) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (context is null) return AccessFailure(actorFailure);
        if (context.Workspace.Kind != "Household") return NotFound();
        var invitation = await db.WorkspaceInvitations.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.Id == invitationId, http.RequestAborted);
        if (invitation is null || invitation.Status is "Accepted" or "Revoked") return NotFound();
        if (invitation.Version != body.Version) return Problem(409, "La invitación cambió. Actualizá la pantalla.");
        invitation.Status = "Revoked"; invitation.Version++;
        db.WorkspaceAuditEvents.Add(Audit(context.User.Id, id, "invitation.revoked", invitation.Id));
        mutation.MarkChanged();
        await db.SaveChangesAsync(http.RequestAborted);
        await mutation.CommitAsync(http.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> PreviewInvitation(HttpContext http, AppDbContext db, InvitationService invitations, InvitationRequest request)
    {
        if (!VerifiedEmail(http.User, out var email)) return Problem(403, "Volvé a iniciar sesión con un correo verificado de Google.");
        var hash = InvitationService.HashToken(request.Token ?? "");
        if (hash.Length == 0) return NotFound();
        var now = invitations.Now;
        var invite = await db.WorkspaceInvitations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash.SequenceEqual(hash), http.RequestAborted);
        if (invite is null || invite.Status != "Pending" || invite.ExpiresAt <= now || invite.Email != InvitationService.NormalizeEmail(email)) return NotFound();
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(x => x.Id == invite.WorkspaceId, http.RequestAborted);
        if (workspace.IsArchived) return NotFound();
        return Results.Ok(new { workspaceId = workspace.Id, workspaceName = workspace.Name, role = invite.Role, expiresAt = invite.ExpiresAt });
    }

    private static async Task<IResult> AcceptInvitation(HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner, WorkspaceMutationGate gate, InvitationService invitations, IAntiforgery csrf, InvitationRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        if (!VerifiedEmail(http.User, out var email)) return Problem(403, "Volvé a iniciar sesión con un correo verificado de Google.");
        var hash = InvitationService.HashToken(request.Token ?? "");
        if (hash.Length == 0) return NotFound();
        await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
        var actor = await provisioner.GetUserAsync(http.User, http.RequestAborted);
        var invitationHeader = await db.WorkspaceInvitations.AsNoTracking().Where(x => x.TokenHash.SequenceEqual(hash))
            .Select(x => new { x.WorkspaceId }).SingleOrDefaultAsync(http.RequestAborted);
        if (invitationHeader is null) return NotFound();
        var (lease, failure) = await gate.AcquireAsync(invitationHeader.WorkspaceId, http.RequestAborted);
        if (lease is null) return AccessFailure(failure);
        await using var mutation = lease;
        var invitation = await db.WorkspaceInvitations.SingleOrDefaultAsync(x => x.WorkspaceId == invitationHeader.WorkspaceId && x.TokenHash.SequenceEqual(hash), http.RequestAborted);
        if (invitation is null || invitation.Email != InvitationService.NormalizeEmail(email)) return Problem(403, "La invitación no corresponde al correo verificado de esta sesión.");
        var membership = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == mutation.Workspace.Id && x.UserId == actor.Id, http.RequestAborted);
        if (invitation.Status == "Accepted")
        {
            if (invitation.AcceptedByUserId == actor.Id && membership is { Active: true } && membership.ActivationGeneration == invitation.AcceptedMembershipGeneration)
                return Results.Ok(ToAccepted(mutation.Workspace, membership.Role, membership.Version,
                    await db.ApplicationUsers.AsNoTracking().Where(x => x.Id == mutation.Workspace.OwnerUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(http.RequestAborted)));
            return NotFound();
        }
        if (invitation.Status != "Pending" || invitation.ExpiresAt <= invitations.Now || mutation.Workspace.IsArchived) return NotFound();
        var wasActive = membership is { Active: true };
        var now = invitations.Now;
        var occupied = await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == mutation.Workspace.Id && x.Active, http.RequestAborted)
            + await db.WorkspaceInvitations.CountAsync(x => x.WorkspaceId == mutation.Workspace.Id && x.Status == "Pending" && x.ExpiresAt > now && x.Id != invitation.Id, http.RequestAborted);
        if (!wasActive && occupied >= 4) return Problem(409, "El Hogar ya alcanzó el límite de cuatro integrantes.");
        if (membership is { Active: false })
        {
            membership.Active = true; membership.Role = invitation.Role; membership.Version++; membership.JoinedAt = invitations.Now; membership.ActivationGeneration = Guid.NewGuid();
        }
        else if (membership is null)
        {
            membership = new WorkspaceMembership { WorkspaceId = mutation.Workspace.Id, UserId = actor.Id, Role = invitation.Role, Active = true, JoinedAt = invitations.Now, ActivationGeneration = Guid.NewGuid() };
            db.WorkspaceMemberships.Add(membership);
        }
        else if (membership.Role != "Owner" && membership.Role != invitation.Role)
        {
            membership.Role = invitation.Role; membership.Version++;
        }
        invitation.Status = "Accepted";
        invitation.AcceptedByUserId = actor.Id;
        invitation.AcceptedMembershipGeneration = membership.ActivationGeneration;
        invitation.Version++;
        db.WorkspaceAuditEvents.Add(Audit(actor.Id, mutation.Workspace.Id, "invitation.accepted", invitation.Id));
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); }
        catch (DbUpdateException) { return Problem(409, "La invitación ya cambió o el Hogar alcanzó su capacidad."); }
        return Results.Ok(ToAccepted(mutation.Workspace, membership.Role, membership.Version,
            await db.ApplicationUsers.AsNoTracking().Where(x => x.Id == mutation.Workspace.OwnerUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(http.RequestAborted)));
    }

    private static async Task<IResult> GetPersonalSharing(HttpContext http, WorkspaceProvisioner provisioner, PrivacyGrantService grants)
    {
        var personal = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
        return Results.Ok(await grants.GetSharingAsync(personal.Id, personal.OwnerUserId, http.RequestAborted));
    }

    private static async Task<IResult> UpdatePersonalSharing(HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner, PrivacyGrantService grants, WorkspaceMutationGate gate, IAntiforgery csrf, Guid householdId, UpdatePersonalSharingRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        var personal = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
        var ownerId = personal.OwnerUserId;
        var (lease, failure) = await gate.AcquireAsync(householdId, http.RequestAborted);
        if (lease is null) return AccessFailure(failure);
        await using var mutation = lease;
        var household = mutation.Workspace;
        if (household.Kind != "Household") return NotFound();
        var ownerMembership = await db.WorkspaceMemberships.AsNoTracking().Where(x => x.WorkspaceId == householdId && x.UserId == ownerId && x.Active).Select(x => new { x.UserId }).SingleOrDefaultAsync(http.RequestAborted);
        if (ownerMembership is null) return Problem(403, "Solo quien es titular del Personal y miembro activo del Hogar puede cambiar este consentimiento.");
        var grant = await db.PersonalReadGrants.SingleOrDefaultAsync(x => x.PersonalWorkspaceId == personal.Id && x.HouseholdWorkspaceId == householdId, http.RequestAborted);
        var currentVersion = grant?.Version ?? 1;
        if (request.Version != currentVersion) return Problem(409, "El consentimiento cambió. Actualizá la pantalla.");
        if (request.Enabled && household.IsArchived) return Problem(403, "No se puede activar el consentimiento en un Hogar archivado.");
        if (grant is not null && grant.Enabled == request.Enabled && !request.Enabled) return Results.Ok((await grants.GetSharingAsync(personal.Id, ownerId, http.RequestAborted)).Single(x => x.HouseholdId == householdId));
        await grants.RenewAudienceAsync(personal.Id, householdId, ownerId, request.Enabled, http.RequestAborted);
        db.WorkspaceAuditEvents.Add(Audit(ownerId, householdId, request.Enabled ? "personal_read_grant.enabled" : "personal_read_grant.revoked", personal.Id));
        mutation.MarkChanged();
        await db.SaveChangesAsync(http.RequestAborted);
        await mutation.CommitAsync(http.RequestAborted);
        return Results.Ok((await grants.GetSharingAsync(personal.Id, ownerId, http.RequestAborted)).Single(x => x.HouseholdId == householdId));
    }

    private static InvitationDto ToDto(WorkspaceInvitation invitation, string? shareUrl) => new(invitation.Id, invitation.Email, invitation.Role, invitation.Status, invitation.ExpiresAt, invitation.Version, shareUrl);
    private static WorkspaceDto ToAccepted(Workspace workspace, string role, int membershipVersion, string? ownerDisplayName)
    {
        var permissions = WorkspacePermissions.For(role, personalOwner: false);
        return new WorkspaceDto(workspace.Id, workspace.Name, ownerDisplayName, membershipVersion, workspace.Kind, role, false, workspace.IsArchived, workspace.Version,
            new(permissions.Contains(WorkspacePermissions.Read), permissions.Contains(WorkspacePermissions.ReadGroups), permissions.Contains(WorkspacePermissions.RecordFinance), permissions.Contains(WorkspacePermissions.ManageResources), permissions.Contains(WorkspacePermissions.CorrectHistory), permissions.Contains(WorkspacePermissions.ManageMembers), permissions.Contains(WorkspacePermissions.ManagePrivacy)));
    }
    private static WorkspaceAuditEvent Audit(Guid actor, Guid workspaceId, string action, Guid resourceId) => new() { WorkspaceId = workspaceId, ActorUserId = actor, Action = action, ResourceType = "WorkspaceInvitation", ResourceId = resourceId.ToString(), OccurredAt = DateTimeOffset.UtcNow };
    private static bool ValidEmail(string? email) => !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= 320 && MailAddress.TryCreate(email.Trim(), out var address) && address.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool VerifiedEmail(ClaimsPrincipal principal, out string email)
    {
        email = principal.FindFirstValue(ClaimTypes.Email) ?? "";
        return string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(email);
    }
    private static async Task<bool> ValidCsrf(HttpContext http, IAntiforgery csrf) { try { await csrf.ValidateRequestAsync(http); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static IResult AccessFailure(int status) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: status == 404 ? "El espacio no existe o no está disponible." : "No tenés permiso para realizar esta acción en el espacio.");
    private static IResult NotFound() => Results.Problem(statusCode: 404, title: "No se pudo completar la solicitud", detail: "El recurso no existe o no está disponible.");
    private static IResult Problem(int status, string detail) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: detail);
}
