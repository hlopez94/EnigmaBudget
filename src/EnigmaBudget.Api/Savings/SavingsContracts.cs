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

public sealed record SavingsAllocationRequest(Guid Id, Guid GoalId, Guid AccountId, string Amount);
public sealed record SavingsReleaseRequest(Guid Id, string Amount);
public sealed record SavingsApplicationRequest(Guid Id, Guid AllocationId, Guid FinancePaymentId, string Amount);
public sealed record SavingsApplicationReverse(Guid RequestId, string Reason, bool Confirm);
public sealed record SavingsLiquidityUpdate(Guid RequestId, int Version, string? FundsAvailableFrom);
public sealed record SavingsAccountBalanceDto(Guid AccountId, string Currency, string CashBalance, string AvailableCash,
    string RestrictedCash, string TotalReserved, string BackingDeficit, string FreeBalance, bool IsArchived, string? FundsAvailableFrom);
public sealed record SavingsGoalDetailsDto(SavingsGoalDto Goal, IReadOnlyList<SavingsAllocationDto> Allocations, IReadOnlyList<SavingsMovementDto> Movements,
    int HistoryPage, int HistoryPageSize, int TotalMovements, bool HasMore);
public sealed record SavingsMovementDto(Guid Id, Guid AllocationId, string Kind, string Currency, string Amount, Guid? FinancePaymentId,
    Guid? ReversesMovementId, string? Reason, Guid ActorUserId, string ActorDisplayName, string OccurredAt, bool IsReversed);
