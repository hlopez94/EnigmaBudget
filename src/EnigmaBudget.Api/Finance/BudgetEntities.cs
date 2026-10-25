namespace EnigmaBudget.Api.Finance;

public sealed class MonthlyBudget
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Period { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Limit { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class BudgetCommand
{
    public Guid WorkspaceId { get; set; }
    public Guid RequestId { get; set; }
    public string CommandKind { get; set; } = "";
    public Guid TargetBudgetId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
