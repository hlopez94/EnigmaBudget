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
        var revisionRows = db.RecurringRuleRevisions.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.EffectiveFrom <= windowThrough);
        var revisions = await revisionRows
            .Where(x => x.EffectiveFrom >= state.WindowStart
                || !db.RecurringRuleRevisions.Any(previous => previous.WorkspaceId == workspaceId && previous.RuleId == x.RuleId
                    && previous.EffectiveFrom <= state.WindowStart && previous.EffectiveFrom > x.EffectiveFrom))
            .Where(x => !db.RecurringRuleRevisions.Any(newer => newer.WorkspaceId == workspaceId && newer.RuleId == x.RuleId
                && newer.EffectiveFrom == x.EffectiveFrom && newer.Version > x.Version))
            .OrderBy(x => x.RuleId).ThenBy(x => x.EffectiveFrom).ToListAsync(ct);
        var revisionGroups = revisions.GroupBy(x => x.RuleId).ToDictionary(x => x.Key, x => x.ToArray());
        var actions = await db.RecurringOccurrenceActions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ScheduledDate >= state.WindowStart && x.ScheduledDate <= windowThrough)
            .OrderBy(x => x.ScheduledDate).ThenBy(x => x.RuleId).ToListAsync(ct);
        var actionRevisionIds = actions.Select(x => x.RuleRevisionId).Except(revisions.Select(x => x.Id)).Distinct().ToArray();
        var actionRevisionHistory = actionRevisionIds.Length == 0 ? [] : await db.RecurringRuleRevisions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && actionRevisionIds.Contains(x.Id)).ToListAsync(ct);
        var revisionsById = revisions.Concat(actionRevisionHistory).ToDictionary(x => x.Id);
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
            revisionsById.TryGetValue(action.RuleRevisionId, out var revision);
            if (revision?.RuleId != action.RuleId) revision = null;
            if (rule is null || revision is null) continue;
            var status = action.Outcome;
            occurrences[(action.RuleId, action.ScheduledDate)] = ToDto(workspaceId, rule, revision, action.ScheduledDate, action, status);
        }

        var ordered = occurrences.Values.OrderBy(x => x.ScheduledDate, StringComparer.Ordinal).ThenBy(x => x.RuleId).ToArray();
        if (state.LastDate is not null)
            ordered = ordered.Where(x => StringComparer.Ordinal.Compare(x.ScheduledDate, state.LastDate) > 0
                || x.ScheduledDate == state.LastDate && x.RuleId.CompareTo(state.LastRuleId!.Value) > 0).ToArray();
        var items = ordered.Take(PageSize).ToArray();
        var linkedOperationIds = items.Where(x => x.FinanceOperationId.HasValue).Select(x => x.FinanceOperationId!.Value).Distinct().ToArray();
        if (linkedOperationIds.Length > 0)
        {
            var actuals = await db.FinanceOperations.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && linkedOperationIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Amount, x.Date, x.IsVoided }).ToDictionaryAsync(x => x.Id, ct);
            items = items.Select(item => item.FinanceOperationId is Guid operationId && actuals.TryGetValue(operationId, out var actual)
                ? item with { ActualAmount = actual.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    ActualDate = actual.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), OperationVoided = actual.IsVoided }
                : item).ToArray();
        }
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
        if (revision.Frequency == "Weekly") return distance % ((long)revision.Interval * 7L) == 0;
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
        var before = from.AddDays(-1);
        return await db.Database.SqlQuery<DateOnly?>($"""
            WITH RECURSIVE latest_revisions AS
            (
                SELECT r."WorkspaceId", r."RuleId", r."Id" AS "RevisionId", r."EffectiveFrom", r."State",
                       r."ScheduleAnchorDate", r."Frequency", r."Interval", r."EndDate"
                FROM "RecurringRuleRevisions" AS r
                WHERE r."WorkspaceId" = {workspaceId} AND r."EffectiveFrom" < {from}
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM "RecurringRuleRevisions" AS newer
                      WHERE newer."WorkspaceId" = r."WorkspaceId" AND newer."RuleId" = r."RuleId"
                        AND newer."EffectiveFrom" = r."EffectiveFrom" AND newer."Version" > r."Version"
                  )
            ), revision_segments AS
            (
                SELECT r.*,
                       LEAD(r."EffectiveFrom") OVER (PARTITION BY r."RuleId" ORDER BY r."EffectiveFrom") AS "NextEffectiveFrom"
                FROM latest_revisions AS r
            ), bounded_segments AS
            (
                SELECT s.*,
                       GREATEST(s."EffectiveFrom", s."ScheduleAnchorDate") AS "LowerDate",
                       LEAST({before}, COALESCE(s."EndDate", {before}), COALESCE(s."NextEffectiveFrom" - 1, {before})) AS "UpperDate"
                FROM revision_segments AS s
                WHERE s."State" = 'Active'
            ), indexed_candidates AS
            (
                SELECT b.*,
                       GREATEST(0::bigint,
                           CASE b."Frequency"
                               WHEN 'Weekly' THEN CEILING((b."LowerDate" - b."ScheduleAnchorDate")::numeric / (b."Interval"::numeric * 7))::bigint
                               WHEN 'Monthly' THEN CEILING((((EXTRACT(YEAR FROM b."LowerDate") - EXTRACT(YEAR FROM b."ScheduleAnchorDate")) * 12
                                   + EXTRACT(MONTH FROM b."LowerDate") - EXTRACT(MONTH FROM b."ScheduleAnchorDate")) / b."Interval")::numeric)::bigint
                               WHEN 'Annual' THEN CEILING((EXTRACT(YEAR FROM b."LowerDate") - EXTRACT(YEAR FROM b."ScheduleAnchorDate"))::numeric / b."Interval")::bigint
                               ELSE 0::bigint
                           END) AS "CandidateIndex"
                FROM bounded_segments AS b
                WHERE b."LowerDate" <= b."UpperDate"
            ), first_dates AS
            (
                SELECT c.*,
                       CASE c."Frequency"
                           WHEN 'Weekly' THEN CASE WHEN c."CandidateIndex" * c."Interval" * 7 <= c."UpperDate" - c."ScheduleAnchorDate"
                               THEN c."ScheduleAnchorDate" + (c."CandidateIndex" * c."Interval" * 7)::integer ELSE NULL::date END
                           WHEN 'Monthly' THEN CASE WHEN c."CandidateIndex" * c."Interval" <=
                               (EXTRACT(YEAR FROM c."UpperDate") - EXTRACT(YEAR FROM c."ScheduleAnchorDate")) * 12
                                   + EXTRACT(MONTH FROM c."UpperDate") - EXTRACT(MONTH FROM c."ScheduleAnchorDate")
                               THEN (c."ScheduleAnchorDate" + make_interval(months => (c."CandidateIndex" * c."Interval")::integer))::date ELSE NULL::date END
                           WHEN 'Annual' THEN CASE WHEN c."CandidateIndex" * c."Interval" <= EXTRACT(YEAR FROM c."UpperDate") - EXTRACT(YEAR FROM c."ScheduleAnchorDate")
                               THEN (c."ScheduleAnchorDate" + make_interval(years => (c."CandidateIndex" * c."Interval")::integer))::date ELSE NULL::date END
                           ELSE c."ScheduleAnchorDate"
                       END AS "CandidateDate"
                FROM indexed_candidates AS c
            ), adjusted_candidates AS
            (
                SELECT f.*,
                       f."CandidateIndex" + CASE WHEN f."CandidateDate" < f."LowerDate" THEN 1 ELSE 0 END AS "FirstIndex"
                FROM first_dates AS f
            ), safe_candidates AS
            (
                SELECT a.* FROM adjusted_candidates AS a
                WHERE (a."Frequency" = 'Weekly' AND (a."FirstIndex"::numeric * a."Interval" * 7) <= a."UpperDate" - a."ScheduleAnchorDate")
                   OR (a."Frequency" = 'Monthly' AND (a."FirstIndex"::numeric * a."Interval") <=
                       (EXTRACT(YEAR FROM a."UpperDate") - EXTRACT(YEAR FROM a."ScheduleAnchorDate")) * 12
                           + EXTRACT(MONTH FROM a."UpperDate") - EXTRACT(MONTH FROM a."ScheduleAnchorDate"))
                   OR (a."Frequency" = 'Annual' AND (a."FirstIndex"::numeric * a."Interval") <= EXTRACT(YEAR FROM a."UpperDate") - EXTRACT(YEAR FROM a."ScheduleAnchorDate"))
            ), occurrence_walk ("WorkspaceId", "RuleId", "Frequency", "Interval", "AnchorDate", "UpperDate", "OccurrenceIndex", "ScheduledDate") AS
            (
                SELECT a."WorkspaceId", a."RuleId", a."Frequency", a."Interval", a."ScheduleAnchorDate", a."UpperDate", a."FirstIndex",
                       CASE a."Frequency"
                           WHEN 'Weekly' THEN CASE WHEN (a."FirstIndex"::numeric * a."Interval" * 7) <= a."UpperDate" - a."ScheduleAnchorDate"
                               THEN a."ScheduleAnchorDate" + (a."FirstIndex" * a."Interval" * 7)::integer ELSE NULL::date END
                           WHEN 'Monthly' THEN CASE WHEN (a."FirstIndex"::numeric * a."Interval") <=
                               (EXTRACT(YEAR FROM a."UpperDate") - EXTRACT(YEAR FROM a."ScheduleAnchorDate")) * 12
                                   + EXTRACT(MONTH FROM a."UpperDate") - EXTRACT(MONTH FROM a."ScheduleAnchorDate")
                               THEN (a."ScheduleAnchorDate" + make_interval(months => (a."FirstIndex" * a."Interval")::integer))::date ELSE NULL::date END
                           WHEN 'Annual' THEN CASE WHEN (a."FirstIndex"::numeric * a."Interval") <= EXTRACT(YEAR FROM a."UpperDate") - EXTRACT(YEAR FROM a."ScheduleAnchorDate")
                               THEN (a."ScheduleAnchorDate" + make_interval(years => (a."FirstIndex" * a."Interval")::integer))::date ELSE NULL::date END
                           ELSE a."ScheduleAnchorDate"
                       END
                FROM safe_candidates AS a
                UNION ALL
                SELECT w."WorkspaceId", w."RuleId", w."Frequency", w."Interval", w."AnchorDate", w."UpperDate", w."OccurrenceIndex" + 1,
                       CASE w."Frequency"
                           WHEN 'Weekly' THEN CASE WHEN ((w."OccurrenceIndex" + 1)::numeric * w."Interval" * 7) <= w."UpperDate" - w."AnchorDate"
                               THEN w."AnchorDate" + ((w."OccurrenceIndex" + 1) * w."Interval" * 7)::integer ELSE NULL::date END
                           WHEN 'Monthly' THEN CASE WHEN ((w."OccurrenceIndex" + 1)::numeric * w."Interval") <=
                               (EXTRACT(YEAR FROM w."UpperDate") - EXTRACT(YEAR FROM w."AnchorDate")) * 12
                                   + EXTRACT(MONTH FROM w."UpperDate") - EXTRACT(MONTH FROM w."AnchorDate")
                               THEN (w."AnchorDate" + make_interval(months => ((w."OccurrenceIndex" + 1) * w."Interval")::integer))::date ELSE NULL::date END
                           WHEN 'Annual' THEN CASE WHEN ((w."OccurrenceIndex" + 1)::numeric * w."Interval") <= EXTRACT(YEAR FROM w."UpperDate") - EXTRACT(YEAR FROM w."AnchorDate")
                               THEN (w."AnchorDate" + make_interval(years => ((w."OccurrenceIndex" + 1) * w."Interval")::integer))::date ELSE NULL::date END
                           ELSE w."AnchorDate"
                       END
                FROM occurrence_walk AS w
                WHERE w."ScheduledDate" <= w."UpperDate"
                  AND EXISTS
                  (
                      SELECT 1 FROM "RecurringOccurrenceActions" AS action
                      WHERE action."WorkspaceId" = w."WorkspaceId" AND action."RuleId" = w."RuleId"
                        AND action."ScheduledDate" = w."ScheduledDate"
                  )
            )
            SELECT MIN(w."ScheduledDate")::date AS "Value"
            FROM occurrence_walk AS w
            WHERE w."ScheduledDate" <= w."UpperDate"
              AND NOT EXISTS
              (
                  SELECT 1 FROM "RecurringOccurrenceActions" AS action
                  WHERE action."WorkspaceId" = w."WorkspaceId" AND action."RuleId" = w."RuleId"
                    AND action."ScheduledDate" = w."ScheduledDate"
              )
            """).SingleOrDefaultAsync(ct);
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
