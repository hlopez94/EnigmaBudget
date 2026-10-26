using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Finance;

internal static class OperationSettlementMapping
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    internal static string F(decimal amount) => amount.ToString("0.00", Invariant);

    internal static OperationDto Map(FinanceOperation operation, IReadOnlyList<FinancePayment> payments,
        IReadOnlyList<NonCashSettlement> settlements)
    {
        var calculation = OperationSettlementCalculator.Calculate(operation, payments, settlements);
        return new OperationDto(operation.Id, operation.Kind, operation.AccountId,
            operation.Date.ToString("yyyy-MM-dd"), operation.Description, operation.Category,
            F(operation.Amount), operation.Currency, F(calculation.CashPaidAmount), F(calculation.PendingAmount),
            calculation.Status, operation.Version,
            payments.OrderBy(x => x.Date).ThenBy(x => x.Id).Select(x => new PaymentDto(x.Id, x.AccountId,
                x.Date.ToString("yyyy-MM-dd"), F(x.Amount))).ToArray(), operation.CategoryId, operation.MerchantId,
            operation.IncomeSourceId, operation.MerchantNameSnapshot, operation.IncomeSourceNameSnapshot,
            F(calculation.CashPaidAmount), F(calculation.NonCashSettledAmount),
            settlements.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new NonCashSettlementDto(
                x.Id, x.Kind, x.Currency, F(x.Amount), x.HealthcareComponentId, x.CardPurchaseId,
                x.ReversesSettlementId, x.ActorUserId, x.CreatedAt.ToString("O", Invariant))).ToArray());
    }

    internal static string Hash<T>(T request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
}

public sealed class OperationSettlementService(AppDbContext db, WorkspaceAccessService access, WorkspaceMutationGate gate, TimeProvider clock)
{
    public async Task<IResult> CreateAsync(HttpContext http, Microsoft.AspNetCore.Antiforgery.IAntiforgery csrf, Guid id, NonCashSettlementCreate request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.BadRequest();
        if (request.RequestId == Guid.Empty || request.OperationVersion < 1 || request.Currency is not ("ARS" or "USD")
            || !MoneyRules.Money(request.Amount, false, out var amount) || amount <= 0m
            || (request.HealthcareComponentId is null) == (request.CardPurchaseId is null))
            return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: "Revisá los datos de la liquidación.");
        var initial = FinanceWorkspaceFilter.Get(http)!;
        var (lease, status) = await gate.AcquireAsync(initial.Workspace.Id, http.RequestAborted);
        if (lease is null) return Results.NotFound();
        await using var mutation = lease;
        var (context, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.RecordFinance, http.RequestAborted);
        if (context is null) return Results.Problem(statusCode: authStatus, title: "No se pudo completar la solicitud", detail: "No tenés permiso para registrar movimientos.");
        var workspaceId = context.Workspace.Id;
        var operation = await LockOperationAsync(workspaceId, id, http.RequestAborted);
        if (operation is null) return Results.NotFound();
        var settlements = await LockSettlementsAsync(workspaceId, id, http.RequestAborted);
        var payments = await db.FinancePayments.Where(x => x.WorkspaceId == workspaceId && x.OperationId == id).ToArrayAsync(http.RequestAborted);
        var hash = OperationSettlementMapping.Hash(new { request.RequestId, request.OperationVersion, request.Currency, Amount = OperationSettlementMapping.F(amount), request.HealthcareComponentId, request.CardPurchaseId });
        var replay = settlements.SingleOrDefault(x => x.RequestId == request.RequestId);
        if (replay is not null)
            return replay.PayloadHash == hash ? Results.Ok(OperationSettlementMapping.Map(operation, payments, settlements)) : Conflict("El identificador ya se usó con otros datos.");
        if (operation.Kind != "Expense" || operation.IsVoided) return Conflict("El movimiento no admite liquidaciones.");
        if (operation.Version != request.OperationVersion) return Conflict("El movimiento cambió. Actualizá la pantalla.");
        if (operation.Currency != request.Currency) return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: "La moneda no coincide con el movimiento.");
        var kind = request.HealthcareComponentId is not null ? "PayrollWithholding" : "CardCharge";
        var row = new NonCashSettlement { Id = Guid.NewGuid(), WorkspaceId = workspaceId, OperationId = id,
            Currency = request.Currency, Kind = kind, Amount = amount, HealthcareComponentId = request.HealthcareComponentId,
            CardPurchaseId = request.CardPurchaseId, RequestId = request.RequestId, PayloadHash = hash,
            ActorUserId = context.User.Id, CreatedAt = clock.GetUtcNow() };
        var activeSource = settlements.Where(x => x.Kind != "Reversal").Where(root =>
            !settlements.Any(reversal => reversal.Kind == "Reversal" && reversal.ReversesSettlementId == root.Id));
        if (activeSource.Any(x => x.HealthcareComponentId == row.HealthcareComponentId && row.HealthcareComponentId != null
            || x.CardPurchaseId == row.CardPurchaseId && row.CardPurchaseId != null)) return Conflict("El origen ya está liquidado.");
        var current = OperationSettlementCalculator.Calculate(operation, payments, settlements);
        if (amount > current.PendingAmount) return Conflict("El importe supera el saldo pendiente.");
        db.NonCashSettlements.Add(row);
        if (!await OperationSettlementSourceValidator.ValidateAsync(db, operation, row, http.RequestAborted)) return Conflict("El origen de la liquidación no es válido.");
        operation.Version++;
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); }
        catch (Exception ex) when (ex is DbUpdateException or DbUpdateConcurrencyException) { return Conflict("El movimiento cambió durante la liquidación."); }
        return Results.Ok(OperationSettlementMapping.Map(operation, payments, [.. settlements, row]));
    }

    public async Task<IResult> ReverseAsync(HttpContext http, Microsoft.AspNetCore.Antiforgery.IAntiforgery csrf, Guid id, Guid settlementId, NonCashSettlementReverse request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.BadRequest();
        if (request.RequestId == Guid.Empty || request.OperationVersion < 1 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 240)
            return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: "Indicá el motivo de la corrección.");
        var initial = FinanceWorkspaceFilter.Get(http)!;
        var (lease, status) = await gate.AcquireAsync(initial.Workspace.Id, http.RequestAborted);
        if (lease is null) return Results.NotFound();
        await using var mutation = lease;
        var (context, authStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.CorrectHistory, http.RequestAborted);
        if (context is null) return Results.Problem(statusCode: authStatus, title: "No se pudo completar la solicitud", detail: "No tenés permiso para corregir el historial.");
        var workspaceId = context.Workspace.Id;
        var operation = await LockOperationAsync(workspaceId, id, http.RequestAborted);
        if (operation is null) return Results.NotFound();
        var settlements = await LockSettlementsAsync(workspaceId, id, http.RequestAborted);
        var payments = await db.FinancePayments.Where(x => x.WorkspaceId == workspaceId && x.OperationId == id).ToArrayAsync(http.RequestAborted);
        var hash = OperationSettlementMapping.Hash(new { request.RequestId, request.OperationVersion, Reason = request.Reason.Trim(), settlementId });
        var replay = settlements.SingleOrDefault(x => x.RequestId == request.RequestId);
        if (replay is not null) return replay.PayloadHash == hash ? Results.Ok(OperationSettlementMapping.Map(operation, payments, settlements)) : Conflict("El identificador ya se usó con otros datos.");
        if (operation.IsVoided || operation.Version != request.OperationVersion) return Conflict("El movimiento cambió.");
        var root = settlements.SingleOrDefault(x => x.Id == settlementId);
        if (root is null) return Results.NotFound();
        if (root.Kind != "PayrollWithholding" || settlements.Any(x => x.ReversesSettlementId == root.Id)) return Conflict("Solo se puede corregir una retención salarial activa.");
        var reversal = new NonCashSettlement { Id = Guid.NewGuid(), WorkspaceId = workspaceId, OperationId = id,
            Currency = root.Currency, Kind = "Reversal", Amount = root.Amount, ReversesSettlementId = root.Id,
            RequestId = request.RequestId, PayloadHash = hash, ActorUserId = context.User.Id, CreatedAt = clock.GetUtcNow() };
        db.NonCashSettlements.Add(reversal);
        if (!await OperationSettlementSourceValidator.ValidateAsync(db, operation, reversal, http.RequestAborted)) return Conflict("La corrección no es válida.");
        operation.Version++;
        mutation.MarkChanged();
        try { await db.SaveChangesAsync(http.RequestAborted); await mutation.CommitAsync(http.RequestAborted); }
        catch (Exception ex) when (ex is DbUpdateException or DbUpdateConcurrencyException) { return Conflict("El movimiento cambió durante la corrección."); }
        return Results.Ok(OperationSettlementMapping.Map(operation, payments, [.. settlements, reversal]));
    }

    internal static async Task<FinanceOperation?> LockOperationAsync(AppDbContext db, Guid workspaceId, Guid operationId, CancellationToken ct) =>
        await db.FinanceOperations.FromSqlInterpolated($"SELECT * FROM \"FinanceOperations\" WHERE \"WorkspaceId\" = {workspaceId} AND \"Id\" = {operationId} FOR UPDATE").SingleOrDefaultAsync(ct);

    private Task<FinanceOperation?> LockOperationAsync(Guid workspaceId, Guid operationId, CancellationToken ct) => LockOperationAsync(db, workspaceId, operationId, ct);
    internal static Task<NonCashSettlement[]> LockSettlementsAsync(AppDbContext db, Guid workspaceId, Guid operationId, CancellationToken ct) =>
        db.NonCashSettlements.FromSqlInterpolated($"SELECT * FROM \"NonCashSettlements\" WHERE \"WorkspaceId\" = {workspaceId} AND \"OperationId\" = {operationId} ORDER BY \"Id\" FOR UPDATE").ToArrayAsync(ct);
    private Task<NonCashSettlement[]> LockSettlementsAsync(Guid workspaceId, Guid operationId, CancellationToken ct) => LockSettlementsAsync(db, workspaceId, operationId, ct);
    private static IResult Conflict(string detail) => Results.Problem(statusCode: 409, title: "No se pudo completar la solicitud", detail: detail);
}
