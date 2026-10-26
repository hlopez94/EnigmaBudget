namespace EnigmaBudget.Api.ExchangeRates;

public sealed class ExchangeRateSnapshot
{
    public Guid Id { get; set; }
    public required string Currency { get; set; }
    public required string House { get; set; }
    public required string Name { get; set; }
    public decimal? Buy { get; set; }
    public decimal? Sell { get; set; }
    public DateTimeOffset ProviderUpdatedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public sealed class SavingsGoalRateChoiceEntity
{
    public Guid WorkspaceId { get; set; }
    public Guid GoalId { get; set; }
    public required string Mode { get; set; }
    public Guid? SnapshotId { get; set; }
    public string? House { get; set; }
    public string? Side { get; set; }
    public decimal? ManualArsPerUsd { get; set; }
    public string? ManualReason { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class ExchangeRateRefreshCommand
{
    public Guid WorkspaceId { get; set; }
    public Guid RequestId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
