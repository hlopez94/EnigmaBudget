using System.Data;
using System.Globalization;
using System.Security.Claims;
using EnigmaBudget.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnigmaBudget.Api.Finance;

public static partial class FinanceEndpoints
{
    private static async Task<IResult> CreateGroup(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        GroupCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !ValidName(r.Name)
            || r.Participants is not { Length: >= 2 and <= 50 }
            || r.Participants.Select(x => x.Id).Distinct().Count() != r.Participants.Length
            || r.Participants.Count(x => x.IsSelf) != 1
            || r.Participants.Any(x => !ValidName(x.Name))
        )
            return Problem(
                400,
                "El grupo necesita entre 2 y 50 participantes y exactamente una persona propia."
            );

        var ws = await Workspace(db, Subject(http));
        var existing = await db.Groups.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (existing is not null)
        {
            var participants = await db
                .Participants.Where(x => x.WorkspaceId == ws.Id && x.GroupId == r.Id)
                .ToArrayAsync();
            var same =
                existing.Name == r.Name.Trim()
                && participants.Length == r.Participants.Length
                && r.Participants.All(candidate =>
                    participants.Any(saved =>
                        saved.Id == candidate.Id
                        && saved.Name == candidate.Name.Trim()
                        && saved.IsSelf == candidate.IsSelf
                    )
                );
            return same
                ? Results.Ok(
                    new
                    {
                        id = existing.Id,
                        name = existing.Name,
                        version = existing.Version,
                    }
                )
                : Problem(409, "El identificador del grupo ya se usó con otros datos.");
        }

        var group = new SharedGroup
        {
            Id = r.Id,
            WorkspaceId = ws.Id,
            Name = r.Name.Trim(),
        };
        db.Groups.Add(group);
        db.Participants.AddRange(
            r.Participants.Select(x => new GroupParticipant
            {
                Id = x.Id,
                WorkspaceId = ws.Id,
                GroupId = r.Id,
                Name = x.Name.Trim(),
                IsSelf = x.IsSelf,
            })
        );
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Problem(409, "El identificador del grupo ya se usó.");
        }
        return Results.Ok(
            new
            {
                id = group.Id,
                name = group.Name,
                version = group.Version,
            }
        );
    }

    private static async Task<IResult> GetGroups(
        HttpContext http,
        AppDbContext db,
        BusinessClock clock,
        string? period = null
    )
    {
        var ws = await Workspace(db, Subject(http));
        period ??= clock.CurrentPeriod;
        var groups = await db.Groups.Where(x => x.WorkspaceId == ws.Id).ToArrayAsync();
        var result = new List<object>();
        foreach (var g in groups)
        {
            var parts = await db
                .Participants.Where(x => x.WorkspaceId == ws.Id && x.GroupId == g.Id)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    isSelf = x.IsSelf,
                })
                .ToArrayAsync();
            var rule = await RuleDto(db, ws.Id, g.Id, period);
            result.Add(
                new
                {
                    id = g.Id,
                    name = g.Name,
                    version = g.Version,
                    participants = parts,
                    rule,
                }
            );
        }
        return Results.Ok(result);
    }

    private static async Task<object?> RuleDto(AppDbContext db, Guid ws, Guid group, string period)
    {
        var rule = await db.MonthlyRules.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws && x.GroupId == group && x.Period == period
        );
        if (rule is null)
            return null;
        var weights = await db
            .RuleWeights.Where(x => x.WorkspaceId == ws && x.RuleId == rule.Id)
            .Select(x => new { id = x.ParticipantId, value = F(x.Value) })
            .ToArrayAsync();
        return new
        {
            period = rule.Period,
            version = rule.Version,
            mode = rule.Mode,
            basisCurrency = rule.BasisCurrency,
            weights,
            confirmed = rule.Confirmed,
        };
    }

    private static async Task<IResult> PutRule(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        Guid groupId,
        string period,
        RuleCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        var ws = await Workspace(db, Subject(http));
        if (
            !MoneyRules.IsPeriod(period)
            || !await db.Groups.AnyAsync(x => x.WorkspaceId == ws.Id && x.Id == groupId)
        )
            return Results.NotFound();
        var people = await db
            .Participants.Where(x => x.WorkspaceId == ws.Id && x.GroupId == groupId)
            .Select(x => x.Id)
            .ToArrayAsync();
        if (
            r.Mode is not ("Equal" or "Percentage" or "Income")
            || r.BasisCurrency is not ("ARS" or "USD")
            || r.Weights.Length != people.Length
            || r.Weights.Select(x => x.ParticipantId).Distinct().Count() != people.Length
            || !r.Weights.All(x =>
                people.Contains(x.ParticipantId)
                && MoneyRules.Money(x.Value, false, out var v)
                && v >= 0
            )
            || r.Mode == "Percentage"
                && r.Weights.Sum(x => decimal.Parse(x.Value, Invariant)) != 100
            || r.Mode == "Income" && r.Weights.Sum(x => decimal.Parse(x.Value, Invariant)) <= 0
        )
            return Problem(400, "Revisá el reparto y sus participantes.");
        var rule = await db.MonthlyRules.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.GroupId == groupId && x.Period == period
        );
        if (rule?.Confirmed == true)
            return Problem(409, "El reparto de este mes está confirmado.");
        if ((rule?.Version ?? 0) != r.Version)
            return Problem(409, "El reparto cambió. Actualizá la pantalla.");
        if (rule is null)
        {
            rule = new MonthlyRule
            {
                Id = Guid.NewGuid(),
                WorkspaceId = ws.Id,
                GroupId = groupId,
                Period = period,
                Version = 1,
            };
            db.MonthlyRules.Add(rule);
        }
        else
        {
            db.RuleWeights.RemoveRange(db.RuleWeights.Where(x => x.RuleId == rule.Id));
            rule.Version++;
            rule.MutationVersion++;
        }
        rule.Mode = r.Mode;
        rule.BasisCurrency = r.BasisCurrency;
        foreach (var w in r.Weights)
            db.RuleWeights.Add(
                new RuleWeight
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = ws.Id,
                    RuleId = rule.Id,
                    ParticipantId = w.ParticipantId,
                    Value = decimal.Parse(w.Value, Invariant),
                }
            );
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            return Problem(409, "El reparto cambió. Actualizá la pantalla.");
        }
        return Results.Ok(await RuleDto(db, ws.Id, groupId, period));
    }

    private static async Task<IResult> ConfirmRule(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        Guid groupId,
        string period,
        VersionRequest r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        var ws = await Workspace(db, Subject(http));
        var rule = await db.MonthlyRules.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.GroupId == groupId && x.Period == period
        );
        if (rule is null)
            return Results.NotFound();
        if (rule.Version != r.Version)
            return Problem(409, "El reparto cambió.");
        rule.Confirmed = true;
        rule.Version++;
        rule.MutationVersion++;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            return Problem(409, "El reparto cambió.");
        }
        return Results.Ok(await RuleDto(db, ws.Id, groupId, period));
    }

    private static async Task<IResult> GetReceivables(
        HttpContext http,
        AppDbContext db,
        Guid groupId,
        string? period = null
    )
    {
        var ws = await Workspace(db, Subject(http));
        if (!await db.Groups.AnyAsync(x => x.WorkspaceId == ws.Id && x.Id == groupId))
            return Results.NotFound();
        if (!MoneyRules.IsPeriod(period ?? ""))
            return Problem(400, "Período inválido.");
        var installments = await db
            .Installments.Where(x =>
                x.WorkspaceId == ws.Id && x.GroupId == groupId && x.Period == period
            )
            .ToArrayAsync();
        var people = await db
            .Participants.Where(x => x.WorkspaceId == ws.Id && x.GroupId == groupId)
            .ToArrayAsync();
        var rows = new List<object>();
        foreach (var installment in installments)
        {
            var rule = await db.MonthlyRules.SingleOrDefaultAsync(x =>
                x.WorkspaceId == ws.Id && x.GroupId == groupId && x.Period == period
            );
            var used = rule;
            var estimated = false;
            if (used is null)
            {
                used = await db
                    .MonthlyRules.Where(x =>
                        x.WorkspaceId == ws.Id
                        && x.GroupId == groupId
                        && string.Compare(x.Period, period) < 0
                    )
                    .OrderByDescending(x => x.Period)
                    .FirstOrDefaultAsync();
                estimated = used is not null;
            }
            if (used is null)
            {
                foreach (var person in people)
                    rows.Add(
                        new
                        {
                            id = installment.Id,
                            groupId,
                            sourceId = installment.Id,
                            purchaseId = installment.PurchaseId,
                            period,
                            description = installment.Description,
                            currency = installment.Currency,
                            participantId = person.Id,
                            participantName = person.Name,
                            isSelf = person.IsSelf,
                            amount = (string?)null,
                            collectedAmount = "0.00",
                            pendingAmount = (string?)null,
                            isEstimated = false,
                            ruleVersion = (int?)null,
                            monthConfirmed = false,
                            status = "MissingRule",
                        }
                    );
                continue;
            }
            var weights = await db
                .RuleWeights.Where(x => x.WorkspaceId == ws.Id && x.RuleId == used.Id)
                .ToArrayAsync();
            var allocations = MoneyRules.Allocate(installment.Amount, people, weights, used.Mode);
            var collections = await db
                .Collections.Where(x => x.WorkspaceId == ws.Id && x.InstallmentId == installment.Id)
                .ToArrayAsync();
            foreach (var person in people)
            {
                var amt = allocations[person.Id];
                var collected = collections
                    .Where(x => x.ParticipantId == person.Id)
                    .Sum(x => x.AppliedAmount);
                rows.Add(
                    new
                    {
                        id = installment.Id,
                        groupId,
                        sourceId = installment.Id,
                        purchaseId = installment.PurchaseId,
                        period,
                        description = installment.Description,
                        currency = installment.Currency,
                        participantId = person.Id,
                        participantName = person.Name,
                        isSelf = person.IsSelf,
                        amount = F(amt),
                        collectedAmount = F(collected),
                        pendingAmount = person.IsSelf
                            ? (string?)null
                            : F(Math.Max(0, amt - collected)),
                        isEstimated = estimated,
                        ruleVersion = used.Version,
                        monthConfirmed = rule?.Confirmed ?? false,
                        status = person.IsSelf ? "OwnShare"
                        : estimated ? "Estimated"
                        : collected >= amt ? "Collected"
                        : collected > 0 ? "Partial"
                        : "Pending",
                    }
                );
            }
        }
        return Results.Ok(rows);
    }

    private static async Task<IResult> Collect(
        HttpContext http,
        AppDbContext db,
        IAntiforgery csrf,
        BusinessClock clock,
        Guid groupId,
        CollectionCreate r
    )
    {
        if (!await Csrf(http, csrf))
            return Results.BadRequest();
        if (
            !MoneyRules.Money(r.Amount, false, out var bankAmount)
            || !MoneyRules.Money(r.AppliedAmount, false, out var applied)
            || bankAmount <= 0
            || applied <= 0
            || !DateOnly.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Invariant,
                DateTimeStyles.None,
                out var date
            )
            || date > clock.Today
        )
            return Problem(400, "Revisá los datos del cobro.");
        var ws = await Workspace(db, Subject(http));
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        var existing = await db.Collections.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.Id
        );
        if (existing is not null)
            return
                existing.AppliedAmount == applied
                && existing.Amount == bankAmount
                && existing.InstallmentId == r.InstallmentId
                && existing.ParticipantId == r.ParticipantId
                && existing.AccountId == r.AccountId
                && existing.Date == date
                && existing.RuleVersion == r.RuleVersion
                ? Results.Ok(new { id = existing.Id, appliedAmount = F(applied) })
                : Problem(409, "El identificador ya se usó.");
        var installment = await db.Installments.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.InstallmentId && x.GroupId == groupId
        );
        if (installment is null)
            return Results.NotFound();
        var rule = await db.MonthlyRules.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.GroupId == groupId && x.Period == installment.Period
        );
        if (rule is null)
            return Problem(409, "El mes todavía no tiene reparto definido.");
        if (rule.Version != r.RuleVersion)
            return Problem(409, "La regla cambió. Actualizá el reparto antes de cobrar.");
        var person = await db.Participants.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.GroupId == groupId && x.Id == r.ParticipantId
        );
        if (person is null)
            return Results.NotFound();
        if (person.IsSelf)
            return Problem(400, "No se puede cobrar a la persona propia.");
        var account = await db.Accounts.SingleOrDefaultAsync(x =>
            x.WorkspaceId == ws.Id && x.Id == r.AccountId
        );
        if (account is null)
            return Results.NotFound();
        if (account.IsArchived || account.Currency == installment.Currency && bankAmount != applied)
            return Problem(400, "Los importes de cobro no son compatibles.");
        var weights = await db
            .RuleWeights.Where(x => x.WorkspaceId == ws.Id && x.RuleId == rule.Id)
            .ToArrayAsync();
        var people = await db
            .Participants.Where(x => x.WorkspaceId == ws.Id && x.GroupId == groupId)
            .ToArrayAsync();
        var share = MoneyRules.Allocate(installment.Amount, people, weights, rule.Mode)[person.Id];
        var prior =
            await db
                .Collections.Where(x =>
                    x.WorkspaceId == ws.Id
                    && x.InstallmentId == installment.Id
                    && x.ParticipantId == person.Id
                )
                .SumAsync(x => (decimal?)x.AppliedAmount)
            ?? 0;
        if (applied > share - prior)
            return Problem(400, "El cobro supera el saldo pendiente de la persona.");
        if (date < account.OpeningDate)
            return Problem(400, "La fecha es anterior al alta de la cuenta.");
        rule.Confirmed = true;
        rule.MutationVersion++;
        db.Collections.Add(
            new SharedCollection
            {
                Id = r.Id,
                WorkspaceId = ws.Id,
                InstallmentId = installment.Id,
                ParticipantId = person.Id,
                AccountId = account.Id,
                Date = date,
                Amount = bankAmount,
                AppliedAmount = applied,
                RuleVersion = r.RuleVersion,
            }
        );
        db.LedgerEntries.Add(Entry(ws.Id, account, date, bankAmount, "shared-collection", r.Id));
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex) when (IsPersistenceConflict(ex))
        {
            await transaction.RollbackAsync();
            return Problem(409, "El cobro cambió durante la operación.");
        }
        return Results.Ok(
            new
            {
                id = r.Id,
                appliedAmount = F(applied),
                ruleVersion = rule.Version,
            }
        );
    }
}
