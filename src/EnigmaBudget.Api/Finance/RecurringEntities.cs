namespace EnigmaBudget.Api.Finance;

public sealed class RecurringRule
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public int CurrentVersion { get; set; } = 1;
    public string State { get; set; } = "Active";
}

public sealed class RecurringRuleRevision
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RuleId { get; set; }
    public int Version { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string State { get; set; } = "Active";
    public DateOnly ScheduleAnchorDate { get; set; }
    public string Flow { get; set; } = "Expense";
    public string Description { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public Guid? IncomeSourceId { get; set; }
    public Guid? MerchantId { get; set; }
    public decimal Amount { get; set; }
    public string AmountMode { get; set; } = "Fixed";
    public string Currency { get; set; } = "ARS";
    public string Frequency { get; set; } = "Monthly";
    public int Interval { get; set; } = 1;
    public DateOnly? EndDate { get; set; }
    public int? DueDay { get; set; }
    public Guid? PreferredAccountId { get; set; }
    public Guid? PreferredCardId { get; set; }
}

public sealed class RecurringOccurrenceAction
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RuleId { get; set; }
    public Guid RuleRevisionId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public string Outcome { get; set; } = "Recorded";
    public Guid? FinanceOperationId { get; set; }
    public Guid? CardPurchaseId { get; set; }
    public string? Reason { get; set; }
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
}

public sealed class HealthcareComponent
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RuleRevisionId { get; set; }
    public Guid? OccurrenceActionId { get; set; }
    public string Kind { get; set; } = "Other";
    public decimal Amount { get; set; }
    public bool AppliesToProviderBalance { get; set; }
    public Guid? ParticipantId { get; set; }
}

public sealed class NonCashSettlement
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid OperationId { get; set; }
    public string Currency { get; set; } = "ARS";
    public string Kind { get; set; } = "PayrollWithholding";
    public decimal Amount { get; set; }
    public Guid? HealthcareComponentId { get; set; }
    public Guid? CardPurchaseId { get; set; }
    public Guid? ReversesSettlementId { get; set; }
    public Guid? CauseVoidRequestId { get; set; }
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RecurringRuleCommand
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RequestId { get; set; }
    public string CommandKind { get; set; } = "";
    public Guid RuleId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
    public Guid ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
