using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.Api.Finance;

public static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this RouteGroupBuilder api)
    {
        var budgets = api.MapGroup("").AddEndpointFilter<FinanceWorkspaceFilter>();
        budgets.MapGet("/budgets", async (HttpContext http, BudgetService service, string? period, string? currency) =>
        {
            var result = await service.GetAsync(FinanceWorkspaceFilter.Get(http)!, period ?? "", currency ?? "", http.RequestAborted);
            return result.Status == 200 ? Results.Ok(result.Result) : Problem(result.Status, result.Error!);
        });
        budgets.MapPost("/budgets", async (HttpContext http, [FromBody] MonthlyBudgetCreate request, BudgetService service) => Result(await service.CreateAsync(http, request, http.RequestAborted)));
        budgets.MapPut("/budgets/{id:guid}", async (HttpContext http, [FromRoute] Guid id, [FromBody] MonthlyBudgetUpdate request, BudgetService service) => Result(await service.UpdateAsync(http, id, request, http.RequestAborted)));
    }

    private static IResult Result((BudgetDefinitionDto? Result, int Status, string? Error) response) => response.Status is 200 or 201
        ? response.Result is null ? Results.Problem(statusCode: 500) : Results.Json(response.Result, statusCode: response.Status)
        : Problem(response.Status, response.Error ?? "No se pudo completar la solicitud.");
    private static IResult Problem(int status, string error) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: error);
}
