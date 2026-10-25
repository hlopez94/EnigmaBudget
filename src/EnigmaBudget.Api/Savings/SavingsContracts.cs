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
    int Version
);

public sealed record SavingsAllocationDto(
    Guid Id,
    Guid GoalId,
    Guid AccountId,
    string Currency,
    string ReservedAmount,
    int Version
);
