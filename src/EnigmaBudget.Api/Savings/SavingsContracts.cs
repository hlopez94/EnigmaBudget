namespace EnigmaBudget.Api.Savings;

public sealed record SavingsGoalCreate(
    Guid Id,
    string Name,
    string TargetCurrency,
    string TargetAmount,
    string? DesiredDate,
    string? MonthlyContribution,
    string? NextContributionDate
);

public sealed record SavingsGoalUpdate(
    Guid RequestId,
    int Version,
    string Name,
    string TargetCurrency,
    string TargetAmount,
    string? DesiredDate,
    string State,
    string? MonthlyContribution,
    string? NextContributionDate
);

public sealed record SavingsGoalDto(
    Guid Id,
    string Name,
    string TargetCurrency,
    string TargetAmount,
    string? DesiredDate,
    string State,
    string? MonthlyContribution,
    string? NextContributionDate,
    bool Reached,
    IReadOnlyList<BackingAmountDto> BackingAmounts,
    GoalRateChoiceDto? RateChoice,
    string? SavedAmount,
    string? RemainingAmount,
    string? RequiredMonthlyContribution,
    int? RequiredContributionCount,
    string? EstimatedCompletionDate,
    string EstimateStatus,
    string EstimateExplanation,
    int Version
);

public sealed record SavingsGoalClose(Guid RequestId, int Version, bool ConfirmRelease);
public sealed record BackingAmountDto(string Currency, string Amount);
public sealed record GoalRateChoiceDto(string Mode, Guid? SnapshotId, string? House, string? Side, string Rate, string? ProviderUpdatedAt, string? FetchedAt, string? ManualReason, bool Stale);

public sealed record SavingsAllocationDto(
    Guid Id,
    Guid GoalId,
    Guid AccountId,
    string Currency,
    string ReservedAmount,
    int Version
);
