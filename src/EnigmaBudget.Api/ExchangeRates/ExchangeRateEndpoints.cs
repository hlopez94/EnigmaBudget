using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Antiforgery;

namespace EnigmaBudget.Api.ExchangeRates;

public static class ExchangeRateEndpoints
{
    public static void MapExchangeRateEndpoints(this RouteGroupBuilder api)
    {
        var rates = api.MapGroup("").AddEndpointFilter<FinanceWorkspaceFilter>();
        rates.MapGet("/exchange-rates", async (ExchangeRateService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        rates.MapGet("/exchange-rates/snapshots/{snapshotId:guid}", async (ExchangeRateService service, Guid snapshotId, CancellationToken ct) =>
        {
            var snapshot = await service.GetSnapshotAsync(snapshotId, ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        });
        rates.MapPost("/exchange-rates/refresh", async (HttpContext http, IAntiforgery csrf, ExchangeRateService service, ExchangeRateRefreshRequest request) =>
        {
            if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: "La validación de seguridad de la solicitud no fue válida.");
            try { return Results.Ok(await service.RefreshAsync(FinanceWorkspaceFilter.Get(http)!, request, http.RequestAborted)); }
            catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: ex.Message); }
        });
        rates.MapPut("/savings/goals/{id:guid}/rate", async (HttpContext http, IAntiforgery csrf, ExchangeRateService service, Guid id, SavingsGoalRateChoice request) =>
        {
            if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: "La validación de seguridad de la solicitud no fue válida.");
            var result = await service.SetGoalChoiceAsync(FinanceWorkspaceFilter.Get(http)!, http.User, id, request, http.RequestAborted);
            return result.Status == 200 ? Results.Ok(result.Result) : Results.Problem(statusCode: result.Status, title: "No se pudo completar la solicitud", detail: result.Error);
        });
    }
}
