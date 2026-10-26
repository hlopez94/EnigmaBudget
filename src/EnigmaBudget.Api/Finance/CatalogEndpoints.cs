using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.Api.Finance;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this RouteGroupBuilder finance)
    {
        var catalog = finance.MapGroup("/finance/catalog");
        catalog.MapGet("", (HttpContext http, CatalogService service) => service.GetAsync(http));

        catalog.MapPost("/categories", (HttpContext http, [FromBody] CatalogCreate request, [FromServices] CatalogService service) => service.CreateCategoryAsync(http, request));
        catalog.MapPut("/categories/{id:guid}", (HttpContext http, [FromRoute] Guid id, [FromBody] CatalogUpdate request, [FromServices] CatalogService service) => service.UpdateCategoryAsync(http, id, request));
        catalog.MapDelete("/categories/{id:guid}", (HttpContext http, [FromRoute] Guid id, [FromBody] CatalogArchive request, [FromServices] CatalogService service) => service.ArchiveCategoryAsync(http, id, request));

        catalog.MapPost("/merchants", (HttpContext http, [FromBody] CatalogCreate request, [FromServices] CatalogService service) => service.CreateMerchantAsync(http, request));
        catalog.MapPut("/merchants/{id:guid}", (HttpContext http, [FromRoute] Guid id, [FromBody] CatalogUpdate request, [FromServices] CatalogService service) => service.UpdateMerchantAsync(http, id, request));
        catalog.MapDelete("/merchants/{id:guid}", (HttpContext http, [FromRoute] Guid id, [FromBody] CatalogArchive request, [FromServices] CatalogService service) => service.ArchiveMerchantAsync(http, id, request));

        catalog.MapPost("/income-sources", (HttpContext http, [FromBody] CatalogCreate request, [FromServices] CatalogService service) => service.CreateIncomeSourceAsync(http, request));
        catalog.MapPut("/income-sources/{id:guid}", (HttpContext http, [FromRoute] Guid id, [FromBody] CatalogUpdate request, [FromServices] CatalogService service) => service.UpdateIncomeSourceAsync(http, id, request));
        catalog.MapDelete("/income-sources/{id:guid}", (HttpContext http, [FromRoute] Guid id, [FromBody] CatalogArchive request, [FromServices] CatalogService service) => service.ArchiveIncomeSourceAsync(http, id, request));
    }
}
