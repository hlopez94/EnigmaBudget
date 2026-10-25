namespace EnigmaBudget.Api.Finance;

public sealed class FinanceCategory
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
    public int Version { get; set; } = 1;
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
