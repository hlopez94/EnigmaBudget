namespace EnigmaBudget.Api.Finance;

public sealed class FinanceCategory
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
    public bool IsDefault { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class CatalogCommand
{
    public Guid WorkspaceId { get; set; }
    public Guid RequestId { get; set; }
    public required string CommandKind { get; set; }
    public Guid TargetResourceId { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResultJson { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class FinanceMerchant
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class FinanceIncomeSource
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
    public int Version { get; set; } = 1;
}
