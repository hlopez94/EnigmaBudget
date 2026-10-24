using System.Globalization;
using System.Data;
using System.Security.Claims;
using EnigmaBudget.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Finance;

public static partial class FinanceEndpoints
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static void MapFinanceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/accounts", Accounts);
        api.MapGet("/finance", FinanceOverview);
        api.MapPost("/accounts", CreateAccount);
        api.MapPut("/accounts/{id:guid}", UpdateAccount);
        api.MapPost("/accounts/{id:guid}/adjustments", AdjustAccount);
        api.MapGet("/cards", Cards);
        api.MapPost("/operations", CreateOperation);
        api.MapGet("/operations", Operations);
        api.MapPost("/operations/{id:guid}/payments", PayOperation);
        api.MapPost("/operations/{id:guid}/void", VoidOperation);
        api.MapPost("/transfers", Transfer);
        api.MapPost("/cards", CreateCard);
        api.MapPut("/cards/{id:guid}", UpdateCard);
        api.MapPost("/groups", CreateGroup);
        api.MapGet("/groups", GetGroups);
        api.MapPut("/groups/{groupId:guid}/rules/{period}", PutRule);
        api.MapPost("/groups/{groupId:guid}/rules/{period}/confirm", ConfirmRule);
        api.MapPost("/purchases", CreatePurchase);
        api.MapGet("/groups/{groupId:guid}/receivables", GetReceivables);
        api.MapPost("/groups/{groupId:guid}/collections", Collect);
        api.MapGet("/purchases/{id:guid}", GetPurchase);
        api.MapGet("/cards/{cardId:guid}/purchases", GetPurchases);
        api.MapGet("/cards/{cardId:guid}/statement", GetStatement);
        api.MapPut("/cards/{cardId:guid}/statement", SaveStatement);
        api.MapPost("/cards/{cardId:guid}/statement/confirm", ConfirmStatement);
        api.MapPost("/cards/{cardId:guid}/payments", PayCard);
    }

    private static string Subject(HttpContext h)=>h.User.FindFirstValue(ClaimTypes.NameIdentifier)??"";
    private static async Task<Workspace> Workspace(AppDbContext db,string subject){var ws=await db.Workspaces.FirstOrDefaultAsync(x=>x.OwnerSubject==subject);if(ws is not null)return ws;ws=new Workspace{OwnerSubject=subject,Draft=new OnboardingDraft()};db.Workspaces.Add(ws);try{await db.SaveChangesAsync();return ws;}catch(DbUpdateException ex)when(ex.GetBaseException() is PostgresException {SqlState:PostgresErrorCodes.UniqueViolation}){db.ChangeTracker.Clear();return await db.Workspaces.SingleAsync(x=>x.OwnerSubject==subject);}}
    private static async Task<bool> Csrf(HttpContext h,IAntiforgery csrf){try{await csrf.ValidateRequestAsync(h);return true;}catch(AntiforgeryValidationException){return false;}}
    private static IResult Problem(int status,string detail)=>Results.Problem(statusCode:status,title:"No se pudo completar la solicitud",detail:detail);
    private static bool ValidName(string? x)=>!string.IsNullOrWhiteSpace(x)&&x.Trim().Length<=120;
    private static string F(decimal x)=>x.ToString("0.00",Invariant);
}
