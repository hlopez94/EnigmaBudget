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

public sealed record RecurringRuleCreate(Guid Id, string Flow, string Description, Guid? CategoryId, Guid? IncomeSourceId, Guid? MerchantId, string Amount, string AmountMode, string Currency, string Frequency, int Interval, string StartDate, string? EndDate, int? DueDay, Guid? PreferredAccountId, Guid? PreferredCardId, IReadOnlyList<HealthcareComponentInput> HealthcareComponents);
public sealed record RecurringRuleUpdate(Guid RequestId, int Version, string EffectiveFrom, string Description, Guid? CategoryId, Guid? IncomeSourceId, Guid? MerchantId, string Amount, string AmountMode, string Currency, string Frequency, int Interval, string ScheduleAnchorDate, string? EndDate, int? DueDay, Guid? PreferredAccountId, Guid? PreferredCardId, IReadOnlyList<HealthcareComponentInput> HealthcareComponents);
public sealed record RecurringRuleStateChange(Guid RequestId, int Version, string EffectiveFrom, string State);
public sealed record RecurringHealthcareComponentDto(Guid Id, Guid RuleRevisionId, string Kind, string Amount, bool AppliesToProviderBalance, Guid? ParticipantId, Guid? OccurrenceActionId);
public sealed record RecurringRuleViewDto(RecurringRuleDto Rule, string StateForToday, RecurringRuleRevisionDto? EffectiveRevision, IReadOnlyList<RecurringRuleRevisionDto> History, IReadOnlyList<RecurringHealthcareComponentDto> HealthcareComponents);
public sealed record RecurrenceOccurrenceDto(Guid OccurrenceId, Guid RuleId, int RuleVersion, string ScheduledDate, string? DueDate, Guid RevisionId, string Amount, string Currency, string Direction, string Status, Guid? FinanceOperationId, Guid? CardPurchaseId);
public sealed record RecurringOccurrencePageDto(IReadOnlyList<RecurrenceOccurrenceDto> Items, string? NextCursor, bool OlderOccurrencesIncomplete, string? EarliestOmittedScheduledDate);
