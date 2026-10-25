using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Finance;

public sealed class CatalogService(
    AppDbContext db,
    WorkspaceAccessService access,
    WorkspaceMutationGate gate,
    IAntiforgery csrf,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> CategoryKinds = new(["Expense", "Income"], StringComparer.Ordinal);
    private static readonly HashSet<string> IncomeKinds = new(["Salary", "Extra", "AnnualBonus", "IndependentWork", "Other"], StringComparer.Ordinal);

    public async Task<IResult> GetAsync(HttpContext http)
    {
        var workspaceId = FinanceWorkspaceFilter.Get(http)!.Workspace.Id;
        var categories = await db.FinanceCategories.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Kind).ThenBy(x => x.Name)
            .Select(x => new CategoryCatalogDto(x.Id, x.Name, x.Kind, x.IsArchived, x.IsDefault, x.Version)).ToArrayAsync(http.RequestAborted);
        var merchants = await db.FinanceMerchants.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Name)
            .Select(x => new MerchantCatalogDto(x.Id, x.Name, x.IsArchived, x.Version)).ToArrayAsync(http.RequestAborted);
        var sources = await db.FinanceIncomeSources.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Kind).ThenBy(x => x.Name)
            .Select(x => new IncomeSourceCatalogDto(x.Id, x.Name, x.Kind, x.IsArchived, x.Version)).ToArrayAsync(http.RequestAborted);
        return Results.Ok(new FinanceCatalogDto(categories, merchants, sources));
    }

    public Task<IResult> CreateCategoryAsync(HttpContext http, CatalogCreate request) => CreateAsync(http, "CategoryCreate", request.Id, request, async workspaceId =>
    {
        var name = NormalizeName(request.Name);
        if (request.Id == Guid.Empty || name is null || request.Kind is null || !CategoryKinds.Contains(request.Kind)) return Fail(400, "Revisá el nombre y el tipo de categoría.");
        var row = new FinanceCategory { Id = request.Id, WorkspaceId = workspaceId, Kind = request.Kind, Name = name };
        db.FinanceCategories.Add(row);
        return Ok(new CategoryCatalogDto(row.Id, row.Name, row.Kind, row.IsArchived, row.IsDefault, row.Version));
    });

    public Task<IResult> CreateMerchantAsync(HttpContext http, CatalogCreate request) => CreateAsync(http, "MerchantCreate", request.Id, request, async workspaceId =>
    {
        var name = NormalizeName(request.Name);
        if (request.Id == Guid.Empty || name is null || !string.IsNullOrEmpty(request.Kind)) return Fail(400, "Revisá el nombre del comercio.");
        var row = new FinanceMerchant { Id = request.Id, WorkspaceId = workspaceId, Name = name };
        db.FinanceMerchants.Add(row);
        return Ok(new MerchantCatalogDto(row.Id, row.Name, row.IsArchived, row.Version));
    });

    public Task<IResult> CreateIncomeSourceAsync(HttpContext http, CatalogCreate request) => CreateAsync(http, "IncomeSourceCreate", request.Id, request, async workspaceId =>
    {
        var name = NormalizeName(request.Name);
        if (request.Id == Guid.Empty || name is null || request.Kind is null || !IncomeKinds.Contains(request.Kind)) return Fail(400, "Revisá el nombre y el tipo de fuente de ingreso.");
        var row = new FinanceIncomeSource { Id = request.Id, WorkspaceId = workspaceId, Kind = request.Kind, Name = name };
        db.FinanceIncomeSources.Add(row);
        return Ok(new IncomeSourceCatalogDto(row.Id, row.Name, row.Kind, row.IsArchived, row.Version));
    });

    public Task<IResult> UpdateCategoryAsync(HttpContext http, Guid id, CatalogUpdate request) => UpdateAsync(http, "CategoryUpdate", id, request, async workspaceId =>
    {
        if (request.Kind is not null) return Fail(400, "El tipo de categoría no se puede modificar.");
        var row = await db.FinanceCategories.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, http.RequestAborted);
        if (row is null) return Fail(404, "La categoría no existe.");
        if (row.IsDefault) return Fail(409, "La categoría predeterminada está protegida.");
        if (request.Version != row.Version) return Fail(409, "La categoría cambió. Actualizá la pantalla.");
        var name = NormalizeName(request.Name);
        if (name is null) return Fail(400, "Revisá el nombre de la categoría.");
        row.Name = name; row.Version++;
        return Ok(new CategoryCatalogDto(row.Id, row.Name, row.Kind, row.IsArchived, row.IsDefault, row.Version));
    });

    public Task<IResult> UpdateMerchantAsync(HttpContext http, Guid id, CatalogUpdate request) => UpdateAsync(http, "MerchantUpdate", id, request, async workspaceId =>
    {
        if (request.Kind is not null) return Fail(400, "El comercio no tiene tipo modificable.");
        var row = await db.FinanceMerchants.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, http.RequestAborted);
        if (row is null) return Fail(404, "El comercio no existe.");
        if (request.Version != row.Version) return Fail(409, "El comercio cambió. Actualizá la pantalla.");
        var name = NormalizeName(request.Name);
        if (name is null) return Fail(400, "Revisá el nombre del comercio.");
        row.Name = name; row.Version++;
        return Ok(new MerchantCatalogDto(row.Id, row.Name, row.IsArchived, row.Version));
    });

    public Task<IResult> UpdateIncomeSourceAsync(HttpContext http, Guid id, CatalogUpdate request) => UpdateAsync(http, "IncomeSourceUpdate", id, request, async workspaceId =>
    {
        if (request.Kind is not null) return Fail(400, "El tipo de fuente de ingreso no se puede modificar.");
        var row = await db.FinanceIncomeSources.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, http.RequestAborted);
        if (row is null) return Fail(404, "La fuente de ingreso no existe.");
        if (request.Version != row.Version) return Fail(409, "La fuente de ingreso cambió. Actualizá la pantalla.");
        var name = NormalizeName(request.Name);
        if (name is null) return Fail(400, "Revisá el nombre de la fuente de ingreso.");
        row.Name = name; row.Version++;
        return Ok(new IncomeSourceCatalogDto(row.Id, row.Name, row.Kind, row.IsArchived, row.Version));
    });

    public Task<IResult> ArchiveCategoryAsync(HttpContext http, Guid id, CatalogArchive request) => ArchiveAsync(http, "CategoryArchive", id, request, async workspaceId =>
    {
        var row = await db.FinanceCategories.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, http.RequestAborted);
        if (row is null) return Fail(404, "La categoría no existe.");
        if (row.IsDefault) return Fail(409, "La categoría predeterminada está protegida.");
        if (request.Version != row.Version) return Fail(409, "La categoría cambió. Actualizá la pantalla.");
        row.IsArchived = true; row.Version++;
        return Ok(new CategoryCatalogDto(row.Id, row.Name, row.Kind, row.IsArchived, row.IsDefault, row.Version));
    });

    public Task<IResult> ArchiveMerchantAsync(HttpContext http, Guid id, CatalogArchive request) => ArchiveAsync(http, "MerchantArchive", id, request, async workspaceId =>
    {
        var row = await db.FinanceMerchants.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, http.RequestAborted);
        if (row is null) return Fail(404, "El comercio no existe.");
        if (request.Version != row.Version) return Fail(409, "El comercio cambió. Actualizá la pantalla.");
        row.IsArchived = true; row.Version++;
        return Ok(new MerchantCatalogDto(row.Id, row.Name, row.IsArchived, row.Version));
    });

    public Task<IResult> ArchiveIncomeSourceAsync(HttpContext http, Guid id, CatalogArchive request) => ArchiveAsync(http, "IncomeSourceArchive", id, request, async workspaceId =>
    {
        var row = await db.FinanceIncomeSources.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, http.RequestAborted);
        if (row is null) return Fail(404, "La fuente de ingreso no existe.");
        if (request.Version != row.Version) return Fail(409, "La fuente de ingreso cambió. Actualizá la pantalla.");
        row.IsArchived = true; row.Version++;
        return Ok(new IncomeSourceCatalogDto(row.Id, row.Name, row.Kind, row.IsArchived, row.Version));
    });

    private Task<IResult> CreateAsync<T>(HttpContext http, string kind, Guid id, T payload, Func<Guid, Task<MutationResult>> apply) => MutateAsync(http, kind, id, payload, 201, apply);
    private Task<IResult> UpdateAsync<T>(HttpContext http, string kind, Guid id, T payload, Func<Guid, Task<MutationResult>> apply) => MutateAsync(http, kind, id, payload, 200, apply);
    private Task<IResult> ArchiveAsync(HttpContext http, string kind, Guid id, CatalogArchive payload, Func<Guid, Task<MutationResult>> apply) => MutateAsync(http, kind, id, payload, 200, apply);

    private async Task<IResult> MutateAsync<T>(HttpContext http, string kind, Guid targetId, T payload, int successStatus, Func<Guid, Task<MutationResult>> apply)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.BadRequest();
        var initial = FinanceWorkspaceFilter.Get(http)!;
        var (lease, lockStatus) = await gate.AcquireAsync(initial.Workspace.Id, http.RequestAborted);
        if (lease is null) return Failure(lockStatus, "El espacio no existe.");
        await using var mutation = lease;
        var (context, authorizationStatus) = await access.ResolveWithinLockedWorkspaceAsync(http.User, mutation.Workspace, WorkspacePermissions.ManageResources, http.RequestAborted);
        if (context is null) return Failure(authorizationStatus, "No tenés permiso para administrar este catálogo.");
        var requestId = payload switch { CatalogCreate create => create.Id, CatalogUpdate update => update.RequestId, CatalogArchive archive => archive.RequestId, _ => Guid.Empty };
        if (requestId == Guid.Empty || targetId == Guid.Empty) return Failure(400, "El identificador de solicitud no es válido.");
        var serialized = JsonSerializer.Serialize(payload, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + targetId.ToString("D") + "\n" + serialized)));
        var existing = await db.CatalogCommands.SingleOrDefaultAsync(x => x.WorkspaceId == context.Workspace.Id && x.RequestId == requestId, http.RequestAborted);
        if (existing is not null)
        {
            if (existing.CommandKind != kind || existing.TargetResourceId != targetId || existing.PayloadHash != hash)
                return Failure(409, "El identificador de solicitud ya se usó con otros datos.");
            return Results.Content(existing.ResultJson, "application/json", Encoding.UTF8, StatusCodes.Status200OK);
        }
        var result = await apply(context.Workspace.Id);
        if (result.Error is not null) return Failure(result.Status, result.Error);
        var json = JsonSerializer.Serialize(result.Value, result.Value!.GetType(), JsonOptions);
        db.CatalogCommands.Add(new CatalogCommand
        {
            WorkspaceId = context.Workspace.Id, RequestId = requestId, CommandKind = kind, TargetResourceId = targetId,
            PayloadHash = hash, ResultJson = json, ActorUserId = context.User.Id, OccurredAt = clock.GetUtcNow()
        });
        db.WorkspaceAuditEvents.Add(new WorkspaceAuditEvent
        {
            WorkspaceId = context.Workspace.Id, ActorUserId = context.User.Id, Action = $"finance.catalog.{kind.ToLowerInvariant()}",
            ResourceType = "FinanceCatalog", ResourceId = targetId.ToString("D"), OccurredAt = clock.GetUtcNow(),
            ChangesJson = JsonSerializer.Serialize(new { requestId, kind }, JsonOptions)
        });
        mutation.MarkChanged();
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
            await mutation.CommitAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(409, "El catálogo cambió. Actualizá la pantalla.");
        }
        catch (DbUpdateException)
        {
            return Failure(409, "El nombre ya existe o el catálogo cambió. Actualizá la pantalla.");
        }
        return Results.Json(result.Value, statusCode: successStatus);
    }

    private static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 80 || trimmed.Any(char.IsControl)) return null;
        return trimmed;
    }

    private static MutationResult Ok(object value) => new(200, value, null);
    private static MutationResult Fail(int status, string message) => new(status, null, message);
    private static IResult Failure(int status, string message) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: message);
    private sealed record MutationResult(int Status, object? Value, string? Error);
}
