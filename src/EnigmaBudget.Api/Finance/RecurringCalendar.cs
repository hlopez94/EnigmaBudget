using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Finance;

/// <summary>Read-only, anchored occurrence resolution shared by calendar and later callers.</summary>
public sealed class RecurringCalendar(AppDbContext db)
{
    private const int PageSize = 200;
    private const int MaximumWindowMonths = 36;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(RecurringOccurrencePageDto? Page, int Status, string? Error)> GetPageAsync(Guid workspaceId, string fromText, string throughText, string? cursor, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(fromText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(throughText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var through)
            || from > through)
            return (null, 400, "Indicá un rango de fechas válido.");

        var state = new CursorState(workspaceId, from, through, from, null, null);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            try
            {
                var decoded = JsonSerializer.Deserialize<CursorState>(Convert.FromBase64String(cursor), JsonOptions);
                if (decoded is null || decoded.WorkspaceId != workspaceId || decoded.From != from || decoded.Through != through || decoded.WindowStart < from || decoded.WindowStart > through
                    || (decoded.LastDate is null) != (decoded.LastRuleId is null)
                    || decoded.LastRuleId == Guid.Empty
                    || decoded.LastDate is not null && (!DateOnly.TryParseExact(decoded.LastDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastDate) || lastDate < decoded.WindowStart || lastDate > AddMonthsClamped(decoded.WindowStart, MaximumWindowMonths, inclusiveEnd: true)))
                    return (null, 400, "El cursor no corresponde a esta consulta.");
                state = decoded;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                return (null, 400, "El cursor no es válido.");
            }
        }

        var windowThrough = AddMonthsClamped(state.WindowStart, MaximumWindowMonths, inclusiveEnd: true);
        if (windowThrough > through) windowThrough = through;
        var rules = await db.RecurringRules.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Id).ToListAsync(ct);
        var revisions = await db.RecurringRuleRevisions.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.RuleId).ThenBy(x => x.EffectiveFrom).ThenBy(x => x.Version).ToListAsync(ct);
        var revisionGroups = revisions.GroupBy(x => x.RuleId).ToDictionary(x => x.Key, x => x.ToArray());
        var actions = await db.RecurringOccurrenceActions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ScheduledDate >= state.WindowStart && x.ScheduledDate <= windowThrough)
            .OrderBy(x => x.ScheduledDate).ThenBy(x => x.RuleId).ToListAsync(ct);
        var actionMap = actions.ToDictionary(x => (x.RuleId, x.ScheduledDate));
        var occurrences = new Dictionary<(Guid RuleId, DateOnly Date), RecurrenceOccurrenceDto>();

        foreach (var rule in rules)
        {
            if (!revisionGroups.TryGetValue(rule.Id, out var ruleRevisions)) continue;
            var effective = ruleRevisions.GroupBy(x => x.EffectiveFrom).Select(x => x.OrderByDescending(y => y.Version).First()).OrderBy(x => x.EffectiveFrom).ToArray();
            for (var i = 0; i < effective.Length; i++)
            {
                var revision = effective[i];
                if (revision.State != "Active") continue;
                var rangeStart = Max(state.WindowStart, revision.EffectiveFrom);
                var rangeEnd = Min(windowThrough, revision.EndDate ?? DateOnly.MaxValue);
                if (i + 1 < effective.Length) rangeEnd = Min(rangeEnd, effective[i + 1].EffectiveFrom.AddDays(-1));
                if (rangeStart > rangeEnd || revision.ScheduleAnchorDate > rangeEnd) continue;
                foreach (var scheduled in Dates(revision, rangeStart, rangeEnd))
                {
                    if (actionMap.ContainsKey((rule.Id, scheduled))) continue;
                    occurrences[(rule.Id, scheduled)] = ToDto(workspaceId, rule, revision, scheduled, null);
                }
            }
        }

        foreach (var action in actions)
        {
            var rule = rules.SingleOrDefault(x => x.Id == action.RuleId);
            var revision = revisions.SingleOrDefault(x => x.Id == action.RuleRevisionId && x.RuleId == action.RuleId);
            if (rule is null || revision is null) continue;
            var status = action.Outcome;
            occurrences[(action.RuleId, action.ScheduledDate)] = ToDto(workspaceId, rule, revision, action.ScheduledDate, action, status);
        }

        var ordered = occurrences.Values.OrderBy(x => x.ScheduledDate, StringComparer.Ordinal).ThenBy(x => x.RuleId).ToArray();
        if (state.LastDate is not null)
            ordered = ordered.Where(x => StringComparer.Ordinal.Compare(x.ScheduledDate, state.LastDate) > 0
                || x.ScheduledDate == state.LastDate && x.RuleId.CompareTo(state.LastRuleId!.Value) > 0).ToArray();
        var items = ordered.Take(PageSize).ToArray();
        string? next = null;
        if (ordered.Length > PageSize)
        {
            var last = items[^1];
            next = Encode(state with { LastDate = last.ScheduledDate, LastRuleId = last.RuleId });
        }
        else if (windowThrough < through)
        {
            next = Encode(new CursorState(workspaceId, from, through, windowThrough.AddDays(1), null, null));
        }

        var earliestOmitted = await FindEarliestOmittedAsync(workspaceId, from, ct);
        return (new RecurringOccurrencePageDto(items, next, earliestOmitted is not null, earliestOmitted?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), 200, null);
    }

    /// <summary>Resolve the immutable revision applicable to a date without opening a gate or transaction.</summary>
    public async Task<RecurringRuleRevision?> ResolveRevisionAsync(Guid workspaceId, Guid ruleId, DateOnly scheduledDate, CancellationToken ct = default)
    {
        var revision = await db.RecurringRuleRevisions.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.RuleId == ruleId && x.EffectiveFrom <= scheduledDate)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        return revision is not null && revision.State == "Active" && revision.ScheduleAnchorDate <= scheduledDate
            && (revision.EndDate is null || scheduledDate <= revision.EndDate) && IsOccurrenceDate(revision, scheduledDate) ? revision : null;
    }

    public static bool IsOccurrenceDate(RecurringRuleRevision revision, DateOnly date)
    {
        if (date < revision.ScheduleAnchorDate || revision.Interval <= 0) return false;
        var distance = revision.Frequency switch
        {
            "Weekly" => date.DayNumber - revision.ScheduleAnchorDate.DayNumber,
            "Monthly" => (date.Year - revision.ScheduleAnchorDate.Year) * 12 + date.Month - revision.ScheduleAnchorDate.Month,
            "Annual" => date.Year - revision.ScheduleAnchorDate.Year,
            _ => -1
        };
        if (distance < 0) return false;
        if (revision.Frequency == "Weekly") return distance % (revision.Interval * 7) == 0;
        if (revision.Frequency == "Monthly")
        {
            if (distance % revision.Interval != 0) return false;
            return date.Day == Math.Min(revision.ScheduleAnchorDate.Day, DateTime.DaysInMonth(date.Year, date.Month));
        }
        if (revision.Frequency == "Annual")
            return distance % revision.Interval == 0 && date.Month == revision.ScheduleAnchorDate.Month
                && date.Day == Math.Min(revision.ScheduleAnchorDate.Day, DateTime.DaysInMonth(date.Year, date.Month));
        return false;
    }

    public static Guid StableOccurrenceId(Guid workspaceId, Guid ruleId, DateOnly scheduledDate)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{workspaceId:D}/{ruleId:D}/{scheduledDate:yyyy-MM-dd}"));
        Span<byte> id = stackalloc byte[16]; bytes.AsSpan(0, 16).CopyTo(id);
        id[6] = (byte)((id[6] & 0x0f) | 0x50); id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return new Guid(id);
    }

    private async Task<DateOnly?> FindEarliestOmittedAsync(Guid workspaceId, DateOnly from, CancellationToken ct)
    {
        if (from == DateOnly.MinValue) return null;
        var previous = from.AddDays(-1);
        var revisions = await db.RecurringRuleRevisions.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.EffectiveFrom < from)
            .OrderBy(x => x.RuleId).ThenBy(x => x.EffectiveFrom).ThenBy(x => x.Version).ToListAsync(ct);
        var actions = await db.RecurringOccurrenceActions.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.ScheduledDate < from)
            .Select(x => new { x.RuleId, x.ScheduledDate }).ToListAsync(ct);
        var handled = actions.Select(x => (x.RuleId, x.ScheduledDate)).ToHashSet();
        DateOnly? earliest = null;
        foreach (var group in revisions.GroupBy(x => x.RuleId))
        {
            var effective = group.GroupBy(x => x.EffectiveFrom).Select(x => x.OrderByDescending(y => y.Version).First()).OrderBy(x => x.EffectiveFrom).ToArray();
            for (var i = 0; i < effective.Length; i++)
            {
                var revision = effective[i];
                if (revision.State != "Active") continue;
                var start = Max(revision.EffectiveFrom, revision.ScheduleAnchorDate);
                var end = Min(previous, revision.EndDate ?? DateOnly.MaxValue);
                if (i + 1 < effective.Length) end = Min(end, effective[i + 1].EffectiveFrom.AddDays(-1));
                if (start > end) continue;
                foreach (var date in Dates(revision, start, end))
                {
                    if (handled.Contains((revision.RuleId, date))) continue;
                    if (earliest is null || date < earliest) earliest = date;
                    break;
                }
            }
        }
        return earliest;
    }

    private static IEnumerable<DateOnly> Dates(RecurringRuleRevision revision, DateOnly from, DateOnly through)
    {
        var anchor = revision.ScheduleAnchorDate;
        long index = revision.Frequency switch
        {
            "Weekly" => Math.Max(0, (from.DayNumber - anchor.DayNumber + revision.Interval * 7L - 1) / (revision.Interval * 7L)),
            "Monthly" => Math.Max(0, ((from.Year - anchor.Year) * 12L + from.Month - anchor.Month) / revision.Interval),
            "Annual" => Math.Max(0, (from.Year - anchor.Year) / revision.Interval),
            _ => long.MaxValue
        };
        while (index < int.MaxValue)
        {
            DateOnly date;
            try { date = OccurrenceDate(anchor, revision.Frequency, revision.Interval, index); }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException) { yield break; }
            if (date > through) yield break;
            if (date >= from && (revision.EndDate is null || date <= revision.EndDate)) yield return date;
            index++;
        }
    }

    private static DateOnly OccurrenceDate(DateOnly anchor, string frequency, int interval, long index)
    {
        var step = checked(index * interval);
        if (frequency == "Weekly") return anchor.AddDays(checked((int)(step * 7)));
        if (frequency == "Monthly")
        {
            var month = checked((long)anchor.Year * 12 + anchor.Month - 1 + step);
            if (month < 12 || month > (long)DateOnly.MaxValue.Year * 12 + 11) throw new ArgumentOutOfRangeException(nameof(index));
            var year = (int)(month / 12); var monthOfYear = (int)(month % 12) + 1;
            return new DateOnly(year, monthOfYear, Math.Min(anchor.Day, DateTime.DaysInMonth(year, monthOfYear)));
        }
        if (frequency == "Annual")
        {
            var year = checked(anchor.Year + (int)step);
            if (year > DateOnly.MaxValue.Year) throw new ArgumentOutOfRangeException(nameof(index));
            return new DateOnly(year, anchor.Month, Math.Min(anchor.Day, DateTime.DaysInMonth(year, anchor.Month)));
        }
        throw new ArgumentOutOfRangeException(nameof(frequency));
    }

    private static RecurrenceOccurrenceDto ToDto(Guid workspaceId, RecurringRule rule, RecurringRuleRevision revision, DateOnly date, RecurringOccurrenceAction? action, string? status = null)
    {
        DateOnly? due = revision.DueDay is int day ? new DateOnly(date.Year, date.Month, Math.Min(day, DateTime.DaysInMonth(date.Year, date.Month))) : null;
        return new RecurrenceOccurrenceDto(StableOccurrenceId(workspaceId, rule.Id, date), rule.Id, rule.CurrentVersion, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), revision.Id, revision.Amount.ToString("0.00", CultureInfo.InvariantCulture), revision.Currency, revision.Flow, status ?? "Estimated", action?.FinanceOperationId, action?.CardPurchaseId);
    }

    private static DateOnly AddMonthsClamped(DateOnly date, int months, bool inclusiveEnd)
    {
        try { return date.AddMonths(months).AddDays(-1); }
        catch (ArgumentOutOfRangeException) { return DateOnly.MaxValue; }
    }
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    private static string Encode(CursorState state) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
    private sealed record CursorState(Guid WorkspaceId, DateOnly From, DateOnly Through, DateOnly WindowStart, string? LastDate, Guid? LastRuleId);
}
