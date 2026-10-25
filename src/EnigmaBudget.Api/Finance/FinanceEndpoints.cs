using System.Data;
using System.Globalization;
using EnigmaBudget.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using EnigmaBudget.Api.Workspaces;
using EnigmaBudget.Api.Identity;

namespace EnigmaBudget.Api.Finance;

public static partial class FinanceEndpoints
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void MapFinanceEndpoints(this RouteGroupBuilder api)
    {
        var finance = api.MapGroup("").AddEndpointFilter<FinanceWorkspaceFilter>();
        finance.MapGet("/accounts", Accounts);
        finance.MapGet("/finance", FinanceOverview);
        finance.MapPost("/accounts", CreateAccount);
        finance.MapPut("/accounts/{id:guid}", UpdateAccount);
        finance.MapPost("/accounts/{id:guid}/adjustments", AdjustAccount);
        finance.MapGet("/cards", Cards);
        finance.MapPost("/operations", CreateOperation);
        finance.MapGet("/operations", Operations);
        finance.MapPost("/operations/{id:guid}/payments", PayOperation);
        finance.MapPost("/operations/{id:guid}/void", VoidOperation);
        finance.MapPost("/transfers", Transfer);
        finance.MapPost("/cards", CreateCard);
        finance.MapPut("/cards/{id:guid}", UpdateCard);
        finance.MapPost("/groups", CreateGroup);
        finance.MapGet("/groups", GetGroups);
        finance.MapPut("/groups/{groupId:guid}/rules/{period}", PutRule);
        finance.MapPost("/groups/{groupId:guid}/rules/{period}/confirm", ConfirmRule);
        finance.MapPost("/purchases", CreatePurchase);
        finance.MapGet("/groups/{groupId:guid}/receivables", GetReceivables);
        finance.MapPost("/groups/{groupId:guid}/collections", Collect);
        finance.MapGet("/purchases/{id:guid}", GetPurchase);
        finance.MapGet("/cards/{cardId:guid}/purchases", GetPurchases);
        finance.MapGet("/cards/{cardId:guid}/statement", GetStatement);
        finance.MapPut("/cards/{cardId:guid}/statement", SaveStatement);
        finance.MapPost("/cards/{cardId:guid}/statement/confirm", ConfirmStatement);
        finance.MapPost("/cards/{cardId:guid}/payments", PayCard);
    }

    private static Task<Workspace> Workspace(AppDbContext db, HttpContext http) =>
        Task.FromResult(FinanceWorkspaceFilter.Get(http)?.Workspace
            ?? throw new InvalidOperationException("Financial endpoint ran without a resolved workspace context."));

    private static Task<bool> Csrf(HttpContext h, IAntiforgery csrf) => NativeAuthCsrf.ValidateRequestAsync(h, csrf);

    private static IResult Problem(int status, string detail) =>
        Results.Problem(
            statusCode: status,
            title: "No se pudo completar la solicitud",
            detail: detail
        );

    private static bool IsPersistenceConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception.GetBaseException() is PostgresException postgres
            && postgres.SqlState is "40001" or "23505";

    private static bool ValidName(string? x) =>
        !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 120;

    private static string F(decimal x) => x.ToString("0.00", Invariant);
}
