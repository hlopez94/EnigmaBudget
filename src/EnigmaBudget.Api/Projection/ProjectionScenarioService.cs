using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Data;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Projection;

public sealed class ProjectionScenarioService(AppDbContext db, WorkspaceMutationGate gate, WorkspaceAccessService access, TimeProvider timeProvider, ProjectionService projectionService)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<ProjectionScenarioDto[]> ListAsync(WorkspaceRequestContext context, CancellationToken ct)
    {
        var actorId = context.User.Id;
        var scenarios = await db.ProjectionScenarios.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.ActorUserId == actorId && !x.IsDeleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(ct);
        var ids = scenarios.Select(x => x.Id).ToArray();
        if (scenarios.Length == 0) return [];
        var assumptions = await db.ProjectionAssumptions.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && ids.Contains(x.ScenarioId)).OrderBy(x => x.Id).ToArrayAsync(ct);
        var visible = await ResolveVisibleAssumptionsAsync(context, assumptions, ct);
        return scenarios.Select(x => ToDto(x, visible.Where(a => a.ScenarioId == x.Id).Select(a => ToInput(a)))).ToArray();
    }

    public async Task<IReadOnlyList<ProjectionAssumptionInput>?> GetAssumptionsAsync(WorkspaceRequestContext context, Guid id, CancellationToken ct)
    {
        var scenario = await db.ProjectionScenarios.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.Id == id && x.ActorUserId == context.User.Id && !x.IsDeleted, ct);
        if (scenario is null) return null;
        var assumptions = await db.ProjectionAssumptions.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.ScenarioId == id).OrderBy(x => x.Id).ToArrayAsync(ct);
        return (await ResolveVisibleAssumptionsAsync(context, assumptions, ct)).Select(ToInput).ToArray();
    }

    public async Task<(ProjectionScenarioDto? Result, int Status, string? Error)> CreateAsync(WorkspaceRequestContext context,
        ClaimsPrincipal principal, ProjectionScenarioCreate request, CancellationToken ct)
    {
        if (request.Id == Guid.Empty || !ValidName(request.Name) || !ValidAssumptions(request.Assumptions)) return (null, 400, "Revisá el nombre y los supuestos del escenario.");
        var hash = Hash(context.User.Id, "ProjectionScenarioCreate", request);
        var (lease, failure) = await AcquireScenarioLeaseAsync(context.Workspace.Id, ct);
        if (lease is null) return (null, failure, "El espacio no está disponible.");
        await using (lease)
        {
            var (fresh, permissionFailure) = await access.ResolveWithinLockedWorkspaceAsync(principal, lease.Workspace, WorkspacePermissions.RecordFinance, ct);
            if (fresh is null) return (null, permissionFailure, "Ya no tenés permiso para guardar escenarios en este espacio.");
            var command = await FindCommand(context.Workspace.Id, request.Id, ct);
            if (command is not null)
            {
                if (command.CommandKind != "ProjectionScenarioCreate" || command.PayloadHash != hash) return (null, 409, "El identificador del escenario ya se usó con otros datos.");
                var replay = await FindOwned(context.Workspace.Id, fresh.User.Id, request.Id, ct);
                return replay is null ? (null, 409, "El escenario ya no está disponible.") : (await ToDtoAsync(replay, fresh, ct), 200, null);
            }
            var collision = await db.ProjectionScenarios.AnyAsync(x => x.WorkspaceId == context.Workspace.Id && x.Id == request.Id, ct);
            if (collision) return (null, 404, "El escenario no está disponible.");
            var scenario = new ProjectionScenario { Id = request.Id, WorkspaceId = context.Workspace.Id, Name = request.Name.Trim(), ActorUserId = fresh.User.Id };
            db.ProjectionScenarios.Add(scenario);
            AddAssumptions(fresh.Workspace.Id, scenario.Id, request.Assumptions);
            db.SavingsCommands.Add(Command(fresh.Workspace.Id, request.Id, "ProjectionScenarioCreate", hash, request.Id));
            lease.MarkChanged();
            try { await db.SaveChangesAsync(ct); await lease.CommitAsync(ct); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); return (null, 409, "El escenario ya existe o hubo un cambio concurrente."); }
            catch (Exception ex) when (IsSerializationConflict(ex))
            {
                db.ChangeTracker.Clear();
                return (null, 409, "El escenario cambió. Volvé a intentarlo.");
            }
            return (await ToDtoAsync(scenario, fresh, ct), 200, null);
        }
    }

    public async Task<(ProjectionScenarioDto? Result, int Status, string? Error)> UpdateAsync(WorkspaceRequestContext context,
        ClaimsPrincipal principal, Guid id, ProjectionScenarioSave request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Version < 1 || !ValidName(request.Name) || !ValidAssumptions(request.Assumptions)) return (null, 400, "Revisá el nombre y los supuestos del escenario.");
        var hash = Hash(context.User.Id, "ProjectionScenarioUpdate", new { id, request });
        var (lease, failure) = await AcquireScenarioLeaseAsync(context.Workspace.Id, ct);
        if (lease is null) return (null, failure, "El espacio no está disponible.");
        await using (lease)
        {
            var (fresh, permissionFailure) = await access.ResolveWithinLockedWorkspaceAsync(principal, lease.Workspace, WorkspacePermissions.RecordFinance, ct);
            if (fresh is null) return (null, permissionFailure, "Ya no tenés permiso para guardar escenarios en este espacio.");
            var command = await FindCommand(context.Workspace.Id, request.RequestId, ct);
            if (command is not null)
            {
                if (command.CommandKind != "ProjectionScenarioUpdate" || command.PayloadHash != hash) return (null, 409, "El identificador de solicitud ya se usó con otros datos.");
                var replay = await FindOwned(context.Workspace.Id, fresh.User.Id, command.ResultResourceId, ct);
                return replay is null ? (null, 409, "El escenario ya no está disponible.") : (await ToDtoAsync(replay, fresh, ct), 200, null);
            }
            var scenario = await FindOwned(context.Workspace.Id, fresh.User.Id, id, ct);
            if (scenario is null) return (null, 404, "El escenario no existe en este espacio.");
            if (scenario.Version != request.Version) return (null, 409, "El escenario cambió. Actualizá la pantalla antes de guardar.");
            scenario.Name = request.Name.Trim(); scenario.Version++;
            var old = await db.ProjectionAssumptions.Where(x => x.WorkspaceId == fresh.Workspace.Id && x.ScenarioId == id).ToArrayAsync(ct);
            var incomingIds = request.Assumptions.Select(x => x.Id).ToHashSet();
            db.ProjectionAssumptions.RemoveRange(old.Where(x => !incomingIds.Contains(x.Id)));
            foreach (var input in request.Assumptions)
            {
                var existing = old.SingleOrDefault(x => x.Id == input.Id);
                var amount = input.Amount is null ? (decimal?)null : decimal.Parse(input.Amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Inv);
                DateOnly? date = input.Date is null ? null : DateOnly.ParseExact(input.Date, "yyyy-MM-dd", Inv);
                if (existing is null)
                    db.ProjectionAssumptions.Add(new ProjectionAssumption { Id = input.Id, WorkspaceId = fresh.Workspace.Id, ScenarioId = id,
                        Kind = input.Kind, Date = date, Currency = input.Currency, Amount = amount, Description = input.Description.Trim(),
                        SourceId = input.SourceId, SourceKind = input.SourceKind, ComponentId = input.ComponentId });
                else
                {
                    existing.Kind = input.Kind; existing.Date = date; existing.Currency = input.Currency; existing.Amount = amount;
                    existing.Description = input.Description.Trim(); existing.SourceId = input.SourceId;
                    existing.SourceKind = input.SourceKind; existing.ComponentId = input.ComponentId; existing.Version++;
                }
            }
            db.SavingsCommands.Add(Command(fresh.Workspace.Id, request.RequestId, "ProjectionScenarioUpdate", hash, id));
            lease.MarkChanged();
            try { await db.SaveChangesAsync(ct); await lease.CommitAsync(ct); }
            catch (DbUpdateException) { return (null, 409, "El escenario cambió. Actualizá la pantalla antes de guardar."); }
            catch (Exception ex) when (IsSerializationConflict(ex))
            {
                db.ChangeTracker.Clear();
                return (null, 409, "El escenario cambió. Actualizá la pantalla antes de guardar.");
            }
            return (await ToDtoAsync(scenario, fresh, ct), 200, null);
        }
    }

    public async Task<(bool Success, int Status, string? Error)> DeleteAsync(WorkspaceRequestContext context,
        ClaimsPrincipal principal, Guid id, ProjectionScenarioDelete request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Version < 1) return (false, 400, "La solicitud de eliminación no es válida.");
        var hash = Hash(context.User.Id, "ProjectionScenarioDelete", new { id, request });
        var (lease, failure) = await gate.AcquireAsync(context.Workspace.Id, ct);
        if (lease is null) return (false, failure, "El espacio no está disponible.");
        await using (lease)
        {
            var (fresh, permissionFailure) = await access.ResolveWithinLockedWorkspaceAsync(principal, lease.Workspace, WorkspacePermissions.RecordFinance, ct);
            if (fresh is null) return (false, permissionFailure, "Ya no tenés permiso para eliminar escenarios en este espacio.");
            var command = await FindCommand(context.Workspace.Id, request.RequestId, ct);
            if (command is not null) return command.CommandKind == "ProjectionScenarioDelete" && command.PayloadHash == hash
                ? (true, 204, null) : (false, 409, "El identificador de solicitud ya se usó con otros datos.");
            var scenario = await FindOwned(context.Workspace.Id, fresh.User.Id, id, ct);
            if (scenario is null) return (false, 404, "El escenario no existe en este espacio.");
            if (scenario.Version != request.Version) return (false, 409, "El escenario cambió. Actualizá la pantalla antes de eliminarlo.");
            scenario.IsDeleted = true; scenario.Version++;
            db.SavingsCommands.Add(Command(fresh.Workspace.Id, request.RequestId, "ProjectionScenarioDelete", hash, id));
            lease.MarkChanged();
            try { await db.SaveChangesAsync(ct); await lease.CommitAsync(ct); }
            catch (DbUpdateException) { return (false, 409, "El escenario cambió. Actualizá la pantalla antes de eliminarlo."); }
            return (true, 204, null);
        }
    }

    private void AddAssumptions(Guid workspaceId, Guid scenarioId, IReadOnlyList<ProjectionAssumptionInput> inputs)
    {
        foreach (var x in inputs)
        {
            decimal? amount = x.Amount is null ? null : decimal.Parse(x.Amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Inv);
            DateOnly? date = x.Date is null ? null : DateOnly.ParseExact(x.Date, "yyyy-MM-dd", Inv);
            db.ProjectionAssumptions.Add(new ProjectionAssumption { Id = x.Id, WorkspaceId = workspaceId, ScenarioId = scenarioId,
                Kind = x.Kind, Date = date, Currency = x.Currency, Amount = amount, Description = x.Description.Trim(), SourceId = x.SourceId,
                SourceKind = x.SourceKind, ComponentId = x.ComponentId });
        }
    }

    private async Task<(WorkspaceMutationLease? Lease, int FailureStatus)> AcquireScenarioLeaseAsync(Guid workspaceId, CancellationToken ct)
    {
        try { return await gate.AcquireAsync(workspaceId, ct, IsolationLevel.RepeatableRead); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return (null, StatusCodes.Status409Conflict);
        }
    }

    private static bool IsSerializationConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.SerializationFailure) return true;
        return false;
    }

    private static bool ValidAssumptions(IReadOnlyList<ProjectionAssumptionInput>? values) => values is not null && values.Count <= 300
        && values.Select(x => x.Id).Distinct().Count() == values.Count && values.All(ValidAssumption);

    private static bool ValidAssumption(ProjectionAssumptionInput x)
    {
        if (x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Description) || x.Description.Trim().Length > 500
            || x.SourceKind is { Length: > 64 } || x.ComponentId is { Length: > 160 }) return false;
        var validDate = DateOnly.TryParseExact(x.Date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out _);
        return x.Kind switch
        {
            "Income" or "Expense" => validDate && x.Currency is "ARS" or "USD" && MoneyRules.Money(x.Amount, false, out var amount) && amount > 0m
                && x.SourceId is null && x.SourceKind is null && x.ComponentId is null,
            "AmountChange" => x.SourceId is not null && x.Currency is "ARS" or "USD" && x.Date is null
                && MoneyRules.Money(x.Amount, true, out var delta) && delta != 0m,
            "DateChange" => x.SourceId is not null && validDate && x.Currency is null && x.Amount is null,
            _ => false,
        };
    }
    private static bool ValidName(string? x) => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 120;
    private static string Hash(Guid actor, string kind, object payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actor, kind, payload }))));
    private SavingsCommand Command(Guid workspaceId, Guid requestId, string kind, string hash, Guid result) =>
        new() { WorkspaceId = workspaceId, RequestId = requestId, CommandKind = kind, PayloadHash = hash, ResultResourceId = result, OccurredAt = timeProvider.GetUtcNow() };
    private Task<SavingsCommand?> FindCommand(Guid ws, Guid requestId, CancellationToken ct) => db.SavingsCommands.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == ws && x.RequestId == requestId, ct);
    private Task<ProjectionScenario?> FindOwned(Guid ws, Guid actor, Guid id, CancellationToken ct) => db.ProjectionScenarios.SingleOrDefaultAsync(x => x.WorkspaceId == ws && x.ActorUserId == actor && x.Id == id && !x.IsDeleted, ct);
    private async Task<ProjectionScenarioDto> ToDtoAsync(ProjectionScenario scenario, WorkspaceRequestContext context, CancellationToken ct)
    {
        var assumptions = await db.ProjectionAssumptions.AsNoTracking().Where(x => x.WorkspaceId == scenario.WorkspaceId && x.ScenarioId == scenario.Id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var visible = await ResolveVisibleAssumptionsAsync(context, assumptions, ct);
        return ToDto(scenario, visible.Select(ToInput));
    }

    private async Task<ProjectionAssumption[]> ResolveVisibleAssumptionsAsync(WorkspaceRequestContext context,
        IReadOnlyList<ProjectionAssumption> assumptions, CancellationToken ct)
    {
        var selectors = assumptions.Where(x => x.SourceId is not null || x.SourceKind is not null || x.ComponentId is not null).ToArray();
        if (selectors.Length == 0) return assumptions.ToArray();
        HashSet<Guid> groupInstallmentIds = [];
        if (!context.Permissions.Contains(WorkspacePermissions.ReadGroups))
        {
            groupInstallmentIds = (await db.Installments.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.GroupId != null)
                .Select(x => x.Id).ToArrayAsync(ct)).ToHashSet();
        }
        var visibleRows = await projectionService.GetScenarioSourceRowsAsync(context, ct);
        var visible = new List<ProjectionAssumption>(assumptions.Count);
        foreach (var assumption in assumptions)
        {
            if (assumption.SourceId is null && assumption.SourceKind is null && assumption.ComponentId is null
                && (assumption.Kind is "Income" or "Expense"))
            {
                visible.Add(assumption);
                continue;
            }
            if (assumption.SourceId is not { } sourceId || assumption.Kind is not ("AmountChange" or "DateChange")) continue;
            if (!context.Permissions.Contains(WorkspacePermissions.ReadGroups)
                && (assumption.SourceKind == "GroupReceivable"
                    || assumption.SourceKind is null && groupInstallmentIds.Contains(sourceId))) continue;
            var matches = visibleRows.Where(x => x.SourceId == sourceId
                && (assumption.SourceKind is null || x.SourceKind == assumption.SourceKind)
                && (assumption.ComponentId is null || x.ComponentId == assumption.ComponentId)
                && (assumption.Kind != "AmountChange" || x.Currency == assumption.Currency)).Take(2).ToArray();
            if (matches.Length == 1) visible.Add(assumption);
        }
        return visible.ToArray();
    }

    private static ProjectionScenarioDto ToDto(ProjectionScenario scenario, IEnumerable<ProjectionAssumptionInput> assumptions) =>
        new(scenario.Id, scenario.Name, scenario.Version, assumptions.ToArray());
    private static ProjectionAssumptionInput ToInput(ProjectionAssumption x) => new(x.Id, x.Kind, x.Date?.ToString("yyyy-MM-dd", Inv), x.Currency,
        x.Amount?.ToString("0.00", Inv), x.Description, x.SourceId, x.SourceKind, x.ComponentId);
}
