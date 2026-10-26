using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Finance;

public sealed class RecurringService(AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, BusinessClock businessClock, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Flows = ["Income", "Expense"];
    private static readonly HashSet<string> Frequencies = ["Weekly", "Monthly", "Annual"];
    private static readonly HashSet<string> ComponentKinds = ["ProviderCharge", "ProviderDiscount", "PayrollWithholding", "HouseholdContribution", "ExpectedRefund", "Other"];

    public async Task<IResult> ListAsync(HttpContext http)
    {
        var workspaceId = FinanceWorkspaceFilter.Get(http)!.Workspace.Id;
        var rules = await db.RecurringRules.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Id).ToListAsync(http.RequestAborted);
        var revisions = await db.RecurringRuleRevisions.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.RuleId).ThenBy(x => x.EffectiveFrom).ThenBy(x => x.Version).ToListAsync(http.RequestAborted);
        var components = await db.HealthcareComponents.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.OccurrenceActionId == null).ToListAsync(http.RequestAborted);
        var today = businessClock.Today;
        var items = rules.Select(rule =>
        {
            var history = revisions.Where(x => x.RuleId == rule.Id).ToArray();
            var effective = history.Where(x => x.EffectiveFrom <= today).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstOrDefault();
            var effectiveDto = effective is null ? null : ToDto(effective);
            var ruleDto = new RecurringRuleDto(rule.Id, rule.WorkspaceId, rule.CurrentVersion, rule.State);
            var planned = components.Where(x => history.Any(revision => revision.Id == x.RuleRevisionId)).Select(ToComponentDto).ToArray();
            return new RecurringRuleViewDto(ruleDto, effective?.State ?? "NotStarted", effectiveDto, history.Select(ToDto).ToArray(), planned);
        }).ToArray();
        return Results.Ok(items);
    }

    public Task<IResult> CreateAsync(HttpContext http, RecurringRuleCreate request) => MutateAsync(http, "Create", request.Id, request.Id, request, 201, async (workspaceId, actorId, ct) =>
    {
        if (request.Id == Guid.Empty || !DateOnly.TryParseExact(request.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !ValidateShape(request.Flow, request.Description, request.Amount, request.AmountMode, request.Currency, request.Frequency, request.Interval, request.EndDate, request.DueDay, request.PreferredCardId, request.HealthcareComponents, out var amount, out var endDate))
            return Fail(400, "Revisá los datos de la regla recurrente.");
        if (endDate is DateOnly end && end < start) return Fail(400, "La fecha de finalización no puede ser anterior al inicio.");
        if (await db.RecurringRules.AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == request.Id, ct)) return Fail(409, "La regla ya existe.");
        var selection = await ValidateSelectionsAsync(workspaceId, request.Flow, request.Currency, request.CategoryId, request.IncomeSourceId, request.MerchantId, request.PreferredAccountId, request.PreferredCardId, request.HealthcareComponents, ct);
        if (selection is not null) return selection;
        var components = ValidateComponents(request.Flow, amount, request.HealthcareComponents);
        if (components is not null) return components;
        var rule = new RecurringRule { Id = request.Id, WorkspaceId = workspaceId, CurrentVersion = 1, State = "Active" };
        var revision = MakeRevision(workspaceId, rule.Id, 1, start, "Active", start, request.Flow, request.Description, request.CategoryId, request.IncomeSourceId, request.MerchantId, amount, request.AmountMode, request.Currency, request.Frequency, request.Interval, endDate, request.DueDay, request.PreferredAccountId, request.PreferredCardId);
        db.RecurringRules.Add(rule); db.RecurringRuleRevisions.Add(revision);
        AddComponents(workspaceId, revision.Id, request.HealthcareComponents);
        return Ok(new RecurringRuleDto(rule.Id, workspaceId, rule.CurrentVersion, rule.State));
    });

    public Task<IResult> UpdateAsync(HttpContext http, Guid ruleId, RecurringRuleUpdate request) => MutateAsync(http, "Update", ruleId, request.RequestId, request, 200, async (workspaceId, actorId, ct) =>
    {
        if (ruleId == Guid.Empty || request.RequestId == Guid.Empty || request.Version < 1
            || !DateOnly.TryParseExact(request.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveFrom)
            || !DateOnly.TryParseExact(request.ScheduleAnchorDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var anchor)
            || !ValidateShape(null, request.Description, request.Amount, request.AmountMode, request.Currency, request.Frequency, request.Interval, request.EndDate, request.DueDay, request.PreferredCardId, request.HealthcareComponents, out var amount, out var endDate))
            return Fail(400, "Revisá los datos de la regla recurrente.");
        var rule = await db.RecurringRules.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == ruleId, ct);
        if (rule is null) return Fail(404, "La regla recurrente no existe.");
        if (request.Version != rule.CurrentVersion) return Fail(409, "La regla cambió. Actualizá la pantalla.");
        if (rule.State == "Ended") return Fail(409, "La regla finalizada no se puede modificar.");
        var latest = await db.RecurringRuleRevisions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstAsync(ct);
        if (effectiveFrom < latest.EffectiveFrom) return Fail(400, "La nueva revisión no puede comenzar antes de la última revisión.");
        var selection = await ValidateSelectionsAsync(workspaceId, latest.Flow, request.Currency, request.CategoryId, request.IncomeSourceId, request.MerchantId, request.PreferredAccountId, request.PreferredCardId, request.HealthcareComponents, ct);
        if (selection is not null) return selection;
        var components = ValidateComponents(latest.Flow, amount, request.HealthcareComponents);
        if (components is not null) return components;
        if (endDate is DateOnly end && end < effectiveFrom) return Fail(400, "La fecha de finalización no puede ser anterior a la revisión.");
        var revision = MakeRevision(workspaceId, rule.Id, rule.CurrentVersion + 1, effectiveFrom, latest.State, anchor, latest.Flow, request.Description, request.CategoryId, request.IncomeSourceId, request.MerchantId, amount, request.AmountMode, request.Currency, request.Frequency, request.Interval, endDate, request.DueDay, request.PreferredAccountId, request.PreferredCardId);
        db.RecurringRuleRevisions.Add(revision); AddComponents(workspaceId, revision.Id, request.HealthcareComponents); rule.CurrentVersion++;
        return Ok(new RecurringRuleDto(rule.Id, workspaceId, rule.CurrentVersion, rule.State));
    });

    public Task<IResult> ChangeStateAsync(HttpContext http, Guid ruleId, RecurringRuleStateChange request) => MutateAsync(http, "StateChange", ruleId, request.RequestId, request, 200, async (workspaceId, actorId, ct) =>
    {
        if (ruleId == Guid.Empty || request.RequestId == Guid.Empty || request.Version < 1
            || !DateOnly.TryParseExact(request.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveFrom)
            || request.State is not ("Active" or "Paused" or "Ended")) return Fail(400, "Revisá el cambio de estado de la regla.");
        var rule = await db.RecurringRules.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == ruleId, ct);
        if (rule is null) return Fail(404, "La regla recurrente no existe.");
        if (request.Version != rule.CurrentVersion) return Fail(409, "La regla cambió. Actualizá la pantalla.");
        if (rule.State == "Ended" || rule.State == request.State) return Fail(409, "No se puede aplicar ese cambio de estado.");
        var latest = await db.RecurringRuleRevisions.Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstAsync(ct);
        if (effectiveFrom < latest.EffectiveFrom) return Fail(400, "El cambio no puede comenzar antes de la última revisión.");
        var revision = new RecurringRuleRevision { Id = Guid.NewGuid(), WorkspaceId = workspaceId, RuleId = ruleId, Version = rule.CurrentVersion + 1,
            EffectiveFrom = effectiveFrom, State = request.State, ScheduleAnchorDate = latest.ScheduleAnchorDate, Flow = latest.Flow, Description = latest.Description,
            CategoryId = latest.CategoryId, IncomeSourceId = latest.IncomeSourceId, MerchantId = latest.MerchantId, Amount = latest.Amount,
            AmountMode = latest.AmountMode, Currency = latest.Currency, Frequency = latest.Frequency, Interval = latest.Interval, EndDate = latest.EndDate,
            DueDay = latest.DueDay, PreferredAccountId = latest.PreferredAccountId, PreferredCardId = latest.PreferredCardId };
        var sourceComponents = await db.HealthcareComponents.Where(x => x.WorkspaceId == workspaceId && x.RuleRevisionId == latest.Id && x.OccurrenceActionId == null).ToListAsync(ct);
        db.RecurringRuleRevisions.Add(revision);
        db.HealthcareComponents.AddRange(sourceComponents.Select(x => new HealthcareComponent { Id = DeterministicComponentId(revision.Id, x.Id), WorkspaceId = workspaceId, RuleRevisionId = revision.Id, Kind = x.Kind, Amount = x.Amount, AppliesToProviderBalance = x.AppliesToProviderBalance, ParticipantId = x.ParticipantId }));
        rule.CurrentVersion++; rule.State = request.State;
        return Ok(new RecurringRuleDto(rule.Id, workspaceId, rule.CurrentVersion, rule.State));
    });

    private async Task<IResult> MutateAsync<T>(HttpContext http, string kind, Guid ruleId, Guid requestId, T payload, int successStatus, Func<Guid, Guid, CancellationToken, Task<MutationResult>> apply)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida.");
        var initial = FinanceWorkspaceFilter.Get(http)!;
        var (lease, lockStatus) = await gate.AcquireAsync(initial.Workspace.Id, http.RequestAborted);
        if (lease is null) return Problem(lockStatus, "El espacio no existe.");
        await using var mutation = lease;
        var (context, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageResources, http.RequestAborted);
        if (context is null) return Problem(authStatus, "No tenés permiso para administrar reglas recurrentes.");
        if (requestId == Guid.Empty || ruleId == Guid.Empty) return Problem(400, "El identificador de solicitud no es válido.");
        var raw = JsonSerializer.Serialize(payload, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + ruleId.ToString("D") + "\n" + raw)));
        var prior = await db.RecurringRuleCommands.SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.RequestId == requestId, http.RequestAborted);
        if (prior is not null)
        {
            if (prior.CommandKind != kind || prior.RuleId != ruleId || prior.PayloadHash != hash) return Problem(409, "El identificador de solicitud ya se usó con otros datos.");
            return Results.Content(prior.ResultJson, "application/json", Encoding.UTF8, StatusCodes.Status200OK);
        }
        var result = await apply(context.Workspace.Id, context.User.Id, http.RequestAborted);
        if (result.Error is not null) return Problem(result.Status, result.Error);
        var resultJson = JsonSerializer.Serialize(result.Value, JsonOptions);
        mutation.MarkChanged();
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException) { return Problem(409, "La regla cambió. Actualizá la pantalla."); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg && pg.SqlState is "23505" or "23503") { return Problem(409, "La regla cambió o la solicitud ya se procesó."); }
        db.RecurringRuleCommands.Add(new RecurringRuleCommand { Id = Guid.NewGuid(), WorkspaceId = context.Workspace.Id, RequestId = requestId, CommandKind = kind, RuleId = ruleId, PayloadHash = hash, ResultJson = resultJson, ActorUserId = context.User.Id, CreatedAt = clock.GetUtcNow() });
        db.WorkspaceAuditEvents.Add(new WorkspaceAuditEvent { WorkspaceId = context.Workspace.Id, ActorUserId = context.User.Id, Action = $"finance.recurring.{kind.ToLowerInvariant()}", ResourceType = "RecurringRule", ResourceId = ruleId.ToString("D"), OccurredAt = clock.GetUtcNow(), ChangesJson = JsonSerializer.Serialize(new { requestId, kind }, JsonOptions) });
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "La regla cambió. Actualizá la pantalla."); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg && pg.SqlState is "23505" or "23503") { return Problem(409, "La regla cambió o la solicitud ya se procesó."); }
        return Results.Json(result.Value, statusCode: successStatus);
    }

    private async Task<MutationResult?> ValidateSelectionsAsync(Guid workspaceId, string flow, string currency, Guid? categoryId, Guid? sourceId, Guid? merchantId, Guid? accountId, Guid? cardId, IReadOnlyList<HealthcareComponentInput>? components, CancellationToken ct)
    {
        if (flow is not ("Income" or "Expense")) return Fail(400, "El flujo de la regla no es válido.");
        if (categoryId is null) return Fail(400, "Seleccioná una categoría activa.");
        var category = await db.FinanceCategories.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == categoryId, ct);
        if (category is null) return Fail(404, "La categoría no existe en este espacio.");
        if (category.IsArchived || category.Kind != flow) return Fail(400, "La categoría debe estar activa y coincidir con el flujo.");
        if (flow == "Income" && sourceId is null || flow == "Expense" && sourceId is not null) return Fail(400, "La fuente de ingreso solo corresponde a ingresos y es obligatoria.");
        if (sourceId is Guid source)
        {
            var row = await db.FinanceIncomeSources.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == source, ct);
            if (row is null) return Fail(404, "La fuente de ingreso no existe en este espacio.");
            if (row.IsArchived) return Fail(409, "La fuente de ingreso está archivada.");
        }
        if (merchantId is Guid merchant)
        {
            var row = await db.FinanceMerchants.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == merchant, ct);
            if (row is null) return Fail(404, "El comercio no existe en este espacio.");
            if (row.IsArchived) return Fail(409, "El comercio está archivado.");
        }
        if (currency is not ("ARS" or "USD")) return Fail(400, "La moneda no es válida.");
        if (accountId is Guid account)
        {
            var row = await db.Accounts.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == account, ct);
            if (row is null) return Fail(404, "La cuenta no existe en este espacio.");
            if (row.IsArchived || row.Currency != currency) return Fail(400, "La cuenta preferida debe estar activa y usar la misma moneda.");
        }
        if (cardId is Guid card)
        {
            if (flow == "Income") return Fail(400, "Un ingreso no puede usar una tarjeta preferida.");
            var row = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == card, ct);
            if (row is null) return Fail(404, "La tarjeta no existe en este espacio.");
            if (row.IsArchived) return Fail(400, "La tarjeta preferida debe estar activa.");
        }
        foreach (var participantId in components?.Where(x => x.ParticipantId.HasValue).Select(x => x.ParticipantId!.Value).Distinct() ?? [])
            if (!await db.Participants.AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == participantId, ct)) return Fail(404, "La persona indicada no pertenece a este espacio.");
        return null;
    }

    private static bool ValidateShape(string? flow, string description, string amountText, string amountMode, string currency, string frequency, int interval, string? endDateText, int? dueDay, Guid? cardId, IReadOnlyList<HealthcareComponentInput>? components, out decimal amount, out DateOnly? endDate)
    {
        amount = 0; endDate = null;
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 240 || description.Any(char.IsControl)
            || !MoneyRules.Money(amountText, false, out amount) || amount <= 0 || amountMode is not ("Fixed" or "Estimated")
            || currency is not ("ARS" or "USD") || !Frequencies.Contains(frequency) || interval <= 0
            || dueDay is < 1 or > 31 || components is null || components.Count > 100
            || components.Any(x => x.Id == Guid.Empty || !ComponentKinds.Contains(x.Kind) || x.AppliesToProviderBalance && x.Kind != "PayrollWithholding" || !MoneyRules.Money(x.Amount, false, out var part) || part <= 0)
            || components.Select(x => x.Id).Distinct().Count() != components.Count) return false;
        if (endDateText is not null)
        {
            if (!DateOnly.TryParseExact(endDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) return false;
            endDate = end;
        }
        if (flow is not null && !Flows.Contains(flow)) return false;
        if (cardId is not null && flow == "Income") return false;
        return true;
    }

    private static MutationResult? ValidateComponents(string flow, decimal amount, IReadOnlyList<HealthcareComponentInput> components)
    {
        var charges = components.Where(x => x.Kind == "ProviderCharge").Sum(x => Parse(x.Amount));
        var discounts = components.Where(x => x.Kind == "ProviderDiscount").Sum(x => Parse(x.Amount));
        if (charges == 0 && discounts == 0) return null;
        var gross = charges - discounts;
        return flow == "Expense" && charges > 0 && gross > 0 && amount == gross ? null : Fail(400, "El importe debe coincidir con los cargos del proveedor menos sus descuentos.");
    }

    private static RecurringRuleRevision MakeRevision(Guid workspaceId, Guid ruleId, int version, DateOnly effective, string state, DateOnly anchor, string flow, string description, Guid? category, Guid? source, Guid? merchant, decimal amount, string amountMode, string currency, string frequency, int interval, DateOnly? endDate, int? dueDay, Guid? account, Guid? card) =>
        new() { Id = Guid.NewGuid(), WorkspaceId = workspaceId, RuleId = ruleId, Version = version, EffectiveFrom = effective, State = state, ScheduleAnchorDate = anchor, Flow = flow, Description = description.Trim(), CategoryId = category, IncomeSourceId = source, MerchantId = merchant, Amount = amount, AmountMode = amountMode, Currency = currency, Frequency = frequency, Interval = interval, EndDate = endDate, DueDay = dueDay, PreferredAccountId = account, PreferredCardId = card };

    private void AddComponents(Guid workspaceId, Guid revisionId, IReadOnlyList<HealthcareComponentInput> components)
    {
        db.HealthcareComponents.AddRange(components.Select(x => new HealthcareComponent { Id = DeterministicComponentId(revisionId, x.Id), WorkspaceId = workspaceId, RuleRevisionId = revisionId, Kind = x.Kind, Amount = Parse(x.Amount), AppliesToProviderBalance = x.AppliesToProviderBalance, ParticipantId = x.ParticipantId }));
    }

    private static Guid DeterministicComponentId(Guid revisionId, Guid lineId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{revisionId:D}/{lineId:D}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static RecurringRuleRevisionDto ToDto(RecurringRuleRevision x) => new(x.Id, x.WorkspaceId, x.RuleId, x.Version, x.EffectiveFrom, x.State, x.ScheduleAnchorDate, x.Flow, x.Description, x.CategoryId, x.IncomeSourceId, x.MerchantId, F(x.Amount), x.AmountMode, x.Currency, x.Frequency, x.Interval, x.EndDate, x.DueDay, x.PreferredAccountId, x.PreferredCardId);
    private static RecurringHealthcareComponentDto ToComponentDto(HealthcareComponent x) => new(x.Id, x.RuleRevisionId, x.Kind, F(x.Amount), x.AppliesToProviderBalance, x.ParticipantId, x.OccurrenceActionId);
    private static decimal Parse(string text) { MoneyRules.Money(text, false, out var value); return value; }
    private static string F(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static MutationResult Ok(object? value) => new(200, value, null);
    private static MutationResult Fail(int status, string error) => new(status, null, error);
    private static IResult Problem(int status, string detail) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: detail);
    private sealed record MutationResult(int Status, object? Value, string? Error);
}
