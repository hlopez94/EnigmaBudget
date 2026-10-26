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

    public Task<IResult> RecordOccurrenceAsync(HttpContext http, Guid ruleId, string scheduledDateText, RecurringOccurrenceRecord request) =>
        OccurrenceActionAsync(http, ruleId, scheduledDateText, "Recorded", request.RequestId, request,
            (workspaceId, actorId, revision, scheduledDate, ct) => RecordOccurrenceCoreAsync(http, workspaceId, actorId, ruleId, revision, scheduledDate, request, ct));

    public Task<IResult> SkipOccurrenceAsync(HttpContext http, Guid ruleId, string scheduledDateText, RecurringOccurrenceSkip request) =>
        OccurrenceActionAsync(http, ruleId, scheduledDateText, "Skipped", request.RequestId, request,
            (workspaceId, actorId, revision, scheduledDate, ct) => SkipOccurrenceCoreAsync(workspaceId, actorId, ruleId, revision, scheduledDate, request, ct));

    private async Task<IResult> OccurrenceActionAsync<T>(HttpContext http, Guid ruleId, string scheduledDateText, string outcome,
        Guid requestId, T request, Func<Guid, Guid, RecurringRuleRevision, DateOnly, CancellationToken, Task<OccurrenceResult>> apply)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida.");
        if (ruleId == Guid.Empty || requestId == Guid.Empty
            || !DateOnly.TryParseExact(scheduledDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var scheduledDate))
            return Problem(400, "Revisá la fecha y los identificadores de la ocurrencia.");

        var initial = FinanceWorkspaceFilter.Get(http)!;
        var (lease, lockStatus) = await gate.AcquireAsync(initial.Workspace.Id, http.RequestAborted);
        if (lease is null) return Problem(lockStatus, "El espacio no existe.");
        await using var mutation = lease;
        var (context, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.RecordFinance, http.RequestAborted);
        if (context is null) return Problem(authStatus, "No tenés permiso para registrar movimientos.");

        var workspaceId = context.Workspace.Id;
        var serialized = JsonSerializer.Serialize(request, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{outcome}\n{ruleId:D}\n{scheduledDate:yyyy-MM-dd}\n{serialized}")));
        var priorRequest = await db.RecurringOccurrenceActions.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.RequestId == requestId, http.RequestAborted);
        if (priorRequest is not null)
            return priorRequest.RuleId == ruleId && priorRequest.ScheduledDate == scheduledDate && priorRequest.Outcome == outcome && priorRequest.PayloadHash == hash
                ? Results.Ok(ActionResult(priorRequest)) : Problem(409, "El identificador de solicitud ya se usó con otros datos.");

        var lockedRule = await db.RecurringRules.FromSqlInterpolated($"SELECT * FROM \"RecurringRules\" WHERE \"WorkspaceId\" = {workspaceId} AND \"Id\" = {ruleId} FOR UPDATE").SingleOrDefaultAsync(http.RequestAborted);
        if (lockedRule is null) return Problem(404, "La regla recurrente no existe.");
        var ruleVersion = request switch
        {
            RecurringOccurrenceRecord record => record.RuleVersion,
            RecurringOccurrenceSkip skip => skip.RuleVersion,
            _ => 0
        };
        if (ruleVersion < 1 || ruleVersion != lockedRule.CurrentVersion) return Problem(409, "La regla cambió. Actualizá la pantalla.");
        var existing = await db.RecurringOccurrenceActions.FromSqlInterpolated($"SELECT * FROM \"RecurringOccurrenceActions\" WHERE \"WorkspaceId\" = {workspaceId} AND \"RuleId\" = {ruleId} AND \"ScheduledDate\" = {scheduledDate} FOR UPDATE").SingleOrDefaultAsync(http.RequestAborted);
        if (existing is not null) return Problem(409, "Esta fecha ya tiene una acción registrada.");

        var calendar = new RecurringCalendar(db);
        var revision = await calendar.ResolveRevisionAsync(workspaceId, ruleId, scheduledDate, http.RequestAborted);
        if (revision is null) return Problem(409, "La fecha no corresponde a una ocurrencia activa de esta regla.");
        var result = await apply(workspaceId, context.User.Id, revision, scheduledDate, http.RequestAborted);
        if (result.Error is not null) return Problem(result.Status, result.Error);

        var action = result.Action!;
        db.RecurringOccurrenceActions.Add(action);
        db.WorkspaceAuditEvents.Add(new WorkspaceAuditEvent { WorkspaceId = workspaceId, ActorUserId = context.User.Id,
            Action = $"finance.recurring.occurrence.{outcome.ToLowerInvariant()}", ResourceType = "RecurringOccurrence",
            ResourceId = RecurringCalendar.StableOccurrenceId(workspaceId, ruleId, scheduledDate).ToString("D"),
            OccurredAt = clock.GetUtcNow(), ChangesJson = JsonSerializer.Serialize(new { requestId, ruleId, scheduledDate, outcome }, JsonOptions) });
        mutation.MarkChanged();
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
            await mutation.CommitAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException) { return Problem(409, "La fecha ya cambió. Actualizá la pantalla."); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg && pg.SqlState is "23505" or "23503")
        { return Problem(409, "La fecha ya tiene una acción registrada o algún recurso cambió."); }
        return Results.Ok(ActionResult(action));
    }

    private Task<OccurrenceResult> SkipOccurrenceCoreAsync(Guid workspaceId, Guid actorId, Guid ruleId,
        RecurringRuleRevision revision, DateOnly scheduledDate, RecurringOccurrenceSkip request, CancellationToken ct)
    {
        if (request.RuleVersion < 1 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 240)
            return Task.FromResult(OccurrenceFail(400, "Indicá un motivo de hasta 240 caracteres."));
        var hash = OccurrenceHash("Skipped", ruleId, scheduledDate, request);
        var action = new RecurringOccurrenceAction { Id = ChildId(workspaceId, request.RequestId, "action", "skip"),
            WorkspaceId = workspaceId, RuleId = ruleId, RuleRevisionId = revision.Id, ScheduledDate = scheduledDate,
            Outcome = "Skipped", Reason = request.Reason.Trim(), RequestId = request.RequestId, PayloadHash = hash };
        return Task.FromResult(new OccurrenceResult(action, 200, null));
    }

    private async Task<OccurrenceResult> RecordOccurrenceCoreAsync(HttpContext http, Guid workspaceId, Guid actorId, Guid ruleId,
        RecurringRuleRevision revision, DateOnly scheduledDate, RecurringOccurrenceRecord request, CancellationToken ct)
    {
        if (request.RuleVersion < 1 || !DateOnly.TryParseExact(request.ActualDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var actualDate)
            || !MoneyRules.Money(request.ActualAmount, false, out var gross) || gross <= 0m
            || !MoneyRules.Money(request.ActualCashAmount, false, out var cash) || !MoneyRules.Money(request.CardChargeAmount, false, out var cardAmount)
            || cash > gross || cardAmount > gross || request.ActualHealthcareComponents is null || request.ActualHealthcareComponents.Count > 100
            || request.ActualHealthcareComponents.Select(x => x.Id).Any(x => x == Guid.Empty)
            || request.ActualHealthcareComponents.Select(x => x.Id).Distinct().Count() != request.ActualHealthcareComponents.Count)
            return OccurrenceFail(400, "Revisá el importe, la fecha y el desglose real.");
        if (actualDate > businessClock.Today) return OccurrenceFail(400, "La fecha real no puede ser futura.");
        foreach (var component in request.ActualHealthcareComponents)
            if (!ComponentKinds.Contains(component.Kind) || component.AppliesToProviderBalance && component.Kind != "PayrollWithholding"
                || !MoneyRules.Money(component.Amount, false, out var part) || part <= 0m)
                return OccurrenceFail(400, "Revisá los componentes reales de la cobertura.");

        var plannedComponents = await db.HealthcareComponents.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.RuleRevisionId == revision.Id && x.OccurrenceActionId == null).ToArrayAsync(ct);
        var isHealthcare = plannedComponents.Any(x => x.Kind is "ProviderCharge" or "ProviderDiscount");
        var actualCharges = request.ActualHealthcareComponents.Where(x => x.Kind == "ProviderCharge").Sum(x => Parse(x.Amount));
        var actualDiscounts = request.ActualHealthcareComponents.Where(x => x.Kind == "ProviderDiscount").Sum(x => Parse(x.Amount));
        if (isHealthcare || actualCharges > 0m || actualDiscounts > 0m)
            if (actualCharges <= 0m || actualCharges - actualDiscounts != gross)
                return OccurrenceFail(400, "El importe debe coincidir con los cargos reales del proveedor menos sus descuentos.");

        if (revision.Flow == "Income")
        {
            if (cardAmount != 0m || request.CardId is not null || request.CardFirstPeriod is not null
                || request.ActualHealthcareComponents.Count != 0 || request.CatalogOverride is { MerchantId: not null }
                || request.CatalogOverride is { CategoryId: null } || request.CatalogOverride is { IncomeSourceId: null })
                return OccurrenceFail(400, "Un ingreso no admite tarjeta ni componentes de gasto.");
        }
        else if (request.CatalogOverride is { IncomeSourceId: not null }) return OccurrenceFail(400, "Un gasto no admite fuente de ingreso.");
        if (request.CatalogOverride is { CategoryId: null }) return OccurrenceFail(400, "La selección de catálogo explícita debe incluir una categoría activa.");

        BankAccount? cashAccount = null;
        if (cash > 0m)
        {
            if (request.CashAccountId is not Guid cashAccountId || request.ClearPreferredAccount)
                return OccurrenceFail(400, "Elegí una cuenta activa para el importe pagado en efectivo.");
            cashAccount = await db.Accounts.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == cashAccountId, ct);
            if (cashAccount is null) return OccurrenceFail(404, "La cuenta indicada no existe en este espacio.");
            if (cashAccount.IsArchived || cashAccount.Currency != revision.Currency || actualDate < cashAccount.OpeningDate)
                return OccurrenceFail(409, "La cuenta no admite este pago real.");
        }
        else if (request.CashAccountId is not null) return OccurrenceFail(400, "No elijas una cuenta de pago si no se cobró ni pagó efectivo.");

        Guid? operationAccountId = cashAccount?.Id;
        if (cash == 0m)
        {
            if (request.ClearPreferredAccount)
            {
                if (request.PreferredAccountId is not null) return OccurrenceFail(400, "Para quitar la cuenta prevista, PreferredAccountId debe quedar vacío.");
                operationAccountId = null;
            }
            else
            {
                var preferredId = request.PreferredAccountId ?? revision.PreferredAccountId;
                if (preferredId is Guid id)
                {
                    var preferred = await db.Accounts.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, ct);
                    if (preferred is null) return OccurrenceFail(404, "La cuenta prevista no existe en este espacio.");
                    if (preferred.IsArchived || preferred.Currency != revision.Currency)
                        return OccurrenceFail(409, "La cuenta prevista está archivada o usa otra moneda. Elegí una cuenta activa o quitá explícitamente la cuenta prevista.");
                    operationAccountId = preferred.Id;
                }
            }
        }

        ResolvedOperationCatalog selected;
        var overrideCategory = request.CatalogOverride?.CategoryId ?? (request.CatalogOverride is null ? revision.CategoryId : null);
        var overrideMerchant = request.CatalogOverride?.MerchantId ?? (request.CatalogOverride is null ? revision.MerchantId : null);
        var overrideSource = request.CatalogOverride?.IncomeSourceId ?? (request.CatalogOverride is null ? revision.IncomeSourceId : null);
        selected = await CatalogSelectionResolver.ResolveOperationAsync(db, workspaceId, revision.Flow, null, overrideCategory, overrideMerchant, overrideSource, ct);
        if (selected.FailureStatus != 0) return OccurrenceFail(selected.FailureStatus, selected.FailureDetail!);

        PaymentCard? card = null;
        string? cardPeriod = request.CardFirstPeriod;
        if (cardAmount > 0m)
        {
            if (revision.Flow != "Expense" || request.CardId is not Guid cardId || !MoneyRules.IsPeriod(cardPeriod ?? ""))
                return OccurrenceFail(400, "Elegí una tarjeta activa y el primer período para el importe cargado.");
            card = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == cardId, ct);
            if (card is null) return OccurrenceFail(404, "La tarjeta indicada no existe en este espacio.");
            if (card.IsArchived || actualDate < card.AsOfDate || cardPeriod!.CompareTo(card.AsOfDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)) < 0)
                return OccurrenceFail(409, "La tarjeta no admite compras en esa fecha o período.");
            if (await db.CardStatements.AnyAsync(x => x.WorkspaceId == workspaceId && x.CardId == card.Id && x.Period == cardPeriod && x.Currency == revision.Currency && x.State == "Confirmed", ct))
                return OccurrenceFail(409, "El resumen de ese período está confirmado.");
        }
        else if (request.CardId is not null || request.CardFirstPeriod is not null)
            return OccurrenceFail(400, "La tarjeta y el período solo corresponden a un cargo positivo.");

        foreach (var participantId in request.ActualHealthcareComponents.Where(x => x.ParticipantId.HasValue).Select(x => x.ParticipantId!.Value).Distinct())
            if (!await db.Participants.AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == participantId, ct))
                return OccurrenceFail(404, "La persona indicada no pertenece a este espacio.");

        var appliedPayroll = request.ActualHealthcareComponents.Where(x => x.Kind == "PayrollWithholding" && x.AppliesToProviderBalance).Sum(x => Parse(x.Amount));
        if (revision.Flow == "Expense" && cash + cardAmount + appliedPayroll > gross)
            return OccurrenceFail(400, "Las partes confirmadas superan el importe bruto del gasto.");
        if (revision.Flow == "Income" && cash > gross) return OccurrenceFail(400, "El cobro no puede superar el ingreso total.");

        var commandHash = OccurrenceHash("Recorded", ruleId, scheduledDate, request);
        var actionId = ChildId(workspaceId, request.RequestId, "action", "recorded");
        var operationId = ChildId(workspaceId, request.RequestId, "operation", "root");
        var operation = new FinanceOperation { Id = operationId, WorkspaceId = workspaceId, Kind = revision.Flow,
            AccountId = operationAccountId, Date = actualDate, Description = revision.Description, Category = selected.CategoryName,
            CategoryId = selected.CategoryId, MerchantId = selected.MerchantId, MerchantNameSnapshot = selected.MerchantName,
            IncomeSourceId = selected.IncomeSourceId, IncomeSourceNameSnapshot = selected.IncomeSourceName,
            CatalogSelectionHash = selected.SelectionHash, Currency = revision.Currency, Amount = gross, InitiallyPaid = cash == gross };
        var action = new RecurringOccurrenceAction { Id = actionId, WorkspaceId = workspaceId, RuleId = ruleId,
            RuleRevisionId = revision.Id, ScheduledDate = scheduledDate, Outcome = "Recorded", FinanceOperationId = operationId,
            RequestId = request.RequestId, PayloadHash = commandHash };
        FinanceRecordWriter.AddOperation(db, operation);
        db.RecurringOccurrenceActions.Add(action);
        var actualComponents = request.ActualHealthcareComponents.Select(x => new HealthcareComponent
        {
            Id = ChildId(workspaceId, request.RequestId, "healthcare-component", x.Id.ToString("D")), WorkspaceId = workspaceId,
            RuleRevisionId = revision.Id, OccurrenceActionId = actionId, Kind = x.Kind, Amount = Parse(x.Amount),
            AppliesToProviderBalance = x.AppliesToProviderBalance, ParticipantId = x.ParticipantId
        }).ToArray();
        db.HealthcareComponents.AddRange(actualComponents);

        var settlementRows = new List<NonCashSettlement>();
        if (cash > 0m) FinanceRecordWriter.AddPayment(db, operation, cashAccount!, actualDate, cash,
            ChildId(workspaceId, request.RequestId, "payment", "cash"), ledgerEntryId: ChildId(workspaceId, request.RequestId, "ledger-entry", "cash"));
        if (cardAmount > 0m)
        {
            var selectedPurchase = await CatalogSelectionResolver.ResolvePurchaseAsync(db, workspaceId, null, selected.CategoryId, selected.MerchantId, ct);
            if (selectedPurchase.FailureStatus != 0) return OccurrenceFail(selectedPurchase.FailureStatus, selectedPurchase.FailureDetail!);
            var purchaseId = ChildId(workspaceId, request.RequestId, "purchase", "card-charge");
            var purchase = new CardPurchase { Id = purchaseId, WorkspaceId = workspaceId, CardId = card!.Id, Date = actualDate,
                Description = revision.Description, Category = selectedPurchase.CategoryName, CategoryId = selectedPurchase.CategoryId,
                ExpenseOperationId = operationId, MerchantId = selectedPurchase.MerchantId, MerchantNameSnapshot = selectedPurchase.MerchantName,
                CatalogSelectionHash = selectedPurchase.SelectionHash, Currency = revision.Currency, TotalAmount = cardAmount,
                InstallmentCount = 1, FirstPeriod = cardPeriod! };
            FinanceRecordWriter.AddPurchase(db, purchase, number => ChildId(workspaceId, request.RequestId, "installment", number.ToString(CultureInfo.InvariantCulture)));
            action.CardPurchaseId = purchaseId;
            card!.Version++;
            settlementRows.Add(new NonCashSettlement { Id = ChildId(workspaceId, request.RequestId, "settlement", "card-charge"),
                WorkspaceId = workspaceId, OperationId = operationId, Currency = revision.Currency, Kind = "CardCharge",
                Amount = cardAmount, CardPurchaseId = purchaseId, RequestId = ChildId(workspaceId, request.RequestId, "settlement-request", "card-charge"),
                PayloadHash = commandHash, ActorUserId = actorId, CreatedAt = clock.GetUtcNow() });
        }
        foreach (var input in request.ActualHealthcareComponents.Where(x => x.Kind == "PayrollWithholding" && x.AppliesToProviderBalance))
        {
            var component = actualComponents.Single(x => x.Id == ChildId(workspaceId, request.RequestId, "healthcare-component", input.Id.ToString("D")));
            settlementRows.Add(new NonCashSettlement { Id = ChildId(workspaceId, request.RequestId, "settlement", input.Id.ToString("D")),
                WorkspaceId = workspaceId, OperationId = operationId, Currency = revision.Currency, Kind = "PayrollWithholding",
                Amount = component.Amount, HealthcareComponentId = component.Id,
                RequestId = ChildId(workspaceId, request.RequestId, "settlement-request", input.Id.ToString("D")), PayloadHash = commandHash,
                ActorUserId = actorId, CreatedAt = clock.GetUtcNow() });
        }
        db.NonCashSettlements.AddRange(settlementRows);
        foreach (var settlement in settlementRows)
        {
            if (!await OperationSettlementSourceValidator.ValidateAsync(db, operation, settlement, ct))
                return OccurrenceFail(409, "El origen de una parte de la liquidación no es válido.");
        }
        return new OccurrenceResult(action, 200, null);
    }

    private static object ActionResult(RecurringOccurrenceAction action) => new
    {
        occurrenceId = action.Id, ruleId = action.RuleId, scheduledDate = action.ScheduledDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        outcome = action.Outcome, financeOperationId = action.FinanceOperationId, cardPurchaseId = action.CardPurchaseId, reason = action.Reason
    };

    private static string OccurrenceHash<T>(string outcome, Guid ruleId, DateOnly scheduledDate, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{outcome}\n{ruleId:D}\n{scheduledDate:yyyy-MM-dd}\n{JsonSerializer.Serialize(request, JsonOptions)}")));

    private static Guid ChildId(Guid workspaceId, Guid requestId, string role, string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{workspaceId:D}/{requestId:D}/{role}/{source}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static OccurrenceResult OccurrenceFail(int status, string detail) => new(null, status, detail);
    private sealed record OccurrenceResult(RecurringOccurrenceAction? Action, int Status, string? Error);

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
