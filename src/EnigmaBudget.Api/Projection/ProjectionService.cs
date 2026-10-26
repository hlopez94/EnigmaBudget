using System.Globalization;
using System.Data;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.Workspaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EnigmaBudget.Api.Projection;

public sealed class ProjectionService(AppDbContext db, BusinessClock clock, SavingsAccountBalanceQuery balances, RecurringProjectionAdapter recurring)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<(ProjectionDto? Result, int Status, string? Error)> GetAsync(WorkspaceRequestContext context,
        DateOnly through, string? scenario, Guid? scenarioId, CancellationToken ct, bool includeRecurringEstimates = false) =>
        await InSnapshotAsync(() => GetCoreAsync(context, through, scenario, scenarioId, ct, includeRecurringEstimates), ct);

    private async Task<(ProjectionDto? Result, int Status, string? Error)> GetCoreAsync(WorkspaceRequestContext context,
        DateOnly through, string? scenario, Guid? scenarioId, CancellationToken ct, bool includeRecurringEstimates)
    {
        var today = clock.Today;
        var selectedScenario = string.IsNullOrWhiteSpace(scenario) ? "Confirmed" : scenario;
        if (selectedScenario != "Confirmed") return (null, 400, "Usá el cálculo para consultar el escenario estimado.");
        if (scenarioId is not null) return (null, 400, "Los escenarios guardados solo se consultan al calcular el escenario estimado.");
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
        var nonCashSettlements = await db.NonCashSettlements.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var cards = await db.Cards.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var installments = await db.Installments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var linkedPurchaseIds = await db.Purchases.AsNoTracking().Where(x => x.WorkspaceId == ws.Id && x.ExpenseOperationId != null)
            .Select(x => x.Id).ToArrayAsync(ct);
        var linkedInstallmentIds = installments.Where(x => linkedPurchaseIds.Contains(x.PurchaseId)).Select(x => x.Id).ToHashSet();
        var statements = await db.CardStatements.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var adjustments = await db.StatementAdjustments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id).ToArrayAsync(ct);
        var payments = await db.CardPayments.AsNoTracking().Where(x => x.WorkspaceId == ws.Id && x.Date <= today).ToArrayAsync(ct);
        var accountBalances = await balances.GetAsync(ws.Id, ct);
        var rows = new List<ProjectionRowDto>();
        IReadOnlyList<ProjectionAlertDto> recurringAlerts = [];
        if (includeRecurringEstimates)
        {
            var (estimatedRows, historyAlerts) = await recurring.GetRowsAndAlertsAsync(context, AddMonthsClamped(today, -36), through, ct);
            rows.AddRange(estimatedRows);
            recurringAlerts = historyAlerts;
        }
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
            if (operation.Kind is not ("Expense" or "Income") || operation.Date > through) continue;
            var calculation = OperationSettlementCalculator.Calculate(operation,
                financePayments.Where(x => x.OperationId == operation.Id).ToArray(),
                nonCashSettlements.Where(x => x.OperationId == operation.Id).ToArray());
            var remaining = calculation.PendingAmount;
            if (remaining <= 0m) continue;
            var warnings = operation.Date < today ? new[] { "Vencimiento atrasado; se evalúa desde hoy." } : Array.Empty<string>();
            rows.Add(new ProjectionRowDto("FinanceOperation", operation.Id, null, Date(operation.Date),
                operation.Date < today ? "Overdue" : "Confirmed", operation.Currency, F(remaining), "Outgoing", "Confirmed",
                operation.AccountId, operation.Description, null, warnings) with { Direction = operation.Kind == "Income" ? "Incoming" : "Outgoing" });
        }

        foreach (var card in cards)
        {
            var input = new CardObligationInput(card, installments.Where(x => x.CardId == card.Id).ToArray(),
                statements.Where(x => x.CardId == card.Id).ToArray(),
                adjustments.Where(x => statements.Any(s => s.Id == x.StatementId && s.CardId == card.Id)).ToArray(),
                payments.Where(x => x.CardId == card.Id).ToArray());
            var built = CardObligationCalculator.Build(input, today);
            foreach (var component in built.Components.Where(x => x.RemainingAmount != 0m))
            {
                if (component.Period is null) continue;
                var statementConfirmed = component.SummaryId is not null;
                if (component.ComponentKind == "Installment" && component.Period.CompareTo(today.ToString("yyyy-MM", Inv)) > 0
                    && !statementConfirmed && !linkedInstallmentIds.Contains(component.SourceId))
                    continue;
                if (component.DueDate is { } due && due > through) continue;
                if (component.DueDate is null && component.Period.CompareTo(through.ToString("yyyy-MM", Inv)) > 0) continue;
                if (component.RemainingAmount < 0m)
                {
                    rows.Add(new("CardCredit", component.SourceId, $"{component.ComponentKind}:{component.Currency}",
                        NullableDate(component.DueDate), component.DateCertainty, component.Currency,
                        F(-component.RemainingAmount), "Information", "Confirmed", null,
                        $"Crédito de tarjeta sin aplicar: {component.Description}", component.SummaryId?.ToString(),
                        component.DueDate is null
                            ? ["Este crédito no es efectivo y no aumenta el saldo bancario.", "El crédito no tiene una fecha de vencimiento confiable."]
                            : ["Este crédito no es efectivo y no aumenta el saldo bancario."]));
                    continue;
                }
                var warnings = component.DueDate is null
                    ? new[] { "La obligación no tiene fecha de vencimiento confiable." }
                    : component.DueDate < today ? new[] { "Vencimiento atrasado; se evalúa desde hoy." } : Array.Empty<string>();
                var componentId = component.ComponentKind == "OpeningDebt"
                    ? $"OpeningDebt:{component.Currency}"
                    : component.ComponentKind;
                rows.Add(new("CardObligation", component.SourceId, componentId, NullableDate(component.DueDate),
                    component.DateCertainty, component.Currency, F(component.RemainingAmount), "Outgoing", "Confirmed", null,
                    component.Description, component.SummaryId?.ToString(), warnings));
            }
        }

        var currencyCodes = new[] { "ARS", "USD" };
        var output = new List<CurrencyProjectionDto>();
        var alerts = recurringAlerts.ToList();
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
                    var dailyOperations = rows.Where(x => (x.SourceKind is "FinanceOperation" or "RecurringOccurrence") && x.AccountId == account.Id && EventDate(x, today) <= date)
                        .Sum(x => x.Direction == "Incoming" ? decimal.Parse(x.Amount, Inv) : -decimal.Parse(x.Amount, Inv));
                    amount += dailyOperations;
                    cash += amount;
                    var availableFrom = account.FundsAvailableFrom is { } from ? (from > account.OpeningDate ? from : account.OpeningDate) : account.OpeningDate;
                    if (availableFrom <= date) available += amount;
                }
                var unassignedOperations = rows.Where(x => (x.SourceKind is "FinanceOperation" or "RecurringOccurrence") && x.AccountId is null && x.Currency == currency && EventDate(x, today) <= date)
                    .Sum(x => x.Direction == "Incoming" ? decimal.Parse(x.Amount, Inv) : -decimal.Parse(x.Amount, Inv));
                cash += unassignedOperations;
                available += unassignedOperations;
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
            foreach (var row in rows.Where(x => (x.SourceKind is "CardObligation" or "CardCredit") && x.Currency == currency && x.Date is null))
                alerts.Add(new("UnknownDueDate", "Warning", currency, "La proyección está incompleta porque un componente de tarjeta no tiene fecha confiable.", null, row.SourceId));
            if (firstShortfall is not null)
                alerts.Add(new("CashShortfall", "Warning", currency, "El efectivo libre proyectado queda por debajo de cero.", NullableDate(firstShortfall), null));
        }

        // Stable identity: all rows are keyed by their source and component, never by statement summaries.
        rows = rows.GroupBy(x => (x.SourceKind, x.SourceId, x.ComponentId, x.Currency)).Select(x => x.First())
            .OrderBy(x => x.Date ?? "9999-99-99", StringComparer.Ordinal).ThenBy(x => x.SourceKind, StringComparer.Ordinal)
            .ThenBy(x => x.SourceId).ThenBy(x => x.ComponentId, StringComparer.Ordinal).ToList();
        return (new(ws.Id.ToString(), ws.Name, Date(today), output, rows, alerts), 200, null);
    }

    public async Task<(ProjectionDto? Result, int Status, string? Error)> CalculateAsync(WorkspaceRequestContext context,
        DateOnly through, IReadOnlyList<ProjectionAssumptionInput> requested, IReadOnlyList<ProjectionAssumptionInput>? saved,
        CancellationToken ct) => await InSnapshotAsync(() => CalculateCoreAsync(context, through, requested, saved, ct), ct);

    private async Task<(ProjectionDto? Result, int Status, string? Error)> CalculateCoreAsync(WorkspaceRequestContext context,
        DateOnly through, IReadOnlyList<ProjectionAssumptionInput> requested, IReadOnlyList<ProjectionAssumptionInput>? saved,
        CancellationToken ct)
    {
        if (through < clock.Today || through > clock.Today.AddMonths(36)) return (null, 400, "El horizonte debe estar entre hoy y los próximos 36 meses.");
        var inputs = saved is null ? requested : saved.Concat(requested).ToArray();
        if (requested.Count > 300 || inputs.Count > 300 || inputs.Select(x => x.Id).Distinct().Count() != inputs.Count) return (null, 400, "El escenario supera el máximo de supuestos permitido.");
        var baseResult = await GetAsync(context, through, "Confirmed", null, ct, includeRecurringEstimates: true);
        if (baseResult.Result is null) return baseResult;
        var original = baseResult.Result;
        var rows = original.Rows.ToList();
        var today = clock.Today;
        var changes = new List<(ProjectionRowDto? Original, ProjectionRowDto Updated)>();
        var cards = await db.Cards.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id).ToArrayAsync(ct);
        var cardInstallments = await db.Installments.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id).ToArrayAsync(ct);
        var cardStatements = await db.CardStatements.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id).ToArrayAsync(ct);
        var statementIds = cardStatements.Select(x => x.Id).ToArray();
        var cardAdjustments = await db.StatementAdjustments.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id && statementIds.Contains(x.StatementId)).ToArrayAsync(ct);
        var cardPayments = await db.CardPayments.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id).ToArrayAsync(ct);
        foreach (var card in cards)
        {
            var built = CardObligationCalculator.Build(new(card, cardInstallments.Where(x => x.CardId == card.Id).ToArray(),
                cardStatements.Where(x => x.CardId == card.Id).ToArray(),
                cardAdjustments.Where(x => cardStatements.Any(st => st.Id == x.StatementId && st.CardId == card.Id)).ToArray(),
                cardPayments.Where(x => x.CardId == card.Id).ToArray()), today);
            foreach (var component in built.Components.Where(x => x.ComponentKind == "Installment" && x.RemainingAmount > 0m
                && x.SummaryId is null && x.Period is not null && string.CompareOrdinal(x.Period, today.ToString("yyyy-MM", Inv)) > 0))
            {
                if (component.DueDate is { } due && due > through) continue;
                if (component.Period!.CompareTo(through.ToString("yyyy-MM", Inv)) > 0) continue;
                rows.Add(new("CardObligation", component.SourceId, "Installment", NullableDate(component.DueDate),
                    component.DateCertainty, component.Currency, F(component.RemainingAmount), "Outgoing", "Estimated", null,
                    component.Description, null, component.DueDate is null ? ["La cuota todavía no tiene fecha de vencimiento confiable."] : []));
            }
        }
        if (context.Permissions.Contains(WorkspacePermissions.ReadGroups))
        {
            var groups = await db.Groups.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id).OrderBy(x => x.Id).ToArrayAsync(ct);
            var throughPeriod = through.ToString("yyyy-MM", Inv);
            var installmentPeriods = await db.Installments.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id
                && groups.Select(g => g.Id).Contains(x.GroupId!.Value) && string.Compare(x.Period, throughPeriod) <= 0)
                .Select(x => new { x.GroupId, x.Period }).Distinct().ToArrayAsync(ct);
            foreach (var group in groups)
            foreach (var period in installmentPeriods.Where(x => x.GroupId == group.Id).Select(x => x.Period).Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                var receivables = await ReceivableProjectionQuery.GetForPeriodAsync(db, context.Workspace.Id, group.Id, period, ct);
                foreach (var receivable in receivables.Where(x => !x.IsSelf && x.PendingAmount is > 0m))
                {
                    var periodDate = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", Inv);
                    var endOfPeriod = new DateOnly(periodDate.Year, periodDate.Month, DateTime.DaysInMonth(periodDate.Year, periodDate.Month));
                    var row = new ProjectionRowDto("GroupReceivable", receivable.SourceId, $"Participant:{receivable.ParticipantId}", Date(endOfPeriod),
                        "Estimated", receivable.Currency, F(receivable.PendingAmount!.Value), "Incoming", "Estimated", null,
                        receivable.Description, null, receivable.IsEstimated ? ["El reparto usa la última regla disponible para este período."] : []);
                    rows.Add(row); changes.Add((null, row));
                }
            }
        }
        foreach (var input in inputs)
        {
            var validation = ValidateAssumption(input, today, through);
            if (validation is not null) return (null, 400, validation);
            if (input.Kind is "Income" or "Expense")
            {
                var amount = decimal.Parse(input.Amount!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Inv);
                var date = DateOnly.ParseExact(input.Date!, "yyyy-MM-dd", Inv);
                var row = new ProjectionRowDto(input.Kind, input.Id, null, Date(date), "Exact", input.Currency!, F(amount),
                    input.Kind == "Income" ? "Incoming" : "Outgoing", "Estimated", null, input.Description.Trim(), null, []);
                rows.Add(row); changes.Add((null, row));
                continue;
            }
            if (input.SourceId is null) return (null, 400, "Indicá el origen del movimiento que querés ajustar.");
            var matches = rows.Where(x => x.SourceId == input.SourceId && x.SourceKind == input.SourceKind
                && (input.ComponentId is null || x.ComponentId == input.ComponentId)).ToArray();
            // A source-less selector is permitted only when it selects one visible canonical row.
            if (input.SourceKind is null && matches.Length == 0)
                matches = rows.Where(x => x.SourceId == input.SourceId && (input.ComponentId is null || x.ComponentId == input.ComponentId)).ToArray();
            if (matches.Length != 1) return (null, 400, "El origen no existe o la selección no identifica una única fila disponible.");
            var existing = matches[0];
            if (existing.Certainty == "Information" || existing.Direction is "Information" or "Availability") return (null, 400, "No se puede ajustar un dato informativo o de disponibilidad que no representa un movimiento de efectivo.");
            if (existing.SourceKind == "GroupReceivable" && !context.Permissions.Contains(WorkspacePermissions.ReadGroups))
                return (null, 400, "El origen no está disponible para este espacio.");
            var dateOverride = input.Kind == "DateChange" ? DateOnly.ParseExact(input.Date!, "yyyy-MM-dd", Inv) :
                existing.Date is null ? (DateOnly?)null : DateOnly.ParseExact(existing.Date, "yyyy-MM-dd", Inv);
            var amountOverride = decimal.Parse(existing.Amount, Inv);
            if (input.Kind == "AmountChange")
            {
                var delta = decimal.Parse(input.Amount!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Inv);
                amountOverride += delta;
                if (amountOverride <= 0m) return (null, 400, "El ajuste debe dejar un importe mayor que cero.");
                if (existing.Currency != input.Currency) return (null, 400, "La moneda del ajuste debe coincidir con la fila proyectada.");
            }
            var updated = existing with { Date = dateOverride is null ? null : Date(dateOverride.Value), Amount = F(amountOverride),
                DateCertainty = input.Kind == "DateChange" ? "Estimated" : existing.DateCertainty,
                Certainty = "Estimated", Description = input.Description.Length > 0 ? input.Description.Trim() : existing.Description };
            rows.Remove(existing); rows.Add(updated);
            changes.RemoveAll(x => x.Original is null && x.Updated == existing);
            changes.Add((existing, updated));
        }

        var scenarioAccounts = await db.Accounts.AsNoTracking().Where(x => x.WorkspaceId == context.Workspace.Id).ToDictionaryAsync(x => x.Id, ct);
        var currencies = original.Currencies.Select(currency => RebuildCurrency(currency, changes, today, scenarioAccounts)).ToArray();
        var alerts = original.Alerts.Where(x => x.Code != "CashShortfall").ToList();
        foreach (var currency in currencies.Where(x => x.FirstShortfallDate is not null))
            alerts.Add(new("CashShortfall", "Warning", currency.Currency, "El efectivo libre proyectado queda por debajo de cero.", currency.FirstShortfallDate, null));
        // An undated credit is noncash, but its missing date still makes the projection incomplete.
        foreach (var row in rows.Where(x => (x.SourceKind is "CardCredit" or "CardObligation") && x.Date is null))
            alerts.Add(new("UnknownDueDate", "Warning", row.Currency, "La proyección está incompleta porque un componente de tarjeta no tiene fecha confiable.", null, row.SourceId));
        alerts = alerts.GroupBy(x => (x.Code, x.Currency, x.SourceId, x.Date)).Select(x => x.First()).ToList();
        var sorted = rows.GroupBy(x => (x.SourceKind, x.SourceId, x.ComponentId, x.Currency)).Select(x => x.First())
            .OrderBy(x => x.Date ?? "9999-99-99", StringComparer.Ordinal).ThenBy(x => x.SourceKind, StringComparer.Ordinal)
            .ThenBy(x => x.SourceId).ThenBy(x => x.ComponentId, StringComparer.Ordinal).ToArray();
        return (original with { Currencies = currencies, Rows = sorted, Alerts = alerts.ToArray() }, 200, null);
    }

    public async Task<IReadOnlyList<ProjectionRowDto>> GetScenarioSourceRowsAsync(WorkspaceRequestContext context, CancellationToken ct)
    {
        var through = clock.Today.AddMonths(36);
        var (result, _, _) = await CalculateAsync(context, through, [], null, ct);
        return result?.Rows ?? [];
    }

    private static string? ValidateAssumption(ProjectionAssumptionInput input, DateOnly today, DateOnly through)
    {
        if (input.Id == Guid.Empty || input.Description is null || input.Description.Trim().Length > 500) return "El supuesto no es válido.";
        if (input.Kind is "Income" or "Expense")
        {
            if (input.SourceId is not null || input.SourceKind is not null || input.ComponentId is not null || input.Currency is not ("ARS" or "USD")
                || !MoneyRules.Money(input.Amount, false, out var amount) || amount <= 0m || !TryDate(input.Date, today, through))
                return "Ingresos y gastos estimados necesitan fecha, moneda e importe positivo válidos y no admiten un origen.";
            return null;
        }
        if (input.Kind == "AmountChange")
        {
            if (input.SourceId is null || input.Currency is not ("ARS" or "USD") || !MoneyRules.Money(input.Amount, true, out var delta) || delta == 0m || input.Date is not null)
                return "El ajuste de importe necesita origen, moneda coincidente e importe decimal firmado.";
        }
        else if (input.Kind == "DateChange")
        {
            if (input.SourceId is null || input.Amount is not null || input.Currency is not null || !TryDate(input.Date, today, through))
                return "El cambio de fecha necesita un origen y una fecha válidos.";
        }
        else return "El tipo de supuesto no es válido.";
        if (input.SourceKind is { Length: > 64 } || input.ComponentId is { Length: > 160 }) return "La selección de origen no es válida.";
        return null;
    }

    private static bool TryDate(string? value, DateOnly today, DateOnly through) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var date) && date >= today && date <= through;

    private static CurrencyProjectionDto RebuildCurrency(CurrencyProjectionDto currency,
        IReadOnlyList<(ProjectionRowDto? Original, ProjectionRowDto Updated)> changes, DateOnly today,
        IReadOnlyDictionary<Guid, BankAccount> accounts)
    {
        var deltas = changes.Where(x => x.Updated.Currency == currency.Currency).ToArray();
        var daily = new List<ProjectionDailyBalanceDto>(currency.DailyBalances.Count);
        foreach (var day in currency.DailyBalances)
        {
            var date = DateOnly.ParseExact(day.Date, "yyyy-MM-dd", Inv);
            decimal cashDelta = 0m, availableDelta = 0m;
            foreach (var change in deltas)
            {
                var old = change.Original;
                var updated = change.Updated;
                var oldEffect = old is null ? 0m : Effect(old, date, today);
                var newEffect = Effect(updated, date, today);
                var appliesToCash = updated.SourceKind is "Income" or "Expense" or "GroupReceivable" or "FinanceOperation" or "RecurringOccurrence" or "LedgerEntry" or "AccountOpening" or "CardObligation";
                if (!appliesToCash) continue;
                var difference = newEffect - oldEffect;
                cashDelta += difference;
                if (updated.SourceKind is "Income" or "Expense" or "GroupReceivable" or "CardObligation") availableDelta += difference;
                else if ((updated.SourceKind is "FinanceOperation" or "RecurringOccurrence") && updated.AccountId is null) availableDelta += difference;
                else if (updated.AccountId is { } accountId && accounts.TryGetValue(accountId, out var account)
                    && account.OpeningDate <= date && (account.FundsAvailableFrom is null || account.FundsAvailableFrom <= date))
                    availableDelta += difference;
            }
            var cash = decimal.Parse(day.CashBalance, Inv) + cashDelta;
            var available = decimal.Parse(day.AvailableCash, Inv) + availableDelta;
            daily.Add(day with { CashBalance = F(cash), AvailableCash = F(available), RestrictedCash = F(cash - available),
                FreeBalance = F(available - decimal.Parse(day.ReservedNominal, Inv)) });
        }
        var minimum = daily.Min(x => decimal.Parse(x.FreeBalance, Inv));
        var shortfall = daily.FirstOrDefault(x => decimal.Parse(x.FreeBalance, Inv) < 0m)?.Date;
        return currency with { DailyBalances = daily, MinimumFreeBalance = F(minimum), FirstShortfallDate = shortfall };
    }

    private static decimal Effect(ProjectionRowDto row, DateOnly date, DateOnly today)
    {
        var eventDate = EventDate(row, today);
        if (eventDate > date || row.Direction == "Information" || row.Direction == "Availability") return 0m;
        var amount = decimal.Parse(row.Amount, Inv);
        return row.Direction == "Outgoing" ? -amount : amount;
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

    private async Task<(ProjectionDto? Result, int Status, string? Error)> InSnapshotAsync(
        Func<Task<(ProjectionDto? Result, int Status, string? Error)>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is { } callerTransaction)
        {
            var isolation = callerTransaction.GetDbTransaction().IsolationLevel;
            if (isolation is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable))
                return (null, 409, "La proyección requiere que la transacción existente use aislamiento RepeatableRead o Serializable.");
            return await action();
        }
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }

    private static DateOnly AddMonthsClamped(DateOnly date, int months)
    {
        try { return date.AddMonths(months); }
        catch (ArgumentOutOfRangeException) { return months < 0 ? DateOnly.MinValue : DateOnly.MaxValue; }
    }
}

public static class ProjectionEndpoints
{
    public static void MapProjectionEndpoints(this RouteGroupBuilder api)
    {
        var projection = api.MapGroup("").AddEndpointFilter<FinanceWorkspaceFilter>();
        projection.MapGet("/projection", GetProjection);
        projection.MapPost("/projection/calculate", CalculateProjection);
        projection.MapGet("/projection/scenarios", ListScenarios);
        projection.MapPost("/projection/scenarios", CreateScenario);
        projection.MapPut("/projection/scenarios/{id:guid}", UpdateScenario);
        projection.MapDelete("/projection/scenarios/{id:guid}", DeleteScenario);
    }

    private static async Task<IResult> ListScenarios(HttpContext http, ProjectionScenarioService scenarios) =>
        Results.Ok(await scenarios.ListAsync(FinanceWorkspaceFilter.Get(http)!, http.RequestAborted));

    private static async Task<IResult> CreateScenario(HttpContext http, Microsoft.AspNetCore.Antiforgery.IAntiforgery csrf,
        ProjectionScenarioService scenarios, [FromBody] ProjectionScenarioCreate request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.BadRequest();
        var context = FinanceWorkspaceFilter.Get(http);
        if (context is null) return Results.NotFound();
        var (result, status, error) = await scenarios.CreateAsync(context, http.User, request, http.RequestAborted);
        return result is not null ? Results.Ok(result) : Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: error);
    }

    private static async Task<IResult> UpdateScenario(HttpContext http, Guid id, Microsoft.AspNetCore.Antiforgery.IAntiforgery csrf,
        ProjectionScenarioService scenarios, [FromBody] ProjectionScenarioSave request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.BadRequest();
        var context = FinanceWorkspaceFilter.Get(http);
        if (context is null) return Results.NotFound();
        var (result, status, error) = await scenarios.UpdateAsync(context, http.User, id, request, http.RequestAborted);
        return result is not null ? Results.Ok(result) : Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: error);
    }

    private static async Task<IResult> DeleteScenario(HttpContext http, Guid id, Microsoft.AspNetCore.Antiforgery.IAntiforgery csrf,
        ProjectionScenarioService scenarios, [FromBody] ProjectionScenarioDelete request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.BadRequest();
        var context = FinanceWorkspaceFilter.Get(http);
        if (context is null) return Results.NotFound();
        var (success, status, error) = await scenarios.DeleteAsync(context, http.User, id, request, http.RequestAborted);
        return success ? Results.NoContent() : Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: error);
    }

    private static async Task<IResult> CalculateProjection(HttpContext http, ProjectionService service,
        Microsoft.AspNetCore.Antiforgery.IAntiforgery csrf, ProjectionScenarioService scenarios, [FromBody] ProjectionCalculateRequest request)
    {
        if (!await NativeAuthCsrf.ValidateRequestAsync(http, csrf)) return Results.BadRequest();
        if (request.Scenario != "Estimated" || !DateOnly.TryParseExact(request.Through, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var through))
            return Results.Problem(statusCode: 400, title: "No se pudo completar la solicitud", detail: "El escenario o la fecha no son válidos.");
        var context = FinanceWorkspaceFilter.Get(http);
        if (context is null) return Results.NotFound();
        IReadOnlyList<ProjectionAssumptionInput>? saved = null;
        if (request.SavedScenarioId is { } id)
        {
            saved = await scenarios.GetAssumptionsAsync(context, id, http.RequestAborted);
            if (saved is null) return Results.NotFound();
        }
        var (result, status, error) = await service.CalculateAsync(context, through, request.Assumptions ?? [], saved, http.RequestAborted);
        return result is not null ? Results.Ok(result) : Results.Problem(statusCode: status, title: "No se pudo completar la solicitud", detail: error);
    }

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
