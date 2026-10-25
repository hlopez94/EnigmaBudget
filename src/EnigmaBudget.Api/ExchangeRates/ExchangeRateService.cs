using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.ExchangeRates;

public sealed class ExchangeRateRefreshCoordinator(IServiceScopeFactory scopes, TimeProvider clock, IHostApplicationLifetime lifetime)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly object sync = new();
    private Task? inFlight;
    private DateTimeOffset? lastSuccess;
    private DateTimeOffset? cacheUntil;
    private string? lastError;

    public async Task RefreshAsync(CancellationToken callerToken)
    {
        Task task;
        lock (sync)
        {
            if (cacheUntil is { } validUntil && validUntil > clock.GetUtcNow()) return;
            if (inFlight is null || inFlight.IsCompleted)
                inFlight = RefreshOwnedAsync(lifetime.ApplicationStopping);
            task = inFlight;
        }
        await task.WaitAsync(callerToken);
    }

    private async Task RefreshOwnedAsync(CancellationToken shutdown)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<DolarApiClient>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fetchedAt = clock.GetUtcNow();
            var quotes = await client.GetDollarsAsync(shutdown);
            var seen = new HashSet<(string Currency, string House, DateTimeOffset UpdatedAt, decimal? Buy, decimal? Sell)>();
            foreach (var quote in quotes)
            {
                if (!seen.Add((quote.Currency, quote.House, quote.ProviderUpdatedAt, quote.Buy, quote.Sell))) continue;
                var exists = await db.ExchangeRateSnapshots.AnyAsync(x => x.Currency == quote.Currency && x.House == quote.House
                    && x.ProviderUpdatedAt == quote.ProviderUpdatedAt && x.Buy == quote.Buy && x.Sell == quote.Sell, shutdown);
                if (exists) continue;
                db.ExchangeRateSnapshots.Add(new ExchangeRateSnapshot { Id = Guid.NewGuid(), Currency = quote.Currency, House = quote.House,
                    Name = quote.Name, Buy = quote.Buy, Sell = quote.Sell, ProviderUpdatedAt = quote.ProviderUpdatedAt, FetchedAt = fetchedAt });
            }
            await db.SaveChangesAsync(shutdown);
            lock (sync) { lastSuccess = fetchedAt; cacheUntil = fetchedAt + CacheDuration; lastError = null; }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { lock (sync) { lastError = "La consulta de cotizaciones se interrumpió al cerrar la aplicación."; cacheUntil = null; } }
        catch (Exception) { lock (sync) { lastError = "No se pudo actualizar la cotización de DolarAPI."; cacheUntil = clock.GetUtcNow() + CacheDuration; } }
    }

    public (bool Stale, string Status, string? Error, DateTimeOffset? LastSuccess, DateTimeOffset? LatestFetch) GetState(DateTimeOffset? latestFetch)
    {
        lock (sync)
        {
            var stale = lastError is not null || cacheUntil is null || cacheUntil <= clock.GetUtcNow();
            return (stale, latestFetch is null ? "Sin datos" : stale ? "Desactualizada" : "Disponible", lastError, lastSuccess, latestFetch);
        }
    }
}

public sealed class ExchangeRateRefreshHostedService(ExchangeRateRefreshCoordinator coordinator, TimeProvider clock, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), clock, linked.Token);
                await coordinator.RefreshAsync(linked.Token);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
    }
}

public sealed class ExchangeRateService(AppDbContext db, ExchangeRateRefreshCoordinator coordinator, WorkspaceMutationGate mutationGate,
    WorkspaceAccessService access, TimeProvider clock)
{
    public async Task<ExchangeRateListDto> ListAsync(CancellationToken ct)
    {
        var rows = await db.ExchangeRateSnapshots.AsNoTracking().OrderBy(x => x.House).ThenByDescending(x => x.ProviderUpdatedAt).ThenByDescending(x => x.FetchedAt).ToArrayAsync(ct);
        var latest = rows.Select(x => (DateTimeOffset?)x.FetchedAt).Max();
        var state = coordinator.GetState(latest);
        var latestFetched = state.LastSuccess ?? latest;
        var snapshots = rows.Select(x => new ExchangeRateSnapshotDto(x.Id, x.Currency, x.House, x.Name, F(x.Buy), F(x.Sell), x.ProviderUpdatedAt.ToString("O", CultureInfo.InvariantCulture), x.FetchedAt.ToString("O", CultureInfo.InvariantCulture))).ToArray();
        return new(state.Status, state.Stale, state.Error, latestFetched?.ToString("O", CultureInfo.InvariantCulture), snapshots);
    }

    public async Task<ExchangeRateListDto> RefreshAsync(WorkspaceRequestContext context, ExchangeRateRefreshRequest request, CancellationToken ct)
    {
        if (request.Id == Guid.Empty) throw new ArgumentException("El identificador de actualización es obligatorio.");
        if (await db.ExchangeRateRefreshCommands.AnyAsync(x => x.WorkspaceId == context.Workspace.Id && x.RequestId == request.Id, ct)) return await ListAsync(ct);
        await coordinator.RefreshAsync(ct);
        db.ExchangeRateRefreshCommands.Add(new ExchangeRateRefreshCommand { WorkspaceId = context.Workspace.Id, RequestId = request.Id, OccurredAt = clock.GetUtcNow() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        return await ListAsync(ct);
    }

    public async Task<(GoalRateChoiceDto? Result, int Status, string? Error)> SetGoalChoiceAsync(WorkspaceRequestContext context, ClaimsPrincipal principal,
        Guid goalId, SavingsGoalRateChoice request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.GoalVersion < 1) return (null, 400, "La solicitud de cotización no es válida.");
        var choice = await ValidateChoiceAsync(request, ct);
        if (choice.Error is not null) return (null, 400, choice.Error);
        var canonical = JsonSerializer.Serialize(new { goalId, request });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var previous = await db.SavingsCommands.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.RequestId == request.RequestId, ct);
        if (previous is not null) return previous.CommandKind == "GoalRateChoice" && previous.PayloadHash == hash
            ? (await ReadChoiceAsync(context.Workspace.Id, goalId, ct), 200, null) : (null, 409, "El identificador de solicitud ya se usó con otros datos.");

        var (lease, failure) = await mutationGate.AcquireAsync(context.Workspace.Id, ct);
        if (lease is null) return (null, failure, "El espacio no está disponible.");
        await using (lease)
        {
            var (fresh, permissionFailure) = await access.ResolveWithinLockedWorkspaceAsync(principal, lease.Workspace, WorkspacePermissions.ManageResources, ct);
            if (fresh is null) return (null, permissionFailure, "Ya no tenés permiso para cambiar supuestos de metas en este espacio.");
            var goal = await SavingsMutationLocks.LockGoalAsync(db, context.Workspace.Id, goalId, ct);
            if (goal is null) return (null, 404, "La meta no existe en este espacio.");
            var existingCommand = await db.SavingsCommands.SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.RequestId == request.RequestId, ct);
            if (existingCommand is not null) return existingCommand.CommandKind == "GoalRateChoice" && existingCommand.PayloadHash == hash
                ? (await ReadChoiceAsync(context.Workspace.Id, goalId, ct), 200, null) : (null, 409, "El identificador de solicitud ya se usó con otros datos.");
            if (goal.Version != request.GoalVersion || goal.State == "Closed") return (null, 409, "La meta cambió o está cerrada. Actualizá la pantalla.");
            var entity = await db.SavingsGoalRateChoices.SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.GoalId == goalId, ct);
            if (entity is null)
            {
                entity = new SavingsGoalRateChoiceEntity { WorkspaceId = context.Workspace.Id, GoalId = goalId, Mode = request.Mode, Version = 0 };
                db.SavingsGoalRateChoices.Add(entity);
            }
            entity.Mode = request.Mode;
            entity.SnapshotId = choice.Snapshot?.Id;
            entity.House = request.Mode == "Snapshot" ? request.House!.Trim() : null;
            entity.Side = request.Mode == "Snapshot" ? request.Side : null;
            entity.ManualArsPerUsd = choice.ManualRate;
            entity.ManualReason = request.Mode == "Manual" ? request.ManualReason!.Trim() : null;
            entity.Version++;
            goal.Version++;
            db.SavingsCommands.Add(new SavingsCommand { WorkspaceId = context.Workspace.Id, RequestId = request.RequestId, CommandKind = "GoalRateChoice", PayloadHash = hash, ResultResourceId = goalId, OccurredAt = clock.GetUtcNow() });
            lease.MarkChanged();
            try { await db.SaveChangesAsync(ct); await lease.CommitAsync(ct); }
            catch (DbUpdateException) { return (null, 409, "La meta o el supuesto cambió. Actualizá la pantalla."); }
            return (await ReadChoiceAsync(context.Workspace.Id, goalId, ct), 200, null);
        }
    }

    private async Task<(ExchangeRateSnapshot? Snapshot, decimal? ManualRate, string? Error)> ValidateChoiceAsync(SavingsGoalRateChoice request, CancellationToken ct)
    {
        if (request.Mode == "Snapshot")
        {
            if (request.SnapshotId is null || string.IsNullOrWhiteSpace(request.House) || request.Side is not ("Compra" or "Venta") || request.ManualArsPerUsd is not null || request.ManualReason is not null)
                return (null, null, "Elegí una cotización guardada, una casa y el lado Compra o Venta.");
            var snapshot = await db.ExchangeRateSnapshots.SingleOrDefaultAsync(x => x.Id == request.SnapshotId && x.Currency == "USD" && x.House == request.House.Trim(), ct);
            if (snapshot is null || (request.Side == "Compra" ? snapshot.Buy : snapshot.Sell) is null)
                return (null, null, "La cotización no existe o no tiene disponible el lado elegido.");
            return (snapshot, null, null);
        }
        if (request.Mode == "Manual")
        {
            if (request.SnapshotId is not null || request.House is not null || request.Side is not null || string.IsNullOrWhiteSpace(request.ManualReason)
                || request.ManualReason.Trim().Length is < 1 or > 240 || !ParseManual(request.ManualArsPerUsd, out var value))
                return (null, null, "Ingresá un cambio manual positivo con hasta ocho decimales y explicá el motivo (1 a 240 caracteres).");
            return (null, value, null);
        }
        return (null, null, "El modo debe ser Snapshot o Manual.");
    }

    public async Task<GoalRateChoiceDto?> ReadChoiceAsync(Guid workspaceId, Guid goalId, CancellationToken ct)
    {
        var entity = await db.SavingsGoalRateChoices.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.GoalId == goalId, ct);
        if (entity is null) return null;
        var snapshot = entity.SnapshotId is null ? null : await db.ExchangeRateSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entity.SnapshotId, ct);
        var rate = entity.Mode == "Manual" ? entity.ManualArsPerUsd : entity.Side == "Compra" ? snapshot?.Buy : snapshot?.Sell;
        if (rate is null) return null;
        var stale = snapshot is not null && clock.GetUtcNow() - snapshot.FetchedAt > TimeSpan.FromMinutes(5);
        return new(entity.Mode, entity.SnapshotId, entity.House, entity.Side, rate.Value.ToString("0.00000000", CultureInfo.InvariantCulture),
            snapshot?.ProviderUpdatedAt.ToString("O", CultureInfo.InvariantCulture), snapshot?.FetchedAt.ToString("O", CultureInfo.InvariantCulture), entity.ManualReason, stale);
    }

    private static bool ParseManual(string? text, out decimal value)
    {
        value = 0m;
        if (text is null || text.Length > 32 || text.Any(c => !(char.IsAsciiDigit(c) || c == '.')) || text.Count(c => c == '.') > 1
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) || value <= 0m || value > 9999999999.99999999m || decimal.Round(value, 8) != value) return false;
        return true;
    }
    private static string? F(decimal? value) => value?.ToString("0.00000000", CultureInfo.InvariantCulture);
}
