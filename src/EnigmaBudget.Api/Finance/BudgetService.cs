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

public sealed record BudgetCategoryRowDto(Guid CategoryId, string CategoryName, string Currency, Guid? BudgetId, int? Version, bool IsConfigured, string LimitAmount, string SpentAmount, string CommittedCardAmount, string RemainingAmount, IReadOnlyList<BudgetSourceRowDto> Sources);
public sealed record BudgetSourceRowDto(string SourceKind, Guid SourceId, string? ComponentId, string Date, string Description, string CategorySnapshot, string Amount);
public sealed record BudgetDefinitionDto(Guid Id, string Period, Guid CategoryId, string Currency, string LimitAmount, int Version);
public sealed record MonthlyBudgetCreate(Guid Id, string Period, Guid CategoryId, string Currency, string LimitAmount);
public sealed record MonthlyBudgetUpdate(Guid RequestId, int Version, string LimitAmount);

public sealed class BudgetService(AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, IAntiforgery csrf, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<(IReadOnlyList<BudgetCategoryRowDto>? Result, int Status, string? Error)> GetAsync(WorkspaceRequestContext context, string period, string currency, CancellationToken ct)
    {
        if (!ValidPeriod(period) || !ValidCurrency(currency)) return (null, 400, "El período o la moneda no son válidos.");
        var (start, end) = Range(period);
        var defaultCategory = await db.FinanceCategories.AsNoTracking().SingleAsync(x => x.WorkspaceId == context.Workspace.Id && x.Kind == "Expense" && x.IsDefault, ct);
        var expenseCategoryIds = await db.FinanceCategories.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.Kind == "Expense").Select(x => x.Id).ToHashSetAsync(ct);
        Guid ResolveExpenseCategory(Guid? categoryId) => categoryId is Guid id && expenseCategoryIds.Contains(id) ? id : defaultCategory.Id;
        var operationData = await db.FinanceOperations.AsNoTracking()
            .Where(x => x.WorkspaceId == context.Workspace.Id && x.Kind == "Expense" && !x.IsVoided && x.Currency == currency && x.Date >= start && (end == DateOnly.MaxValue ? x.Date <= end : x.Date < end))
            .ToListAsync(ct);
        var operationIds = operationData.Select(x => x.Id).ToArray();
        var cashPayments = await db.FinancePayments.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && operationIds.Contains(x.OperationId)).ToArrayAsync(ct);
        var nonCashSettlements = await db.NonCashSettlements.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && operationIds.Contains(x.OperationId)).ToArrayAsync(ct);
        var operationContributions = operationData.ToDictionary(x => x.Id, x => OperationSettlementCalculator.Calculate(x,
            cashPayments.Where(p => p.OperationId == x.Id).ToArray(), nonCashSettlements.Where(s => s.OperationId == x.Id).ToArray()).OperationMonthBudgetContribution);
        var operations = operationData.Select(x => new BudgetSourceRowDto("Operation", x.Id, null, x.Date.ToString("yyyy-MM-dd"), x.Description, x.Category, F(operationContributions[x.Id]))).ToArray();
        var installmentData = await (from installment in db.Installments.AsNoTracking()
            join purchase in db.Purchases.AsNoTracking() on new { installment.WorkspaceId, installment.PurchaseId } equals new { purchase.WorkspaceId, PurchaseId = purchase.Id }
            where installment.WorkspaceId == context.Workspace.Id && installment.Period == period && installment.Currency == currency
            select new { installment.Id, installment.PurchaseId, Date = purchase.Date, installment.Description, installment.Number, installment.Count, installment.Amount, purchase.CategoryId, purchase.Category })
            .ToListAsync(ct);
        var adjustmentData = await (from adjustment in db.StatementAdjustments.AsNoTracking()
            join statement in db.CardStatements.AsNoTracking() on new { adjustment.WorkspaceId, StatementId = adjustment.StatementId } equals new { statement.WorkspaceId, StatementId = statement.Id }
            where adjustment.WorkspaceId == context.Workspace.Id && statement.Period == period && statement.Currency == currency && statement.State == "Confirmed"
            select new { adjustment.Id, adjustment.Description, adjustment.Category, adjustment.CategoryId, adjustment.Amount, statement.Period })
            .ToListAsync(ct);
        var installmentSources = installmentData.Select(x => new BudgetSourceRowDto("Installment", x.Id, null, x.Date.ToString("yyyy-MM-dd"), $"{x.Description} · Cuota {x.Number}/{x.Count}", x.Category, F(x.Amount))).ToArray();
        var adjustmentSources = adjustmentData.Select(x => new BudgetSourceRowDto("StatementAdjustment", x.Id, null, x.Period + "-01", $"{x.Description} · Período {x.Period}", x.Category, F(x.Amount))).ToArray();
        var budgets = await db.MonthlyBudgets.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && x.Period == period && x.Currency == currency).ToDictionaryAsync(x => x.CategoryId, ct);
        var relevant = operationData.Select(x => ResolveExpenseCategory(x.CategoryId))
            .Concat(installmentData.Select(x => ResolveExpenseCategory(x.CategoryId)))
            .Concat(adjustmentData.Select(x => ResolveExpenseCategory(x.CategoryId))).Concat(budgets.Keys).ToHashSet();
        var categories = await db.FinanceCategories.AsNoTracking()
            .Where(x => x.WorkspaceId == context.Workspace.Id && x.Kind == "Expense" && (!x.IsArchived || relevant.Contains(x.Id)))
            .OrderBy(x => x.Name).ToListAsync(ct);
        var rows = new List<BudgetCategoryRowDto>();
        foreach (var category in categories)
        {
            var id = category.Id;
            var sources = operations.Where((_, i) => ResolveExpenseCategory(operationData[i].CategoryId) == id)
                .Concat(installmentSources.Where((_, i) => ResolveExpenseCategory(installmentData[i].CategoryId) == id))
                .Concat(adjustmentSources.Where((_, i) => ResolveExpenseCategory(adjustmentData[i].CategoryId) == id))
                .OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.SourceKind, StringComparer.Ordinal).ThenBy(x => x.SourceId).ToArray();
            var spent = sources.Where(x => x.SourceKind == "Operation").Sum(x => decimal.Parse(x.Amount, Invariant));
            var card = sources.Where(x => x.SourceKind != "Operation").Sum(x => decimal.Parse(x.Amount, Invariant));
            budgets.TryGetValue(id, out var budget);
            var limit = budget?.Limit ?? 0m;
            rows.Add(new BudgetCategoryRowDto(id, category.Name, currency, budget?.Id, budget?.Version, budget is not null, F(limit), F(spent), F(card), F(limit - spent - card), sources));
        }
        return (rows, 200, null);
    }

    public async Task<(BudgetDefinitionDto? Result, int Status, string? Error)> CreateAsync(HttpContext http, MonthlyBudgetCreate request, CancellationToken ct)
    {
        var raw = JsonSerializer.Serialize(request, JsonOptions);
        return await MutateAsync(http, "Create", request.Id, request.Id, raw, 201, async workspaceId =>
        {
            if (request.Id == Guid.Empty || request.CategoryId == Guid.Empty || !ValidPeriod(request.Period) || !ValidCurrency(request.Currency) || !MoneyRules.Money(request.LimitAmount, false, out var limit)) return Fail(400, "Revisá el período, la moneda y el límite.");
            var category = await db.FinanceCategories.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == request.CategoryId, ct);
            if (category is null) return Fail(404, "La categoría no existe.");
            if (category.Kind != "Expense" || category.IsArchived) return Fail(400, "El presupuesto requiere una categoría de gasto activa.");
            if (await db.MonthlyBudgets.AnyAsync(x => x.WorkspaceId == workspaceId && x.Period == request.Period && x.CategoryId == request.CategoryId && x.Currency == request.Currency, ct)) return Fail(409, "Ya existe un presupuesto para esa categoría, período y moneda.");
            var row = new MonthlyBudget { Id = request.Id, WorkspaceId = workspaceId, Period = request.Period, CategoryId = request.CategoryId, Currency = request.Currency, Limit = limit };
            db.MonthlyBudgets.Add(row);
            return Ok(new BudgetDefinitionDto(row.Id, row.Period, row.CategoryId, row.Currency, F(row.Limit), row.Version));
        }, ct);
    }

    public async Task<(BudgetDefinitionDto? Result, int Status, string? Error)> UpdateAsync(HttpContext http, Guid id, MonthlyBudgetUpdate request, CancellationToken ct)
    {
        var raw = JsonSerializer.Serialize(new { id, request }, JsonOptions);
        return await MutateAsync(http, "Update", id, request.RequestId, raw, 200, async workspaceId =>
        {
            if (id == Guid.Empty || request.RequestId == Guid.Empty || request.Version < 1 || !MoneyRules.Money(request.LimitAmount, false, out var limit)) return Fail(400, "Revisá la solicitud y el límite.");
            var row = await db.MonthlyBudgets.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, ct);
            if (row is null) return Fail(404, "El presupuesto no existe.");
            if (request.Version != row.Version) return Fail(409, "El presupuesto cambió. Actualizá la pantalla.");
            row.Limit = limit; row.Version++;
            return Ok(new BudgetDefinitionDto(row.Id, row.Period, row.CategoryId, row.Currency, F(row.Limit), row.Version));
        }, ct);
    }

    private async Task<(BudgetDefinitionDto? Result, int Status, string? Error)> MutateAsync(HttpContext http, string kind, Guid targetId, Guid requestId, string raw, int successStatus, Func<Guid, Task<MutationResult>> apply, CancellationToken ct)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return (null, 400, "La validación de seguridad de la solicitud no fue válida.");
        var initial = FinanceWorkspaceFilter.Get(http)!;
        var (lease, lockStatus) = await gate.AcquireAsync(initial.Workspace.Id, ct);
        if (lease is null) return (null, lockStatus, "El espacio no existe.");
        await using var mutation = lease;
        var (context, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageResources, ct);
        if (context is null) return (null, authStatus, "No tenés permiso para administrar presupuestos.");
        if (requestId == Guid.Empty || targetId == Guid.Empty) return (null, 400, "El identificador de solicitud no es válido.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + targetId.ToString("D") + "\n" + raw)));
        var old = await db.BudgetCommands.SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.RequestId == requestId, ct);
        if (old is not null)
        {
            if (old.CommandKind != kind || old.TargetBudgetId != targetId || old.PayloadHash != hash) return (null, 409, "El identificador de solicitud ya se usó con otros datos.");
            return (JsonSerializer.Deserialize<BudgetDefinitionDto>(old.ResultJson, JsonOptions), 200, null);
        }
        var result = await apply(context.Workspace.Id);
        if (result.Error is not null) return (null, result.Status, result.Error);
        var json = JsonSerializer.Serialize(result.Value, JsonOptions);
        db.BudgetCommands.Add(new BudgetCommand { WorkspaceId = context.Workspace.Id, RequestId = requestId, CommandKind = kind, TargetBudgetId = targetId, PayloadHash = hash, ResultJson = json, ActorUserId = context.User.Id, OccurredAt = clock.GetUtcNow() });
        db.WorkspaceAuditEvents.Add(new WorkspaceAuditEvent { WorkspaceId = context.Workspace.Id, ActorUserId = context.User.Id, Action = $"finance.budget.{kind.ToLowerInvariant()}", ResourceType = "MonthlyBudget", ResourceId = targetId.ToString("D"), OccurredAt = clock.GetUtcNow(), ChangesJson = JsonSerializer.Serialize(new { requestId, kind }, JsonOptions) });
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(ct); await mutation.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { return (null, 409, "El presupuesto cambió. Actualizá la pantalla."); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg && pg.SqlState is "23505" or "23503") { return (null, 409, "El presupuesto ya existe o cambió. Actualizá la pantalla."); }
        return ((BudgetDefinitionDto)result.Value!, successStatus, null);
    }

    public static bool ValidPeriod(string? period)
    {
        if (period is null || period.Length != 7 || period[4] != '-') return false;
        for (var i = 0; i < period.Length; i++) if (i != 4 && (period[i] < '0' || period[i] > '9')) return false;
        return int.TryParse(period[..4], NumberStyles.None, Invariant, out var year) && year >= 2000 && int.TryParse(period[5..], NumberStyles.None, Invariant, out var month) && month is >= 1 and <= 12;
    }
    private static (DateOnly Start, DateOnly End) Range(string period)
    {
        var y = int.Parse(period[..4], Invariant); var m = int.Parse(period[5..], Invariant);
        var start = new DateOnly(y, m, 1);
        return (start, y == 9999 && m == 12 ? DateOnly.MaxValue : start.AddMonths(1));
    }
    private static bool ValidCurrency(string? currency) => currency is "ARS" or "USD";
    private static string F(decimal value) => value.ToString("0.00", Invariant);
    private static MutationResult Ok(BudgetDefinitionDto value) => new(200, value, null);
    private static MutationResult Fail(int status, string error) => new(status, null, error);
    private sealed record MutationResult(int Status, BudgetDefinitionDto? Value, string? Error);
}
