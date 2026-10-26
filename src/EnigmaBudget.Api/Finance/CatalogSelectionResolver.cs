using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Finance;

internal sealed record ResolvedOperationCatalog(
    Guid CategoryId,
    string CategoryName,
    Guid? MerchantId,
    string? MerchantName,
    Guid? IncomeSourceId,
    string? IncomeSourceName,
    string? SelectionHash,
    string? FailureDetail = null,
    int FailureStatus = 0
);

internal sealed record ResolvedPurchaseCatalog(
    Guid CategoryId,
    string CategoryName,
    Guid? MerchantId,
    string? MerchantName,
    string? SelectionHash,
    string? FailureDetail = null,
    int FailureStatus = 0
);

internal static class CatalogSelectionResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<ResolvedOperationCatalog> ResolveOperationAsync(
        AppDbContext db,
        Guid workspaceId,
        string kind,
        string? legacyCategory,
        Guid? categoryId,
        Guid? merchantId,
        Guid? incomeSourceId,
        CancellationToken ct
    )
    {
        if (incomeSourceId.HasValue && kind != "Income")
            return OperationFailure(400, "La fuente de ingreso solo corresponde a un ingreso.");

        var category = await ResolveCategoryAsync(db, workspaceId, kind, legacyCategory, categoryId, ct);
        if (category.FailureStatus != 0) return OperationFailure(category.FailureStatus, category.FailureDetail!);

        FinanceMerchant? merchant = null;
        if (merchantId.HasValue)
        {
            merchant = await db.FinanceMerchants.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == merchantId, ct);
            if (merchant is null) return OperationFailure(404, "El comercio no existe en este espacio.");
            if (merchant.IsArchived) return OperationFailure(409, "El comercio está archivado.");
        }

        FinanceIncomeSource? source = null;
        if (incomeSourceId.HasValue)
        {
            source = await db.FinanceIncomeSources.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == incomeSourceId, ct);
            if (source is null) return OperationFailure(404, "La fuente de ingreso no existe en este espacio.");
            if (source.IsArchived) return OperationFailure(409, "La fuente de ingreso está archivada.");
        }

        return new(category.Id, category.NameForSnapshot, merchant?.Id, merchant?.Name, source?.Id, source?.Name,
            SelectionHash(categoryId, merchantId, incomeSourceId));
    }

    public static async Task<ResolvedPurchaseCatalog> ResolvePurchaseAsync(
        AppDbContext db,
        Guid workspaceId,
        string? legacyCategory,
        Guid? categoryId,
        Guid? merchantId,
        CancellationToken ct
    )
    {
        var category = await ResolveCategoryAsync(db, workspaceId, "Expense", legacyCategory, categoryId, ct);
        if (category.FailureStatus != 0) return PurchaseFailure(category.FailureStatus, category.FailureDetail!);

        FinanceMerchant? merchant = null;
        if (merchantId.HasValue)
        {
            merchant = await db.FinanceMerchants.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == merchantId, ct);
            if (merchant is null) return PurchaseFailure(404, "El comercio no existe en este espacio.");
            if (merchant.IsArchived) return PurchaseFailure(409, "El comercio está archivado.");
        }
        return new(category.Id, category.NameForSnapshot, merchant?.Id, merchant?.Name, SelectionHash(categoryId, merchantId));
    }

    public static string? SelectionHash(params Guid?[] ids)
    {
        if (ids.All(x => x is null)) return null;
        var canonical = JsonSerializer.Serialize(ids.Select(x => x?.ToString("D")).ToArray(), JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static async Task<ResolvedCategory> ResolveCategoryAsync(
        AppDbContext db, Guid workspaceId, string kind, string? legacyCategory, Guid? categoryId, CancellationToken ct)
    {
        if (categoryId.HasValue)
        {
            var selected = await db.FinanceCategories.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == categoryId, ct);
            if (selected is null) return ResolvedCategory.Failure(404, "La categoría no existe en este espacio.");
            if (selected.Kind != kind) return ResolvedCategory.Failure(400, "La categoría no corresponde al tipo de movimiento.");
            if (selected.IsArchived) return ResolvedCategory.Failure(409, "La categoría está archivada.");
            return new(selected.Id, selected.Name, 0, null);
        }

        var trimmed = legacyCategory?.Trim() ?? "";
        var matched = trimmed.Length == 0 ? null : await db.FinanceCategories.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Kind == kind && x.Name == trimmed && !x.IsArchived, ct);
        var fallback = matched ?? await db.FinanceCategories.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Kind == kind && x.IsDefault, ct);
        if (fallback is null) throw new InvalidOperationException("No existe una categoría predeterminada protegida para el tipo seleccionado.");
        return new(fallback.Id, matched is not null ? matched.Name : trimmed, 0, null);
    }

    private static ResolvedOperationCatalog OperationFailure(int status, string detail) =>
        new(Guid.Empty, "", null, null, null, null, null, detail, status);
    private static ResolvedPurchaseCatalog PurchaseFailure(int status, string detail) =>
        new(Guid.Empty, "", null, null, null, detail, status);

    private sealed record ResolvedCategory(Guid Id, string NameForSnapshot, int FailureStatus, string? FailureDetail)
    {
        public static ResolvedCategory Failure(int status, string detail) => new(Guid.Empty, "", status, detail);
    }
}
