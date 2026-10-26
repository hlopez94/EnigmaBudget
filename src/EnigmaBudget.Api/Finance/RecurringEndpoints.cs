using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.Api.Finance;

public static class RecurringEndpoints
{
    public static void MapRecurringEndpoints(this RouteGroupBuilder api)
    {
        var routes = api.MapGroup("/recurrences");
        routes.MapGet("", (HttpContext http, RecurringService service) => service.ListAsync(http));
        routes.MapGet("/occurrences", async (HttpContext http, [FromQuery] string? from, [FromQuery] string? through, [FromQuery] string? cursor, RecurringCalendar calendar) =>
        {
            var result = await calendar.GetPageAsync(Workspaces.FinanceWorkspaceFilter.Get(http)!.Workspace.Id, from ?? "", through ?? "", cursor, http.RequestAborted);
            return result.Page is null ? Results.Problem(statusCode: result.Status, title: "No se pudo consultar el calendario", detail: result.Error) : Results.Ok(result.Page);
        });
        routes.MapPost("", (HttpContext http, [FromBody] RecurringRuleCreate request, [FromServices] RecurringService service) => service.CreateAsync(http, request));
        routes.MapPut("/{id:guid}", (HttpContext http, [FromRoute] Guid id, [FromBody] RecurringRuleUpdate request, [FromServices] RecurringService service) => service.UpdateAsync(http, id, request));
        routes.MapPost("/{id:guid}/state", (HttpContext http, [FromRoute] Guid id, [FromBody] RecurringRuleStateChange request, [FromServices] RecurringService service) => service.ChangeStateAsync(http, id, request));
        routes.MapPost("/{ruleId:guid}/occurrences/{scheduledDate}/record", (HttpContext http, [FromRoute] Guid ruleId, [FromRoute] string scheduledDate, [FromBody] RecurringOccurrenceRecord request, [FromServices] RecurringService service) => service.RecordOccurrenceAsync(http, ruleId, scheduledDate, request));
        routes.MapPost("/{ruleId:guid}/occurrences/{scheduledDate}/skip", (HttpContext http, [FromRoute] Guid ruleId, [FromRoute] string scheduledDate, [FromBody] RecurringOccurrenceSkip request, [FromServices] RecurringService service) => service.SkipOccurrenceAsync(http, ruleId, scheduledDate, request));
    }
}
