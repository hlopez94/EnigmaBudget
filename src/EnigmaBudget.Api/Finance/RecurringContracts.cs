namespace EnigmaBudget.Api.Finance;

public sealed record HealthcareComponentInput(
    Guid Id,
    string Kind,
    string Amount,
    bool AppliesToProviderBalance,
    Guid? ParticipantId
);

public sealed record RecurringRuleDto(
    Guid Id,
    Guid WorkspaceId,
    int CurrentVersion,
    string State
);

public sealed record RecurringRuleRevisionDto(
    Guid Id,
    Guid WorkspaceId,
    Guid RuleId,
    int Version,
    DateOnly EffectiveFrom,
    string State,
    DateOnly ScheduleAnchorDate,
    string Flow,
    string Description,
    Guid? CategoryId,
    Guid? IncomeSourceId,
    Guid? MerchantId,
    string Amount,
    string AmountMode,
    string Currency,
    string Frequency,
    int Interval,
    DateOnly? EndDate,
    int? DueDay,
    Guid? PreferredAccountId,
    Guid? PreferredCardId
);

public sealed record RecurringCatalogOverride(Guid? CategoryId, Guid? MerchantId, Guid? IncomeSourceId);
