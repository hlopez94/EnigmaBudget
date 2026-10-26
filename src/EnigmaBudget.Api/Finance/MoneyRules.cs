using System.Globalization;
using System.Numerics;
using EnigmaBudget.Api.Data;

namespace EnigmaBudget.Api.Finance;

internal static class MoneyRules
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool IsPeriod(string p) =>
        p.Length == 7
        && p[4] == '-'
        && int.TryParse(p[..4], out var y)
        && int.TryParse(p[5..], out var m)
        && y >= 2000
        && m is >= 1 and <= 12;

    public static bool Money(string? s, bool signed, out decimal amount)
    {
        amount = 0;
        if (
            string.IsNullOrWhiteSpace(s)
            || s.Contains(',')
            || !decimal.TryParse(
                s,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                Invariant,
                out amount
            )
            || (!signed && amount < 0)
        )
            return false;
        return decimal.Round(amount, 2) == amount
            && amount <= 9999999999999999.99m
            && amount >= -9999999999999999.99m;
    }

    public static Dictionary<Guid, decimal> Allocate(
        decimal total,
        GroupParticipant[] participants,
        RuleWeight[] weights,
        string mode
    )
    {
        var cents = decimal.ToInt64(total * 100);
        var raw = participants
            .Select(p => new
            {
                p.Id,
                WeightCents = mode == "Equal"
                    ? 100L
                    : decimal.ToInt64(weights.Single(w => w.ParticipantId == p.Id).Value * 100),
            })
            .ToArray();
        var denominator = raw.Aggregate(BigInteger.Zero, (sum, x) => sum + x.WeightCents);
        var shares = raw.Select(x =>
            {
                var numerator = (BigInteger)cents * x.WeightCents;
                var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
                return new
                {
                    x.Id,
                    Cents = (long)quotient,
                    Remainder = remainder,
                };
            })
            .ToArray();
        var extra = cents - shares.Sum(x => x.Cents);
        var winners = shares
            .OrderByDescending(x => x.Remainder)
            .ThenBy(x => x.Id)
            .Take((int)extra)
            .Select(x => x.Id)
            .ToHashSet();
        return shares.ToDictionary(
            x => x.Id,
            x => (x.Cents + (winners.Contains(x.Id) ? 1 : 0)) / 100m
        );
    }
}
