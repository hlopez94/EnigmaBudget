namespace EnigmaBudget.Api.Savings;

public sealed class SavingsGoal
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public required string TargetCurrency { get; set; }
    public decimal TargetAmount { get; set; }
    public DateOnly? DesiredDate { get; set; }
    public required string State { get; set; }
    public decimal? MonthlyContribution { get; set; }
    public DateOnly? NextContributionDate { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class SavingsAllocation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid GoalId { get; set; }
    public Guid AccountId { get; set; }
    public required string Currency { get; init; }
    public decimal ReservedAmount { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class SavingsMovement
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AllocationId { get; set; }
    public Guid CommandId { get; set; }
    public required string Kind { get; init; }
    public required string Currency { get; init; }
    public decimal Amount { get; init; }
    public Guid? FinancePaymentId { get; init; }
    public Guid? ReversesMovementId { get; init; }
    public string? Reason { get; init; }
    public Guid ActorUserId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public int Version { get; init; } = 1;
}

public sealed class SavingsCommand
{
    public Guid WorkspaceId { get; set; }
    public Guid RequestId { get; set; }
    public required string CommandKind { get; set; }
    public required string PayloadHash { get; set; }
    public Guid ResultResourceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class ProjectionScenario
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public Guid ActorUserId { get; set; }
    public bool IsDeleted { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class ProjectionAssumption
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ScenarioId { get; set; }
    public required string Kind { get; set; }
    public DateOnly? Date { get; set; }
    public string? Currency { get; set; }
    public decimal? Amount { get; set; }
    public required string Description { get; set; }
    public Guid? SourceId { get; set; }
    public int Version { get; set; } = 1;
}
