using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Finance;

public sealed record ReceivableProjectionRow(Guid Id, Guid SourceId, Guid GroupId, Guid PurchaseId,
    string Period, string Description, string Currency, Guid ParticipantId, string ParticipantName, bool IsSelf,
    decimal? Amount, decimal CollectedAmount, decimal? PendingAmount, int? RuleVersion, bool MonthConfirmed,
    bool IsEstimated, string Status);

public static class ReceivableProjectionQuery
{
    public static async Task<IReadOnlyList<ReceivableProjectionRow>> GetForPeriodAsync(AppDbContext db,
        Guid workspaceId, Guid groupId, string period, CancellationToken ct)
    {
        var installments = await db.Installments.AsNoTracking().Where(x =>
            x.WorkspaceId == workspaceId && x.GroupId == groupId && x.Period == period).OrderBy(x => x.Id).ToArrayAsync(ct);
        var people = await db.Participants.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.GroupId == groupId)
            .OrderBy(x => x.Id).ToArrayAsync(ct);
        if (installments.Length == 0 || people.Length == 0) return [];
        var rules = await db.MonthlyRules.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.GroupId == groupId
            && string.Compare(x.Period, period) <= 0).OrderBy(x => x.Period).ToArrayAsync(ct);
        var ruleByPeriod = rules.ToDictionary(x => x.Period);
        var weights = await db.RuleWeights.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && rules.Select(r => r.Id).Contains(x.RuleId)).ToArrayAsync(ct);
        var installmentIds = installments.Select(x => x.Id).ToArray();
        var collections = await db.Collections.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && installmentIds.Contains(x.InstallmentId)).ToArrayAsync(ct);
        var rows = new List<ReceivableProjectionRow>();
        foreach (var installment in installments)
        {
            var rule = ruleByPeriod.GetValueOrDefault(period);
            var estimated = false;
            if (rule is null)
            {
                rule = rules.Where(x => string.Compare(x.Period, period, StringComparison.Ordinal) < 0).OrderByDescending(x => x.Period).FirstOrDefault();
                estimated = rule is not null;
            }
            if (rule is null)
            {
                foreach (var person in people)
                    rows.Add(new(installment.Id, installment.Id, groupId, installment.PurchaseId, period,
                        installment.Description, installment.Currency, person.Id, person.Name, person.IsSelf,
                        null, 0m, null, null, false, false, "MissingRule"));
                continue;
            }
            var ruleWeights = weights.Where(x => x.RuleId == rule.Id).ToArray();
            var allocations = MoneyRules.Allocate(installment.Amount, people, ruleWeights, rule.Mode);
            foreach (var person in people)
            {
                var allocated = allocations[person.Id];
                var collected = collections.Where(x => x.InstallmentId == installment.Id && x.ParticipantId == person.Id).Sum(x => x.AppliedAmount);
                decimal? pending = person.IsSelf ? null : Math.Max(0m, allocated - collected);
                var status = person.IsSelf ? "OwnShare" : estimated ? "Estimated" : collected >= allocated ? "Collected" : collected > 0m ? "Partial" : "Pending";
                rows.Add(new(installment.Id, installment.Id, groupId, installment.PurchaseId, period,
                    installment.Description, installment.Currency, person.Id, person.Name, person.IsSelf,
                    allocated, collected, pending, rule.Version, ruleByPeriod.GetValueOrDefault(period)?.Confirmed ?? false,
                    estimated, status));
            }
        }
        return rows;
    }
}
