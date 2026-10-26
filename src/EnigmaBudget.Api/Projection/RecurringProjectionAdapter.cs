using System.Globalization;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Projection;

public sealed class RecurringProjectionAdapter(AppDbContext db, RecurringCalendar calendar, BusinessClock clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private sealed record RevisionLabel(Guid Id, string Description, Guid? PreferredAccountId);
    private sealed record AccountCurrency(Guid Id, string Currency);

    public async Task<IReadOnlyList<ProjectionRowDto>> GetRowsAsync(WorkspaceRequestContext context, DateOnly from,
        DateOnly through, CancellationToken ct) => (await GetRowsAndAlertsAsync(context, from, through, ct)).Rows;

    internal async Task<(IReadOnlyList<ProjectionRowDto> Rows, IReadOnlyList<ProjectionAlertDto> Alerts)> GetRowsAndAlertsAsync(
        WorkspaceRequestContext context, DateOnly from, DateOnly through, CancellationToken ct)
    {
        if (from > through) return ([], []);
        var items = new List<RecurrenceOccurrenceDto>();
        var olderDate = (DateOnly?)null;
        string? cursor = null;
        do
        {
            var (page, status, error) = await calendar.GetPageAsync(context.Workspace.Id,
                from.ToString("yyyy-MM-dd", Inv), through.ToString("yyyy-MM-dd", Inv), cursor, ct);
            if (page is null) throw new InvalidOperationException(error ?? $"No se pudo leer el calendario recurrente ({status}).");
            items.AddRange(page.Items.Where(x => x.Status == "Estimated"));
            if (page.OlderOccurrencesIncomplete && page.EarliestOmittedScheduledDate is { } omitted
                && DateOnly.TryParseExact(omitted, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var date)
                && (olderDate is null || date < olderDate))
                olderDate = date;
            cursor = page.NextCursor;
        } while (cursor is not null);

        var revisions = items.Select(x => x.RevisionId).Distinct().ToArray();
        var labels = revisions.Length == 0 ? new Dictionary<Guid, RevisionLabel>() : await db.RecurringRuleRevisions.AsNoTracking()
            .Where(x => x.WorkspaceId == context.Workspace.Id && revisions.Contains(x.Id))
            .Select(x => new RevisionLabel(x.Id, x.Description, x.PreferredAccountId)).ToDictionaryAsync(x => x.Id, ct);
        var preferredAccounts = labels.Values.Where(x => x.PreferredAccountId.HasValue).Select(x => x.PreferredAccountId!.Value).Distinct().ToArray();
        var accounts = preferredAccounts.Length == 0 ? new Dictionary<Guid, AccountCurrency>() : await db.Accounts.AsNoTracking()
            .Where(x => x.WorkspaceId == context.Workspace.Id && preferredAccounts.Contains(x.Id))
            .Select(x => new AccountCurrency(x.Id, x.Currency)).ToDictionaryAsync(x => x.Id, ct);

        var rows = new List<ProjectionRowDto>(items.Count);
        foreach (var item in items)
        {
            if (!labels.TryGetValue(item.RevisionId, out var revision)
                || !DateOnly.TryParseExact(item.ScheduledDate, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var scheduled)
                || !decimal.TryParse(item.Amount, NumberStyles.AllowDecimalPoint, Inv, out var amount)
                || amount <= 0m || item.Currency is not ("ARS" or "USD") || item.Direction is not ("Income" or "Expense"))
                continue;
            Guid? accountId = revision.PreferredAccountId is { } preferred && accounts.TryGetValue(preferred, out var account)
                && account.Currency == item.Currency ? preferred : null;
            var warnings = scheduled < clock.Today
                ? new[] { "Vencimiento atrasado; se evalúa desde hoy." }
                : Array.Empty<string>();
            rows.Add(new ProjectionRowDto("RecurringOccurrence", item.OccurrenceId, null, item.ScheduledDate,
                "Estimated", item.Currency, amount.ToString("0.00", Inv), item.Direction == "Income" ? "Incoming" : "Outgoing", "Estimated", accountId,
                $"Estimación recurrente: {revision.Description}", null, warnings));
        }

        IReadOnlyList<ProjectionAlertDto> alerts = olderDate is null ? [] : [
            new("RecurringHistoryIncomplete", "Warning", "ARS", "La proyección recurrente omite ocurrencias anteriores al rango de 36 meses.", olderDate.Value.ToString("yyyy-MM-dd", Inv), null),
            new("RecurringHistoryIncomplete", "Warning", "USD", "La proyección recurrente omite ocurrencias anteriores al rango de 36 meses.", olderDate.Value.ToString("yyyy-MM-dd", Inv), null),
        ];
        return (rows, alerts);
    }
}
