using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Antiforgery;

namespace EnigmaBudget.Api.Savings;

public static class SavingsEndpoints
{
    public static void MapSavingsEndpoints(this RouteGroupBuilder api)
    {
        var savings = api.MapGroup("").AddEndpointFilter<FinanceWorkspaceFilter>();
        savings.MapGet("/savings/goals", ListGoals);
        savings.MapPost("/savings/goals", CreateGoal);
        savings.MapPut("/savings/goals/{id:guid}", UpdateGoal);
        savings.MapPost("/savings/goals/{id:guid}/close", CloseGoal);
    }

    private static async Task<IResult> ListGoals(HttpContext http, SavingsService service)
    {
        var context = FinanceWorkspaceFilter.Get(http)!;
        return Results.Ok(await service.ListAsync(context, http.RequestAborted));
    }

    private static async Task<IResult> CreateGoal(HttpContext http, IAntiforgery csrf, SavingsService service, SavingsGoalCreate request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida.");
        return Result(await service.CreateAsync(FinanceWorkspaceFilter.Get(http)!, request, http.RequestAborted));
    }

    private static async Task<IResult> UpdateGoal(HttpContext http, IAntiforgery csrf, SavingsService service, Guid id, SavingsGoalUpdate request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida.");
        return Result(await service.UpdateAsync(FinanceWorkspaceFilter.Get(http)!, id, request, http.RequestAborted));
    }

    private static async Task<IResult> CloseGoal(HttpContext http, IAntiforgery csrf, SavingsService service, Guid id, SavingsGoalClose request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida.");
        return Result(await service.CloseAsync(FinanceWorkspaceFilter.Get(http)!, id, request, http.RequestAborted));
    }

    private static IResult Result((SavingsGoalDto? Result, int Status, string? Error) response) =>
        response.Status == 200 ? Results.Ok(response.Result) : Problem(response.Status, response.Error ?? "No se pudo completar la solicitud.");

    private static IResult Problem(int status, string detail) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: detail);
}
