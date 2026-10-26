using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.ExchangeRates;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Savings;

public sealed record SavingsEstimateResult(decimal RemainingAmount, bool Reached, int? RequiredContributionCount,
    DateOnly? EstimatedCompletionDate, decimal? RequiredMonthlyContribution, string EstimateStatus, string Explanation);

/// <summary>
/// Canonical reservation-row lock order. Callers first hold a workspace mutation lease, then use this helper,
/// and only then lock any goal row. This lets reservation commands and goal close share one lock order.
/// </summary>
public static class SavingsMutationLocks
{
    public static async Task LockAccountsThenAllocationsAsync(AppDbContext db, Guid workspaceId,
        IEnumerable<Guid> accountIds, IEnumerable<Guid> allocationIds, CancellationToken cancellationToken)
    {
        foreach (var accountId in accountIds.Distinct().OrderBy(x => x))
            _ = await db.Accounts.FromSqlInterpolated($"SELECT * FROM \"Accounts\" WHERE \"WorkspaceId\" = {workspaceId} AND \"Id\" = {accountId} FOR UPDATE")
                .AsNoTracking().ToListAsync(cancellationToken);
        foreach (var allocationId in allocationIds.Distinct().OrderBy(x => x))
            _ = await db.SavingsAllocations.FromSqlInterpolated($"SELECT * FROM \"SavingsAllocations\" WHERE \"WorkspaceId\" = {workspaceId} AND \"Id\" = {allocationId} FOR UPDATE")
                .AsNoTracking().ToListAsync(cancellationToken);
    }

    public static Task<SavingsGoal?> LockGoalAsync(AppDbContext db, Guid workspaceId, Guid goalId, CancellationToken cancellationToken) =>
        db.SavingsGoals.FromSqlInterpolated($"SELECT * FROM \"SavingsGoals\" WHERE \"WorkspaceId\" = {workspaceId} AND \"Id\" = {goalId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}

public static class SavingsEstimate
{
    public static SavingsEstimateResult Calculate(decimal target, decimal saved, decimal? monthlyContribution,
        DateOnly? nextDate, DateOnly? desiredDate, string state, DateOnly today)
    {
        var remaining = Math.Max(0m, target - saved);
        if (remaining == 0m)
            return new(0m, true, 0, today, null, "Reached", "La meta ya alcanzó el monto objetivo.");
        if (state == "Closed")
            return new(remaining, false, null, null, null, "Closed", "La meta está cerrada y no tiene una fecha de cumplimiento estimada.");
        if (state == "Paused")
            return new(remaining, false, null, null, null, "Paused", "La meta está pausada; no se estima una fecha automática.");

        decimal? required = null;
        int? desiredCount = null;
        if (desiredDate is { } due)
        {
            if (due < today)
                return new(remaining, false, null, null, null, "DesiredDatePassed", "La fecha deseada ya pasó; elegí una fecha futura para estimar el aporte necesario.");
            var occurrences = EligibleOccurrenceCount(nextDate, today, due);
            if (occurrences == 0)
                return new(remaining, false, null, null, null, "NoContributionDateBeforeGoal", "No hay una fecha de aporte desde hoy hasta la fecha deseada.");
            desiredCount = occurrences;
            required = decimal.Ceiling((remaining / occurrences) * 100m) / 100m;
        }

        if (monthlyContribution is not > 0m || nextDate is null)
        {
            return new(remaining, false, desiredCount, null, required,
                desiredDate is null ? "NoMonthlyContribution" : "DesiredDateEstimate",
                desiredDate is null ? "Configurá un aporte mensual positivo y su próxima fecha para estimar cuándo llegarías." : "Este es el aporte mensual necesario para la fecha elegida.");
        }

        var countValue = decimal.Ceiling(remaining / monthlyContribution.Value);
        if (countValue > int.MaxValue)
            return new(remaining, false, desiredCount, null, required, "BeyondCalendarRange", "El aporte permitiría alcanzar la meta fuera del rango de fechas disponible.");
        var count = decimal.ToInt32(countValue);
        var firstIndex = FirstOccurrenceIndex(nextDate.Value, today);
        var completionIndex = (long)firstIndex + count - 1L;
        if (completionIndex > int.MaxValue)
            return new(remaining, false, desiredCount, null, required, "BeyondCalendarRange", "El aporte permitiría alcanzar la meta fuera del rango de fechas disponible.");
        DateOnly completion;
        try { completion = nextDate.Value.AddMonths((int)completionIndex); }
        catch (ArgumentOutOfRangeException)
        {
            return new(remaining, false, desiredCount, null, required, "BeyondCalendarRange", "El aporte permitiría alcanzar la meta fuera del rango de fechas disponible.");
        }
        return new(remaining, false, desiredCount ?? count, completion, required,
            "Estimated",
            required is null ? "La fecha estimada considera el aporte mensual indicado, sin rendimientos ni intereses." : "La fecha estimada usa el aporte indicado; el monto requerido se calcula para la fecha deseada.");
    }

    private static int EligibleOccurrenceCount(DateOnly? next, DateOnly today, DateOnly end)
    {
        if (next is null) return 0;
        var startIndex = FirstOccurrenceIndex(next.Value, today);
        var count = 0;
        var lastValidMonthIndex = (9999 - next.Value.Year) * 12 + 12 - next.Value.Month;
        for (var i = startIndex; i <= lastValidMonthIndex; i++)
        {
            var occurrence = next.Value.AddMonths(i);
            if (occurrence > end) break;
            count++;
        }
        return count;
    }

    private static int FirstOccurrenceIndex(DateOnly next, DateOnly today)
    {
        if (next >= today) return 0;
        var months = (today.Year - next.Year) * 12 + today.Month - next.Month;
        while (next.AddMonths(months) < today) months++;
        return months;
    }
}

public sealed class SavingsService(AppDbContext db, BusinessClock clock, WorkspaceMutationGate mutationGate, SavingsAccountBalanceQuery balanceQuery)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<SavingsGoalDto[]> ListAsync(WorkspaceRequestContext context, CancellationToken cancellationToken)
    {
        var goals = await db.SavingsGoals.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        var allocations = await db.SavingsAllocations.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && goals.Select(g => g.Id).Contains(x.GoalId))
            .OrderBy(x => x.Currency).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        var balances = await balanceQuery.GetAsync(context.Workspace.Id, cancellationToken);
        var deficits = balances.Where(x => decimal.Parse(x.BackingDeficit, Invariant) > 0m).Select(x => x.AccountId).ToHashSet();
        var choices = await db.SavingsGoalRateChoices.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && goals.Select(g => g.Id).Contains(x.GoalId)).ToDictionaryAsync(x => x.GoalId, cancellationToken);
        var snapshotIds = choices.Values.Where(x => x.SnapshotId.HasValue).Select(x => x.SnapshotId!.Value).Distinct().ToArray();
        var snapshots = await db.ExchangeRateSnapshots.AsNoTracking().Where(x => snapshotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var result = new List<SavingsGoalDto>(goals.Length);
        foreach (var goal in goals)
        {
            choices.TryGetValue(goal.Id, out var choice);
            ExchangeRateSnapshot? snapshot = null;
            if (choice?.SnapshotId is { } snapshotId) snapshots.TryGetValue(snapshotId, out snapshot);
            result.Add(ToDto(goal, allocations.Where(a => a.GoalId == goal.Id), allocations.Where(a => a.GoalId == goal.Id).Any(a => deficits.Contains(a.AccountId)), choice, snapshot));
        }
        return result.ToArray();
    }

    public async Task<(SavingsGoalDetailsDto? Result, int Status, string? Error)> GetDetailsAsync(WorkspaceRequestContext context, Guid goalId, int historyPage, CancellationToken ct)
    {
        const int pageSize = 50;
        var skipLong = ((long)historyPage - 1L) * pageSize;
        if (historyPage <= 0 || skipLong > int.MaxValue) return (null, 400, "La página del historial no es válida.");
        var goal = await db.SavingsGoals.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.Id == goalId, ct);
        if (goal is null) return (null, 404, "La meta no existe en este espacio.");
        var goalAllocations = await db.SavingsAllocations.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.GoalId == goalId).OrderBy(x => x.Id).ToArrayAsync(ct);
        var ids = goalAllocations.Select(x => x.Id).ToArray();
        var total = ids.Length == 0 ? 0 : await db.SavingsMovements.AsNoTracking().CountAsync(x => x.WorkspaceId == context.Workspace.Id && ids.Contains(x.AllocationId), ct);
        SavingsMovementDto[] movements;
        if (ids.Length == 0) movements = [];
        else
        {
            var movementRows = await (
                from movement in db.SavingsMovements.AsNoTracking()
                join actor in db.ApplicationUsers.AsNoTracking() on movement.ActorUserId equals actor.Id
                where movement.WorkspaceId == context.Workspace.Id && ids.Contains(movement.AllocationId)
                orderby movement.OccurredAt descending, movement.Id descending
                select new { Movement = movement, ActorDisplayName = actor.DisplayName ?? "Usuario", IsReversed = movement.Kind == "Apply" && db.SavingsMovements.Any(reverse => reverse.WorkspaceId == context.Workspace.Id && reverse.ReversesMovementId == movement.Id) }
            ).Skip((int)skipLong).Take(pageSize).ToArrayAsync(ct);
            movements = movementRows.Select(x => new SavingsMovementDto(x.Movement.Id, x.Movement.AllocationId, x.Movement.Kind, x.Movement.Currency, F(x.Movement.Amount), x.Movement.FinancePaymentId,
                x.Movement.ReversesMovementId, x.Movement.Reason, x.Movement.ActorUserId, x.ActorDisplayName, x.Movement.OccurredAt.ToString("O"), x.IsReversed)).ToArray();
        }
        var allocationDtos = goalAllocations.Select(x => new SavingsAllocationDto(x.Id, x.GoalId, x.AccountId, x.Currency, F(x.ReservedAmount), x.Version)).ToArray();
        return (new(await ToDtoWithBackingAsync(goal, goalAllocations, ct), allocationDtos, movements, historyPage, pageSize, total, skipLong + movements.Length < total), 200, null);
    }

    public async Task<(SavingsGoalDto? Result, int Status, string? Error)> CreateAsync(WorkspaceRequestContext context, SavingsGoalCreate request, CancellationToken cancellationToken)
    {
        if (!ValidId(request.Id) || !ValidGoal(request.Name, request.TargetCurrency, request.TargetAmount, request.DesiredDate, request.MonthlyContribution, request.NextContributionDate))
            return (null, 400, "Revisá nombre, moneda, monto, fecha deseada y aporte mensual de la meta.");
        var hash = Hash(request);
        var existing = await FindCommandAsync(context.Workspace.Id, request.Id, cancellationToken);
        if (existing is not null)
            return await ReplayAsync(context, existing, "GoalCreate", hash, cancellationToken);
        var goal = new SavingsGoal
        {
            Id = request.Id, WorkspaceId = context.Workspace.Id, Name = request.Name.Trim(), TargetCurrency = request.TargetCurrency,
            TargetAmount = ParseAmount(request.TargetAmount)!.Value, DesiredDate = ParseDate(request.DesiredDate), State = "Active",
            MonthlyContribution = ParseAmount(request.MonthlyContribution), NextContributionDate = ParseDate(request.NextContributionDate)
        };
        db.SavingsGoals.Add(goal);
        db.SavingsCommands.Add(Command(context.Workspace.Id, request.Id, "GoalCreate", hash, request.Id, clock.UtcNow));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var replay = await FindCommandAsync(context.Workspace.Id, request.Id, cancellationToken);
            if (replay is not null) return await ReplayAsync(context, replay, "GoalCreate", hash, cancellationToken);
            return (null, 409, "La meta ya existe o hubo un cambio concurrente.");
        }
        return (await ToDtoWithBackingAsync(goal, [], cancellationToken), 200, null);
    }

    public async Task<(SavingsGoalDto? Result, int Status, string? Error)> UpdateAsync(WorkspaceRequestContext context, Guid id, SavingsGoalUpdate request, CancellationToken cancellationToken)
    {
        if (!ValidId(request.RequestId) || request.Version < 1 || request.State is not ("Active" or "Paused")
            || !ValidGoal(request.Name, request.TargetCurrency, request.TargetAmount, request.DesiredDate, request.MonthlyContribution, request.NextContributionDate))
            return (null, 400, "Revisá los datos y el estado de la meta. Solo podés dejarla activa o pausada.");
        var hash = Hash(new { id, request });
        var command = await FindCommandAsync(context.Workspace.Id, request.RequestId, cancellationToken);
        if (command is not null) return await ReplayAsync(context, command, "GoalUpdate", hash, cancellationToken);
        var goal = await db.SavingsGoals.SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.Id == id, cancellationToken);
        if (goal is null) return (null, 404, "La meta no existe en este espacio.");
        if (goal.Version != request.Version || goal.State == "Closed") return (null, 409, "La meta cambió o ya está cerrada.");
        goal.Name = request.Name.Trim(); goal.TargetCurrency = request.TargetCurrency; goal.TargetAmount = ParseAmount(request.TargetAmount)!.Value;
        goal.DesiredDate = ParseDate(request.DesiredDate); goal.State = request.State;
        goal.MonthlyContribution = ParseAmount(request.MonthlyContribution); goal.NextContributionDate = ParseDate(request.NextContributionDate); goal.Version++;
        db.SavingsCommands.Add(Command(context.Workspace.Id, request.RequestId, "GoalUpdate", hash, id, clock.UtcNow));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return (null, 409, "La meta cambió. Actualizá la pantalla antes de guardar."); }
        var allocations = await db.SavingsAllocations.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.GoalId == id).ToArrayAsync(cancellationToken);
        return (await ToDtoWithBackingAsync(goal, allocations, cancellationToken), 200, null);
    }

    public async Task<(SavingsGoalDto? Result, int Status, string? Error)> CloseAsync(WorkspaceRequestContext context, ClaimsPrincipal principal, WorkspaceAccessService access, Guid id, SavingsGoalClose request, CancellationToken cancellationToken)
    {
        if (!ValidId(request.RequestId) || request.Version < 1) return (null, 400, "La solicitud de cierre no es válida.");
        var hash = Hash(new { id, request });
        var existing = await FindCommandAsync(context.Workspace.Id, request.RequestId, cancellationToken);
        if (existing is not null) return await ReplayAsync(context, existing, "GoalClose", hash, cancellationToken);
        if (!request.ConfirmRelease) return (null, 400, "Confirmá el retiro del dinero reservado para cerrar la meta.");

        var (lease, failure) = await mutationGate.AcquireAsync(context.Workspace.Id, cancellationToken);
        if (lease is null) return (null, failure, "El espacio no está disponible.");
        await using (lease)
        {
            // Shared lock order for Task 3 reservations and close: workspace, accounts by Id, allocations by Id, then the goal.
            var accountIds = await db.SavingsAllocations.AsNoTracking().Where(a => a.WorkspaceId == context.Workspace.Id && a.GoalId == id)
                .Select(a => a.AccountId).Distinct().OrderBy(x => x).ToArrayAsync(cancellationToken);
            var allocationIds = await db.SavingsAllocations.AsNoTracking().Where(a => a.WorkspaceId == context.Workspace.Id && a.GoalId == id)
                .Select(a => a.Id).OrderBy(x => x).ToArrayAsync(cancellationToken);
            await SavingsMutationLocks.LockAccountsThenAllocationsAsync(db, context.Workspace.Id, accountIds, allocationIds, cancellationToken);
            var (fresh, permissionFailure) = await access.ResolveWithinLockedWorkspaceAsync(principal, lease.Workspace, WorkspacePermissions.ManageResources, cancellationToken);
            if (fresh is null) return (null, permissionFailure, "Ya no tenés permiso para cerrar metas en este espacio.");
            var goal = await SavingsMutationLocks.LockGoalAsync(db, context.Workspace.Id, id, cancellationToken);
            if (goal is null) return (null, 404, "La meta no existe en este espacio.");
            var priorCommand = await FindCommandAsync(context.Workspace.Id, request.RequestId, cancellationToken);
            if (priorCommand is not null) return await ReplayAsync(context, priorCommand, "GoalClose", hash, cancellationToken);
            if (goal.Version != request.Version || goal.State == "Closed") return (null, 409, "La meta cambió o ya está cerrada.");
            var allocations = await db.SavingsAllocations.Where(a => a.WorkspaceId == context.Workspace.Id && a.GoalId == id).OrderBy(a => a.Id).ToArrayAsync(cancellationToken);
            var now = clock.UtcNow;
            db.SavingsCommands.Add(Command(context.Workspace.Id, request.RequestId, "GoalClose", hash, id, now));
            foreach (var allocation in allocations)
            {
                if (allocation.ReservedAmount <= 0m) continue;
                db.SavingsMovements.Add(new SavingsMovement { Id = Guid.NewGuid(), WorkspaceId = context.Workspace.Id, AllocationId = allocation.Id,
                    CommandId = request.RequestId, Kind = "Release", Currency = allocation.Currency, Amount = allocation.ReservedAmount,
                    ActorUserId = fresh.User.Id, OccurredAt = now });
                allocation.ReservedAmount = 0m; allocation.Version++;
            }
            goal.State = "Closed"; goal.Version++;
            lease.MarkChanged();
            try { await db.SaveChangesAsync(cancellationToken); await lease.CommitAsync(cancellationToken); }
            catch (DbUpdateException) { return (null, 409, "La meta o sus reservas cambiaron durante el cierre."); }
            return (await ToDtoWithBackingAsync(goal, allocations, cancellationToken), 200, null);
        }
    }

    private async Task<(SavingsGoalDto? Result, int Status, string? Error)> ReplayAsync(WorkspaceRequestContext context, SavingsCommand command, string kind, string hash, CancellationToken cancellationToken)
    {
        if (command.CommandKind != kind || command.PayloadHash != hash) return (null, 409, "El identificador de solicitud ya se usó con otros datos.");
        var goal = await db.SavingsGoals.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.Id == command.ResultResourceId, cancellationToken);
        if (goal is null) return (null, 409, "No se puede repetir la operación porque la meta ya no está disponible.");
        var allocations = await db.SavingsAllocations.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.GoalId == goal.Id).ToArrayAsync(cancellationToken);
        return (await ToDtoWithBackingAsync(goal, allocations, cancellationToken), 200, null);
    }

    private Task<SavingsCommand?> FindCommandAsync(Guid workspaceId, Guid requestId, CancellationToken cancellationToken) =>
        db.SavingsCommands.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.RequestId == requestId, cancellationToken);

    private static SavingsCommand Command(Guid workspaceId, Guid requestId, string kind, string hash, Guid result, DateTimeOffset now) =>
        new() { WorkspaceId = workspaceId, RequestId = requestId, CommandKind = kind, PayloadHash = hash, ResultResourceId = result, OccurredAt = now };

    private SavingsGoalDto ToDto(SavingsGoal goal, IEnumerable<SavingsAllocation> source, bool hasBackingDeficit = false,
        SavingsGoalRateChoiceEntity? selected = null, ExchangeRateSnapshot? snapshot = null)
    {
        var allocations = source.ToArray();
        var backing = allocations.GroupBy(x => x.Currency).OrderBy(x => x.Key).Select(x => new BackingAmountDto(x.Key, F(x.Sum(a => a.ReservedAmount)))).ToArray();
        var rate = selected?.Mode == "Manual" ? selected.ManualArsPerUsd : selected?.Side == "Compra" ? snapshot?.Buy : selected?.Side == "Venta" ? snapshot?.Sell : null;
        var rateChoice = rate is null ? null : new GoalRateChoiceDto(selected!.Mode, selected.SnapshotId, selected.House, selected.Side,
            rate.Value.ToString("0.00000000", Invariant), snapshot?.ProviderUpdatedAt.ToString("O", Invariant), snapshot?.FetchedAt.ToString("O", Invariant), selected.ManualReason,
            snapshot is not null && clock.UtcNow - snapshot.FetchedAt > TimeSpan.FromMinutes(5));
        if (hasBackingDeficit)
            return new(goal.Id, goal.Name, goal.TargetCurrency, F(goal.TargetAmount), D(goal.DesiredDate), goal.State, M(goal.MonthlyContribution), D(goal.NextContributionDate),
                false, backing, rateChoice, null, null, null, null, null, "BackingDeficit", "Una cuenta que respalda esta meta tiene menos efectivo que el total reservado; la estimación queda pendiente hasta corregir el respaldo.", goal.Version);
        var hasForeign = allocations.Any(x => x.ReservedAmount != 0m && x.Currency != goal.TargetCurrency);
        if (hasForeign && rate is null)
            return new(goal.Id, goal.Name, goal.TargetCurrency, F(goal.TargetAmount), D(goal.DesiredDate), goal.State, M(goal.MonthlyContribution), D(goal.NextContributionDate),
                false, backing, rateChoice, null, null, null, null, null, "RateRequired", "Hay reservas en otra moneda. Elegí una cotización explícita para calcular el equivalente.", goal.Version);
        decimal saved = 0m;
        foreach (var allocation in allocations)
        {
            if (allocation.Currency == goal.TargetCurrency) saved += allocation.ReservedAmount;
            else if (allocation.ReservedAmount != 0m && rate is { } selectedRate)
                saved += ExchangeRateMath.Convert(allocation.ReservedAmount, allocation.Currency, goal.TargetCurrency, selectedRate, selected!.Mode == "Manual" ? "Venta" : selected.Side!);
        }
        saved = decimal.Round(saved, 2, MidpointRounding.AwayFromZero);
        var estimate = SavingsEstimate.Calculate(goal.TargetAmount, saved, goal.MonthlyContribution, goal.NextContributionDate, goal.DesiredDate, goal.State, clock.Today);
        return new(goal.Id, goal.Name, goal.TargetCurrency, F(goal.TargetAmount), D(goal.DesiredDate), goal.State, M(goal.MonthlyContribution), D(goal.NextContributionDate),
            estimate.Reached, backing, rateChoice, F(saved), F(estimate.RemainingAmount), M(estimate.RequiredMonthlyContribution), estimate.RequiredContributionCount,
            D(estimate.EstimatedCompletionDate), estimate.EstimateStatus, estimate.Explanation, goal.Version);
    }

    private async Task<SavingsGoalDto> ToDtoWithBackingAsync(SavingsGoal goal, IEnumerable<SavingsAllocation> source, CancellationToken ct)
    {
        var selected = await db.SavingsGoalRateChoices.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == goal.WorkspaceId && x.GoalId == goal.Id, ct);
        var snapshot = selected?.SnapshotId is { } id ? await db.ExchangeRateSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        return ToDto(goal, source, await balanceQuery.GoalHasDeficitAsync(goal.WorkspaceId, goal.Id, ct), selected, snapshot);
    }

    private static bool ValidGoal(string? name, string? currency, string? target, string? desired, string? monthly, string? next) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120 && currency is "ARS" or "USD" && ParseAmount(target) is > 0m
        && (desired is null || ParseDate(desired) is not null) && (monthly is null || ParseAmount(monthly) is > 0m)
        && (next is null || ParseDate(next) is not null) && ((monthly is null) == (next is null));

    private static bool ValidId(Guid id) => id != Guid.Empty;
    private static decimal? ParseAmount(string? value)
    {
        if (value is null || value.Length > 32 || value.Contains(',') || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant, out var amount)
            || amount < 0m || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount) return null;
        return amount;
    }
    private static DateOnly? ParseDate(string? value) => value is not null && DateOnly.TryParseExact(value, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date) ? date : null;
    private static string F(decimal x) => x.ToString("0.00", Invariant);
    private static string? F(decimal? x) => x?.ToString("0.00", Invariant);
    private static string? M(decimal? x) => x?.ToString("0.00", Invariant);
    private static string? D(DateOnly? x) => x?.ToString("yyyy-MM-dd", Invariant);
    private static string Hash<T>(T payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
}
