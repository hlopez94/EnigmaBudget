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
        savings.MapGet("/savings/goals/{id:guid}/details", GoalDetails);
        savings.MapPost("/savings/goals", CreateGoal);
        savings.MapPut("/savings/goals/{id:guid}", UpdateGoal);
        savings.MapPost("/savings/goals/{id:guid}/close", CloseGoal);
        savings.MapPost("/savings/allocations", Allocate);
        savings.MapPost("/savings/allocations/{allocationId:guid}/release", Release);
        savings.MapPost("/savings/applications", Apply);
        savings.MapPost("/savings/applications/{movementId:guid}/reverse", ReverseApplication);
        savings.MapGet("/savings/balances", Balances);
        savings.MapPut("/savings/accounts/{accountId:guid}/liquidity", UpdateLiquidity);
    }

    private static async Task<IResult> ListGoals(HttpContext http, SavingsService service)
    {
        var context = FinanceWorkspaceFilter.Get(http)!;
        return Results.Ok(await service.ListAsync(context, http.RequestAborted));
    }

    private static async Task<IResult> GoalDetails(HttpContext http, SavingsService service, Guid id, int historyPage = 1)
    {
        var result = await service.GetDetailsAsync(FinanceWorkspaceFilter.Get(http)!, id, historyPage, http.RequestAborted);
        return result.Status == 200 ? Results.Ok(result.Result) : Problem(result.Status, result.Error ?? "No se pudo completar la solicitud.");
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

    private static async Task<IResult> CloseGoal(HttpContext http, IAntiforgery csrf, SavingsService service, WorkspaceAccessService access, Guid id, SavingsGoalClose request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida.");
        return Result(await service.CloseAsync(FinanceWorkspaceFilter.Get(http)!, http.User, access, id, request, http.RequestAborted));
    }

    private static async Task<IResult> Allocate(HttpContext http, IAntiforgery csrf, SavingsAllocationService service, SavingsAllocationRequest request)
    { if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida."); return AllocationResult(await service.AllocateAsync(FinanceWorkspaceFilter.Get(http)!, http.User, request, http.RequestAborted)); }
    private static async Task<IResult> Release(HttpContext http, IAntiforgery csrf, SavingsAllocationService service, Guid allocationId, SavingsReleaseRequest request)
    { if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida."); return AllocationResult(await service.ReleaseAsync(FinanceWorkspaceFilter.Get(http)!, http.User, allocationId, request, http.RequestAborted)); }
    private static async Task<IResult> Apply(HttpContext http, IAntiforgery csrf, SavingsAllocationService service, SavingsApplicationRequest request)
    { if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida."); return AllocationResult(await service.ApplyAsync(FinanceWorkspaceFilter.Get(http)!, http.User, request, http.RequestAborted)); }
    private static async Task<IResult> ReverseApplication(HttpContext http, IAntiforgery csrf, SavingsAllocationService service, Guid movementId, SavingsApplicationReverse request)
    { if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida."); return AllocationResult(await service.ReverseAsync(FinanceWorkspaceFilter.Get(http)!, http.User, movementId, request, http.RequestAborted)); }
    private static async Task<IResult> Balances(HttpContext http, SavingsAllocationService service, CancellationToken ct) => Results.Ok(await service.GetBalancesAsync(FinanceWorkspaceFilter.Get(http)!, ct));
    private static async Task<IResult> UpdateLiquidity(HttpContext http, IAntiforgery csrf, SavingsAllocationService service, Guid accountId, SavingsLiquidityUpdate request)
    { if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Problem(400, "La validación de seguridad de la solicitud no fue válida."); return ResultsResult(await service.UpdateLiquidityAsync(FinanceWorkspaceFilter.Get(http)!, http.User, accountId, request, http.RequestAborted)); }

    private static IResult Result((SavingsGoalDto? Result, int Status, string? Error) response) =>
        response.Status == 200 ? Results.Ok(response.Result) : Problem(response.Status, response.Error ?? "No se pudo completar la solicitud.");

    private static IResult AllocationResult((SavingsAllocationDto? Result, int Status, string? Error) response) => response.Status == 200 ? Results.Ok(response.Result) : Problem(response.Status, response.Error ?? "No se pudo completar la solicitud.");
    private static IResult ResultsResult((SavingsAccountBalanceDto? Result, int Status, string? Error) response) => response.Status == 200 ? Results.Ok(response.Result) : Problem(response.Status, response.Error ?? "No se pudo completar la solicitud.");

    private static IResult Problem(int status, string detail) => Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: detail);
}
