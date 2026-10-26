using System.Security.Claims;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using EnigmaBudget.Api.Finance;

namespace EnigmaBudget.Api.Workspaces;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api").RequireAuthorization();
        api.MapGet("/me", Me);
        api.MapGet("/workspaces", List);
        api.MapPost("/workspaces", Create);
        api.MapGet("/workspaces/{id:guid}", Get);
        api.MapPut("/workspaces/{id:guid}", Update);
        api.MapGet("/workspaces/{id:guid}/members", Members);
        api.MapPut("/workspaces/{id:guid}/members/{userId:guid}", ChangeMember);
        api.MapDelete("/workspaces/{id:guid}/members/{userId:guid}", RemoveMember);
        api.MapPost("/workspaces/{id:guid}/leave", Leave);
        api.MapPost("/workspaces/{id:guid}/owner-transfer", TransferOwner);
        api.MapGet("/workspaces/{id:guid}/audit", Audit);
        routes.MapInvitationPrivacyEndpoints();
    }

    private static async Task<IResult> Me(HttpContext http, WorkspaceProvisioner provisioner)
    {
        var personal = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
        var user = personal.OwnerUser;
        if (user is null)
            user = await provisioner.GetUserAsync(http.User, http.RequestAborted);
        return Results.Ok(new MeDto(user.Id, user.DisplayName ?? http.User.Identity?.Name ?? "", user.Email, user.EmailVerified, personal.Id));
    }

    private static async Task<IResult> List(HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner)
    {
        var personal = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
        var rows = await db.WorkspaceMemberships.AsNoTracking().Include(x => x.Workspace).ThenInclude(x => x.OwnerUser)
            .Where(x => x.UserId == personal.OwnerUserId && x.Active)
            .OrderBy(x => x.Workspace.Kind == "Personal" ? 0 : 1).ThenBy(x => x.Workspace.Name)
            .ToArrayAsync(http.RequestAborted);
        var result = rows.Select(x => ToDto(x.Workspace, x.Role, personal.OwnerUserId, membershipVersion: x.Version)).ToList();
        var shared = await (
            from grant in db.PersonalReadGrants.AsNoTracking()
            join audience in db.PersonalReadGrantAudience.AsNoTracking()
                on new { grant.PersonalWorkspaceId, grant.HouseholdWorkspaceId } equals new { audience.PersonalWorkspaceId, audience.HouseholdWorkspaceId }
            join member in db.WorkspaceMemberships.AsNoTracking()
                on new { WorkspaceId = audience.HouseholdWorkspaceId, audience.UserId } equals new { member.WorkspaceId, member.UserId }
            join workspace in db.Workspaces.AsNoTracking() on grant.PersonalWorkspaceId equals workspace.Id
            where grant.Enabled && audience.UserId == personal.OwnerUserId && member.Active && member.ActivationGeneration == audience.MembershipGeneration
            select new { Workspace = workspace, OwnerDisplayName = workspace.OwnerUser.DisplayName }
        ).ToArrayAsync(http.RequestAborted);
        foreach (var workspace in shared)
            if (result.All(x => x.Id != workspace.Workspace.Id)) result.Add(ToDto(workspace.Workspace, "Reader", personal.OwnerUserId, sharedPersonalRead: true, ownerDisplayName: workspace.OwnerDisplayName));
        return Results.Ok(result);
    }

    private static async Task<IResult> Create(HttpContext http, AppDbContext db, WorkspaceProvisioner provisioner, IAntiforgery csrf, CreateWorkspaceRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        if (request.Id == Guid.Empty || request.Kind != "Household" || !ValidName(request.Name)) return Problem(400, "Revisá el tipo y el nombre del espacio.");
        var personal = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
        var existing = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == request.Id, http.RequestAborted);
        if (existing is not null)
        {
            if (existing.Kind == request.Kind && existing.Name == request.Name.Trim() && existing.OwnerUserId == personal.OwnerUserId) return Results.Ok(ToDto(existing, "Owner", personal.OwnerUserId, ownerDisplayName: personal.OwnerUser.DisplayName, membershipVersion: 1));
            return Problem(409, "El identificador ya se usa con otros datos.");
        }
        var workspace = new Workspace { Id = request.Id, Name = request.Name.Trim(), Kind = "Household", OwnerUserId = personal.OwnerUserId, OwnerUser = personal.OwnerUser, IsArchived = false };
        db.Workspaces.Add(workspace);
        db.WorkspaceMemberships.Add(new WorkspaceMembership { Workspace = workspace, UserId = personal.OwnerUserId, Role = "Owner", JoinedAt = DateTimeOffset.UtcNow });
        db.WorkspaceAuditEvents.Add(new WorkspaceAuditEvent { WorkspaceId = workspace.Id, ActorUserId = personal.OwnerUserId, Action = "workspace.created", ResourceType = "Workspace", ResourceId = workspace.Id.ToString(), OccurredAt = DateTimeOffset.UtcNow });
        CatalogDefaults.AddForWorkspace(db, workspace.Id);
        try { await db.SaveChangesAsync(http.RequestAborted); }
        catch (DbUpdateException) { return Problem(409, "No se pudo crear el espacio. Actualizá y volvé a intentar."); }
        return Results.Created($"/api/workspaces/{workspace.Id}", ToDto(workspace, "Owner", personal.OwnerUserId, ownerDisplayName: personal.OwnerUser.DisplayName, membershipVersion: 1));
    }

    private static async Task<IResult> Get(HttpContext http, AppDbContext db, WorkspaceAccessService access, Guid id)
    {
        var (context, failure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.Read, http.RequestAborted);
        if (context is null) return AccessFailure(failure);
        var ownerDisplayName = await db.ApplicationUsers.AsNoTracking().Where(x => x.Id == context.Workspace.OwnerUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(http.RequestAborted);
        var membershipVersion = context.IsSharedPersonalRead ? null : await db.WorkspaceMemberships.AsNoTracking().Where(x => x.WorkspaceId == id && x.UserId == context.User.Id && x.Active).Select(x => (int?)x.Version).SingleOrDefaultAsync(http.RequestAborted);
        return Results.Ok(ToDto(context.Workspace, context.Role, context.User.Id, context.IsSharedPersonalRead, ownerDisplayName, membershipVersion));
    }

    private static async Task<IResult> Update(HttpContext http, AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, Guid id, UpdateWorkspaceRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        var (preflight, preflightFailure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageResources, http.RequestAborted, allowArchivedManagement: true);
        if (preflight is null) return AccessFailure(preflightFailure);
        var (lease, lockFailure) = await gate.AcquireAsync(id, http.RequestAborted);
        if (lease is null) return AccessFailure(lockFailure);
        await using var mutation = lease;
        var (ctx, failure) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageResources, http.RequestAborted, allowArchivedManagement: true);
        if (ctx is null) return AccessFailure(failure);
        var ws = ctx.Workspace;
        if (!ValidName(request.Name)) return Problem(400, "El nombre del espacio no es válido.");
        if (ws.Version != request.Version) return Problem(409, "El espacio cambió. Actualizá la pantalla.");
        ws.Name = request.Name.Trim(); ws.IsArchived = request.IsArchived;
        AddAudit(db, ctx, "workspace.updated", "Workspace", ws.Id);
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); } catch (DbUpdateConcurrencyException) { return Problem(409, "El espacio cambió. Actualizá la pantalla."); }
        var ownerDisplayName = await db.ApplicationUsers.AsNoTracking().Where(x => x.Id == ws.OwnerUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(http.RequestAborted);
        var membershipVersion = await db.WorkspaceMemberships.AsNoTracking().Where(x => x.WorkspaceId == id && x.UserId == ctx.User.Id && x.Active).Select(x => (int?)x.Version).SingleOrDefaultAsync(http.RequestAborted);
        return Results.Ok(ToDto(ws, ctx.Role, ctx.User.Id, ownerDisplayName: ownerDisplayName, membershipVersion: membershipVersion));
    }

    private static async Task<IResult> Members(HttpContext http, AppDbContext db, WorkspaceAccessService access, Guid id)
    {
        var (ctx, failure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (ctx is null) return AccessFailure(failure);
        var rows = await db.WorkspaceMemberships.AsNoTracking().Include(x => x.User).Where(x => x.WorkspaceId == id).OrderBy(x => x.JoinedAt)
            .Select(x => new WorkspaceMemberDto(x.UserId, x.User.DisplayName ?? "", x.Role, x.Active, x.Version)).ToArrayAsync(http.RequestAborted);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ChangeMember(HttpContext http, AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, Guid id, Guid userId, ChangeMembershipRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        var (preflight, preflightFailure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (preflight is null) return AccessFailure(preflightFailure);
        var (lease, lockFailure) = await gate.AcquireAsync(id, http.RequestAborted);
        if (lease is null) return AccessFailure(lockFailure);
        await using var mutation = lease;
        var (ctx, failure) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (ctx is null) return AccessFailure(failure);
        var member = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == userId && x.Active, http.RequestAborted);
        if (member is null) return NotFound();
        if (member.Role == "Owner" || request.Role is not ("Admin" or "Member" or "Reader")) return Problem(403, "No se puede cambiar esa titularidad o rol.");
        if (member.Version != request.Version) return Problem(409, "La membresía cambió. Actualizá la pantalla.");
        AddAudit(db, ctx, "membership.role_changed", "WorkspaceMembership", userId, "{\"role\":\"" + request.Role + "\"}");
        member.Role = request.Role; member.Version++;
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); } catch (DbUpdateConcurrencyException) { return Problem(409, "La membresía cambió. Actualizá la pantalla."); }
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMember(HttpContext http, HttpRequest request, AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, Guid id, Guid userId)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        var body = await request.ReadFromJsonAsync<LeaveWorkspaceRequest>(http.RequestAborted);
        if (body is null) return Results.BadRequest();
        var (preflight, preflightFailure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (preflight is null) return AccessFailure(preflightFailure);
        var (lease, lockFailure) = await gate.AcquireAsync(id, http.RequestAborted);
        if (lease is null) return AccessFailure(lockFailure);
        await using var mutation = lease;
        var (ctx, failure) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (ctx is null) return AccessFailure(failure);
        var member = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == userId && x.Active, http.RequestAborted);
        if (member is null) return NotFound();
        if (member.Role == "Owner") return Problem(403, "La titularidad debe transferirse antes de salir.");
        if (member.Version != body.Version) return Problem(409, "La membresía cambió. Actualizá la pantalla.");
        AddAudit(db, ctx, "membership.revoked", "WorkspaceMembership", userId);
        member.Active = false; member.Version++;
        await DisableOwnerGrantAsync(db, id, userId, http.RequestAborted);
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); } catch (DbUpdateConcurrencyException) { return Problem(409, "La membresía cambió. Actualizá la pantalla."); }
        return Results.NoContent();
    }

    private static async Task<IResult> Leave(HttpContext http, AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, Guid id, LeaveWorkspaceRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        var (preflight, preflightFailure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.Read, http.RequestAborted, allowArchivedManagement: true);
        if (preflight is null) return AccessFailure(preflightFailure);
        var (lease, lockFailure) = await gate.AcquireAsync(id, http.RequestAborted);
        if (lease is null) return AccessFailure(lockFailure);
        await using var mutation = lease;
        var (ctx, failure) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.Read, http.RequestAborted, allowArchivedManagement: true);
        if (ctx is null) return AccessFailure(failure);
        var member = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == ctx.User.Id && x.Active, http.RequestAborted);
        if (member is null) return NotFound();
        if (member.Role == "Owner") return Problem(403, "La titularidad debe transferirse antes de salir.");
        if (member.Version != request.Version) return Problem(409, "La membresía cambió. Actualizá la pantalla.");
        AddAudit(db, ctx, "membership.left", "WorkspaceMembership", ctx.User.Id);
        member.Active = false; member.Version++;
        await DisableOwnerGrantAsync(db, id, ctx.User.Id, http.RequestAborted);
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); } catch (DbUpdateConcurrencyException) { return Problem(409, "La membresía cambió. Actualizá la pantalla."); }
        return Results.NoContent();
    }

    private static async Task<IResult> TransferOwner(HttpContext http, AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, Guid id, OwnerTransferRequest request)
    {
        if (!await ValidCsrf(http, csrf)) return Results.BadRequest();
        var (preflight, preflightFailure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (preflight is null) return AccessFailure(preflightFailure);
        var (lease, lockFailure) = await gate.AcquireAsync(id, http.RequestAborted);
        if (lease is null) return AccessFailure(lockFailure);
        await using var mutation = lease;
        var (ctx, failure) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (ctx is null) return AccessFailure(failure);
        if (ctx.Role != "Owner" || ctx.Workspace.Kind != "Household") return Problem(403, "Solo quien tiene la titularidad de un Hogar puede transferirla.");
        if (ctx.Workspace.Version != request.Version) return Problem(409, "El espacio cambió. Actualizá la pantalla.");
        var target = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == request.UserId && x.Active, http.RequestAborted);
        if (target is null) return NotFound();
        var current = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == id && x.UserId == ctx.User.Id, http.RequestAborted);
        AddAudit(db, ctx, "workspace.owner_transferred", "WorkspaceMembership", request.UserId);
        current.Role = "Admin"; current.Version++;
        target.Role = "Owner"; target.Version++;
        ctx.Workspace.OwnerUserId = target.UserId;
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); } catch (DbUpdateConcurrencyException) { return Problem(409, "El espacio cambió. Actualizá la pantalla."); }
        return Results.NoContent();
    }

    private static async Task<IResult> Audit(HttpContext http, AppDbContext db, WorkspaceAccessService access, Guid id)
    {
        var (ctx, failure) = await access.ResolveAsync(http.User, id.ToString(), WorkspacePermissions.ManageMembers, http.RequestAborted);
        if (ctx is null) return AccessFailure(failure);
        var rows = await db.WorkspaceAuditEvents.AsNoTracking().Where(x => x.WorkspaceId == id).OrderByDescending(x => x.OccurredAt)
            .Select(x => new { x.Id, x.ActorUserId, x.Action, x.ResourceType, x.ResourceId, x.OccurredAt, x.ChangesJson }).Take(200).ToArrayAsync(http.RequestAborted);
        return Results.Ok(rows);
    }

    private static WorkspaceDto ToDto(Workspace ws, string role, Guid userId, bool sharedPersonalRead = false, string? ownerDisplayName = null, int? membershipVersion = null)
    {
        var permissions = WorkspacePermissions.For(role, ws.Kind == "Personal" && role == "Owner" && ws.OwnerUserId == userId);
        if (sharedPersonalRead) permissions = new HashSet<string>([WorkspacePermissions.Read], StringComparer.Ordinal);
        return new WorkspaceDto(ws.Id, ws.Name, ownerDisplayName ?? ws.OwnerUser?.DisplayName, membershipVersion, ws.Kind, role, sharedPersonalRead, ws.IsArchived, ws.Version,
            new(permissions.Contains(WorkspacePermissions.Read), permissions.Contains(WorkspacePermissions.ReadGroups), permissions.Contains(WorkspacePermissions.RecordFinance), permissions.Contains(WorkspacePermissions.ManageResources), permissions.Contains(WorkspacePermissions.CorrectHistory), permissions.Contains(WorkspacePermissions.ManageMembers), permissions.Contains(WorkspacePermissions.ManagePrivacy)));
    }

    private static IResult AccessFailure(int status) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: status == 404 ? "El espacio no existe o no está disponible." : "No tenés permiso para realizar esta acción en el espacio.");
    private static IResult NotFound() => Results.Problem(statusCode: 404, title: "No se pudo completar la solicitud", detail: "El recurso no existe o no está disponible.");
    private static IResult Problem(int status, string detail) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: detail);
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120;
    private static void AddAudit(AppDbContext db, WorkspaceRequestContext context, string action, string resourceType, Guid resourceId, string changesJson = "{}") =>
        db.WorkspaceAuditEvents.Add(new WorkspaceAuditEvent { WorkspaceId = context.Workspace.Id, ActorUserId = context.User.Id, Action = action, ResourceType = resourceType, ResourceId = resourceId.ToString(), OccurredAt = DateTimeOffset.UtcNow, ChangesJson = changesJson });
    private static async Task DisableOwnerGrantAsync(AppDbContext db, Guid householdId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var personalId = await db.Workspaces.Where(x => x.Kind == "Personal" && x.OwnerUserId == ownerUserId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (personalId is null) return;
        var grants = await db.PersonalReadGrants.Where(x => x.PersonalWorkspaceId == personalId && x.HouseholdWorkspaceId == householdId && x.Enabled).ToArrayAsync(cancellationToken);
        foreach (var grant in grants) { grant.Enabled = false; grant.Version++; }
        db.PersonalReadGrantAudience.RemoveRange(db.PersonalReadGrantAudience.Where(x => x.PersonalWorkspaceId == personalId && x.HouseholdWorkspaceId == householdId));
    }
    private static Task<bool> ValidCsrf(HttpContext http, IAntiforgery csrf) => NativeAuthCsrf.ValidateRequestAsync(http, csrf);
}
