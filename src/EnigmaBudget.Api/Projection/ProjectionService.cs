using System.Globalization;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Projection;

public sealed class ProjectionService(AppDbContext db, BusinessClock clock, SavingsAccountBalanceQuery balances)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<(ProjectionDto? Result, int Status, string? Error)> GetAsync(WorkspaceRequestContext context,
        DateOnly through, string? scenario, Guid? scenarioId, CancellationToken ct)
    {
        var today = clock.Today;
        var selectedScenario = string.IsNullOrWhiteSpace(scenario) ? "Confirmed" : scenario;
        if (selectedScenario is not ("Confirmed" or "Estimated")) return (null, 400, "El escenario no es válido.");
        if (selectedScenario == "Estimated") return (null, 501, "El escenario estimado todavía no está disponible.");
        if (scenarioId is not null) return (null, 501, "La lectura de escenarios guardados todavía no está disponible.");
        if (through < today || through > today.AddMonths(36)) return (null, 400, "El horizonte debe estar entre hoy y los próximos 36 meses.");

        var ws = context.Workspace;
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var ledger = await db.LedgerEntries.AsNoTracking().Where(x => x.WorkspaceId == ws.Id && x.Date <= through)
            .Join(db.Accounts.AsNoTracking().Where(x => x.WorkspaceId == ws.Id), x => x.AccountId, a => a.Id,
                (x, a) => new { Entry = x, Account = a }).Where(x => x.Entry.Date >= x.Account.OpeningDate)
            .Select(x => x.Entry).ToArrayAsync(ct);
        var operations = await db.FinanceOperations.AsNoTracking()
            .Where(x => x.WorkspaceId == ws.Id && !x.IsVoided).ToArrayAsync(ct);
        var financePayments = await db.FinancePayments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id)
            .ToArrayAsync(ct);
        var cards = await db.Cards.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var installments = await db.Installments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var statements = await db.CardStatements.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var adjustments = await db.StatementAdjustments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var payments = await db.CardPayments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id && x.Date <= today).ToArrayAsync(ct);
        var accountBalances = await balances.GetAsync(ws.Id, ct);
        var rows = new List<ProjectionRowDto>();
        foreach (var account in accounts)
        {
            if (account.OpeningDate > today && account.OpeningDate <= through && account.OpeningBalance != 0m)
                rows.Add(Row("AccountOpening", account.Id, "OpeningBalance", account.OpeningDate, "Confirmed", account.Currency,
                    account.OpeningBalance, account.OpeningBalance >= 0m ? "Incoming" : "Outgoing", account.Id,
                    $"Saldo inicial: {account.Name}", null, []));
            if (account.FundsAvailableFrom is { } release && release > today && release <= through)
            {
                var amount = F((account.OpeningDate <= release ? account.OpeningBalance : 0m)
                    + ledger.Where(x => x.AccountId == account.Id && x.Date <= release).Sum(x => x.Amount));
                rows.Add(new("AccountLiquidity", account.Id, "FundsAvailableFrom", Date(release), "Confirmed", account.Currency,
                    amount, "Availability", "Confirmed", account.Id, $"Fondos disponibles: {account.Name}", null, []));
            }
        }
        foreach (var entry in ledger.Where(x => x.Date > today))
            rows.Add(Row("LedgerEntry", entry.Id, entry.Source, entry.Date, "Confirmed", entry.Currency, entry.Amount,
                entry.Amount >= 0m ? "Incoming" : "Outgoing", entry.AccountId, LedgerDescription(entry.Source), null, []));

        foreach (var operation in operations)
        {
            if (operation.Kind != "Expense" || operation.Date > through) continue;
            var remaining = Math.Max(0m, operation.Amount - financePayments.Where(x => x.OperationId == operation.Id).Sum(x => x.Amount));
            if (remaining <= 0m) continue;
            var warnings = operation.Date < today ? new[] { "Vencimiento atrasado; se evalúa desde hoy." } : Array.Empty<string>();
            rows.Add(new("FinanceOperation", operation.Id, null, Date(operation.Date),
                operation.Date < today ? "Overdue" : "Confirmed", operation.Currency, F(remaining), "Outgoing", "Confirmed",
                operation.AccountId, operation.Description, null, warnings));
        }

        foreach (var card in cards)
        {
            var input = new CardObligationInput(card, installments.Where(x => x.CardId == card.Id).ToArray(),
                statements.Where(x => x.CardId == card.Id).ToArray(),
                adjustments.Where(x => statements.Any(s => s.Id == x.StatementId && s.CardId == card.Id)).ToArray(),
                payments.Where(x => x.CardId == card.Id).ToArray());
            var built = CardObligationCalculator.Build(input, today);
            foreach (var component in built.Components.Where(x => x.RemainingAmount > 0m))
            {
                if (component.Period is null) continue;
                var statementConfirmed = component.SummaryId is not null;
                if (component.ComponentKind == "Installment" && component.Period.CompareTo(today.ToString("yyyy-MM", Inv)) > 0 && !statementConfirmed)
                    continue;
                if (component.DueDate is { } due && due > through) continue;
                if (component.DueDate is null && component.Period.CompareTo(through.ToString("yyyy-MM", Inv)) > 0) continue;
                var warnings = component.DueDate is null
                    ? new[] { "La obligación no tiene fecha de vencimiento confiable." }
                    : component.DueDate < today ? new[] { "Vencimiento atrasado; se evalúa desde hoy." } : Array.Empty<string>();
                rows.Add(new("CardObligation", component.SourceId, component.ComponentKind, NullableDate(component.DueDate),
                    component.DateCertainty, component.Currency, F(component.RemainingAmount), "Outgoing", "Confirmed", null,
                    component.Description, component.SummaryId?.ToString(), warnings));
            }
        }

        var currencyCodes = new[] { "ARS", "USD" };
        var output = new List<CurrencyProjectionDto>();
        var alerts = new List<ProjectionAlertDto>();
        foreach (var currency in currencyCodes)
        {
            var currencyAccounts = accounts.Where(x => x.Currency == currency).ToArray();
            var headers = accountBalances.Where(x => x.Currency == currency).ToArray();
            var cashHeader = headers.Sum(x => decimal.Parse(x.CashBalance, Inv));
            var availableHeader = headers.Sum(x => decimal.Parse(x.AvailableCash, Inv));
            var reserved = headers.Sum(x => decimal.Parse(x.TotalReserved, Inv));
            var deficit = headers.Sum(x => decimal.Parse(x.BackingDeficit, Inv));
            var daily = new List<ProjectionDailyBalanceDto>();
            decimal min = availableHeader - reserved;
            DateOnly? firstShortfall = min < 0m ? today : null;
            for (var date = today; date <= through; date = date.AddDays(1))
            {
                decimal cash = 0m, available = 0m;
                foreach (var account in currencyAccounts)
                {
                    var amount = account.OpeningDate <= date ? account.OpeningBalance : 0m;
                    amount += ledger.Where(x => x.AccountId == account.Id && x.Date <= date).Sum(x => x.Amount);
                    var dailyOperations = rows.Where(x => x.SourceKind == "FinanceOperation" && x.AccountId == account.Id && EventDate(x, today) <= date)
                        .Sum(x => -decimal.Parse(x.Amount, Inv));
                    amount += dailyOperations;
                    cash += amount;
                    var availableFrom = account.FundsAvailableFrom is { } from ? (from > account.OpeningDate ? from : account.OpeningDate) : account.OpeningDate;
                    if (availableFrom <= date) available += amount;
                }
                var cardDelta = rows.Where(x => x.SourceKind == "CardObligation" && x.Currency == currency && EventDate(x, today) <= date)
                    .Sum(x => -decimal.Parse(x.Amount, Inv));
                cash += cardDelta;
                available += cardDelta;
                var free = available - reserved;
                daily.Add(new(Date(date), F(cash), F(available), F(cash - available), F(reserved), F(free)));
                if (free < min) min = free;
                if (free < 0m && firstShortfall is null) firstShortfall = date;
            }
            output.Add(new(currency, F(cashHeader), F(availableHeader), F(cashHeader - availableHeader), F(reserved), F(deficit),
                F(availableHeader - reserved), F(min), NullableDate(firstShortfall), daily));
            foreach (var row in rows.Where(x => x.Currency == currency && x.Date is null))
                alerts.Add(new("UnknownDueDate", "Warning", currency, "La proyección está incompleta porque una obligación no tiene fecha confiable.", null, row.SourceId));
            if (firstShortfall is not null)
                alerts.Add(new("CashShortfall", "Warning", currency, "El efectivo libre proyectado queda por debajo de cero.", NullableDate(firstShortfall), null));
        }

        // Stable identity: all rows are keyed by their source and component, never by statement summaries.
        rows = rows.GroupBy(x => (x.SourceKind, x.SourceId, x.ComponentId)).Select(x => x.First())
            .OrderBy(x => x.Date ?? "9999-99-99", StringComparer.Ordinal).ThenBy(x => x.SourceKind, StringComparer.Ordinal)
            .ThenBy(x => x.SourceId).ThenBy(x => x.ComponentId, StringComparer.Ordinal).ToList();
        return (new(ws.Id.ToString(), ws.Name, Date(today), output, rows, alerts), 200, null);
    }

    private static DateOnly EventDate(ProjectionRowDto row, DateOnly today) =>
        row.Date is not null && DateOnly.TryParseExact(row.Date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var date)
            ? (date < today ? today : date)
            : DateOnly.MaxValue;

    private static ProjectionRowDto Row(string sourceKind, Guid sourceId, string? componentId, DateOnly date,
        string certainty, string currency, decimal amount, string direction, Guid? accountId, string description,
        string? summaryId, IReadOnlyList<string> warnings) =>
        new(sourceKind, sourceId, componentId, Date(date), "Confirmed", currency, F(Math.Abs(amount)), direction,
            certainty, accountId, description, summaryId, warnings);

    private static string LedgerDescription(string source) => source switch
    {
        "transfer-out" or "transfer-in" => "Transferencia entre cuentas",
        "operation-payment" => "Pago de movimiento",
        "card-payment" => "Pago de tarjeta",
        "opening-balance" => "Saldo inicial",
        _ => "Movimiento confirmado",
    };

    private static string F(decimal amount) => amount.ToString("0.00", Inv);
    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", Inv);
    private static string? NullableDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", Inv);
}

public static class ProjectionEndpoints
{
    public static void MapProjectionEndpoints(this RouteGroupBuilder api) =>
        api.MapGet("/projection", GetProjection).AddEndpointFilter<FinanceWorkspaceFilter>();

    private static async Task<IResult> GetProjection(HttpContext http, ProjectionService service,
        string? through = null, string? scenario = null, Guid? scenarioId = null)
    {
        if (!DateOnly.TryParseExact(through, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: "Indicá una fecha válida para el horizonte.");
        var context = FinanceWorkspaceFilter.Get(http);
        if (context is null) return Results.NotFound();
        var (result, status, error) = await service.GetAsync(context, date, scenario, scenarioId, http.RequestAborted);
        return result is not null ? Results.Ok(result) : Results.Problem(statusCode: status,
            title: "No se pudo completar la solicitud", detail: error);
    }
}
