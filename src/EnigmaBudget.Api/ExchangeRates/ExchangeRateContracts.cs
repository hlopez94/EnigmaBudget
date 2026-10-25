namespace EnigmaBudget.Api.ExchangeRates;

public sealed record ExchangeRateSnapshotDto(Guid Id, string Currency, string House, string Name, string? Buy, string? Sell, string ProviderUpdatedAt, string FetchedAt);
public sealed record ExchangeRateListDto(string Status, bool Stale, string? Error, string? FetchedAt, IReadOnlyList<ExchangeRateSnapshotDto> Snapshots);
public sealed record ExchangeRateRefreshRequest(Guid Id);
public sealed record SavingsGoalRateChoice(Guid RequestId, int GoalVersion, string Mode, Guid? SnapshotId, string? House, string? Side, string? ManualArsPerUsd, string? ManualReason);
