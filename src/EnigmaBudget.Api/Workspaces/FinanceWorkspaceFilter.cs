namespace EnigmaBudget.Api.Workspaces;

public sealed class FinanceWorkspaceFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        var http = invocationContext.HttpContext;
        var permission = RequiredPermission(http.Request.Method, http.Request.Path);
        var access = http.RequestServices.GetRequiredService<WorkspaceAccessService>();
        string? selectedWorkspaceId = null;
        if (http.Request.Headers.TryGetValue("X-Workspace-Id", out var selectedWorkspaceHeader))
            selectedWorkspaceId = selectedWorkspaceHeader.ToString();
        var (context, failure) = await access.ResolveAsync(http.User, selectedWorkspaceId, permission, http.RequestAborted);
        if (context is null)
            return Results.Problem(statusCode: failure, title: "No se pudo completar la solicitud", detail: failure == StatusCodes.Status404NotFound ? "El espacio no existe o no está disponible." : "No tenés permiso para realizar esta acción en el espacio.");
        http.Items[WorkspaceRequestContextItem.Key] = context;
        return await next(invocationContext);
    }

    public static WorkspaceRequestContext? Get(HttpContext http) => http.Items.TryGetValue(WorkspaceRequestContextItem.Key, out var context) ? context as WorkspaceRequestContext : null;

    private static string RequiredPermission(string method, PathString path)
    {
        var p = path.Value ?? "";
        if (p.Equals("/api/finance/catalog", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return WorkspacePermissions.Read;
        if (p.Equals("/api/recurrences", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/api/recurrences/", StringComparison.OrdinalIgnoreCase))
            return method == "GET" ? WorkspacePermissions.Read : WorkspacePermissions.ManageResources;
        if (p.Equals("/api/budgets", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/api/budgets/", StringComparison.OrdinalIgnoreCase))
            return method == "GET" ? WorkspacePermissions.Read : WorkspacePermissions.ManageResources;
        if (p.StartsWith("/api/finance/catalog/", StringComparison.OrdinalIgnoreCase)
            && (method == "POST" || method == "PUT" || method == "DELETE"))
            return WorkspacePermissions.ManageResources;
        if ((p.Equals("/api/exchange-rates/refresh", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/api/projection/calculate", StringComparison.OrdinalIgnoreCase)) && method == "POST")
            return WorkspacePermissions.Read;
        if (p.StartsWith("/api/projection/scenarios", StringComparison.OrdinalIgnoreCase))
            return method == "GET" ? WorkspacePermissions.Read : WorkspacePermissions.RecordFinance;
        if (p.Equals("/api/savings/goals", StringComparison.OrdinalIgnoreCase) && method == "POST"
            || p.StartsWith("/api/savings/goals/", StringComparison.OrdinalIgnoreCase) && (method == "PUT" || method == "POST")
            || p.Equals("/api/accounts/liquidity", StringComparison.OrdinalIgnoreCase) && method == "PUT"
            || p.StartsWith("/api/accounts/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/liquidity", StringComparison.OrdinalIgnoreCase) && method == "PUT"
            || p.StartsWith("/api/savings/accounts/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/liquidity", StringComparison.OrdinalIgnoreCase) && method == "PUT")
            return WorkspacePermissions.ManageResources;
        if (p.Equals("/api/savings/allocations", StringComparison.OrdinalIgnoreCase) && method == "POST"
            || p.StartsWith("/api/savings/allocations/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/release", StringComparison.OrdinalIgnoreCase) && method == "POST"
            || p.Equals("/api/savings/applications", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return WorkspacePermissions.RecordFinance;
        if (p.StartsWith("/api/savings/applications/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/reverse", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return WorkspacePermissions.CorrectHistory;
        if (p.StartsWith("/api/operations/", StringComparison.OrdinalIgnoreCase) && p.Contains("/non-cash-settlements/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/reverse", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return WorkspacePermissions.CorrectHistory;
        if (p.StartsWith("/api/operations/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/non-cash-settlements", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return WorkspacePermissions.RecordFinance;
        if (method == "GET")
            return p.Contains("/groups", StringComparison.OrdinalIgnoreCase)
                || p.Contains("/receivables", StringComparison.OrdinalIgnoreCase)
                ? WorkspacePermissions.ReadGroups
                : WorkspacePermissions.Read;
        if (p.EndsWith("/adjustments", StringComparison.OrdinalIgnoreCase) || p.EndsWith("/void", StringComparison.OrdinalIgnoreCase))
            return WorkspacePermissions.CorrectHistory;
        if (p.Contains("/rules/", StringComparison.OrdinalIgnoreCase)
            || p == "/api/groups"
            || IsResourceOrSingleItemPath(p, "/api/accounts")
            || IsResourceOrSingleItemPath(p, "/api/cards"))
            return WorkspacePermissions.ManageResources;
        return WorkspacePermissions.RecordFinance;
    }

    private static bool IsResourceOrSingleItemPath(string path, string resource)
    {
        if (path.Equals(resource, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = resource + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && path.AsSpan(prefix.Length).IndexOf('/') < 0;
    }
}

public static class WorkspaceRequestContextItem
{
    public const string Key = "enigmabudget.workspace.request-context";
}
