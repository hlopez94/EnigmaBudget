namespace EnigmaBudget.Api.Projection;

public sealed record ProjectionDto(string WorkspaceId, string WorkspaceName, string AsOf,
    IReadOnlyList<CurrencyProjectionDto> Currencies, IReadOnlyList<ProjectionRowDto> Rows,
    IReadOnlyList<ProjectionAlertDto> Alerts);

public sealed record CurrencyProjectionDto(string Currency, string CashBalance, string AvailableCash,
    string RestrictedCash, string ReservedNominal, string BackingDeficit, string FreeBalance,
    string MinimumFreeBalance, string? FirstShortfallDate, IReadOnlyList<ProjectionDailyBalanceDto> DailyBalances);

public sealed record ProjectionDailyBalanceDto(string Date, string CashBalance, string AvailableCash,
    string RestrictedCash, string ReservedNominal, string FreeBalance);

public sealed record ProjectionRowDto(string SourceKind, Guid SourceId, string? ComponentId, string? Date,
    string DateCertainty, string Currency, string Amount, string Direction, string Certainty, Guid? AccountId,
    string Description, string? SummaryId, IReadOnlyList<string> Warnings);

public sealed record ProjectionAlertDto(string Code, string Severity, string Currency, string Message,
    string? Date, Guid? SourceId);

public sealed record ProjectionAssumptionInput(Guid Id, string Kind, string? Date, string? Currency,
    string? Amount, string Description, Guid? SourceId, string? SourceKind = null, string? ComponentId = null);
public sealed record ProjectionCalculateRequest(string Through, string Scenario, Guid? SavedScenarioId,
    IReadOnlyList<ProjectionAssumptionInput> Assumptions);
public sealed record ProjectionScenarioCreate(Guid Id, string Name, IReadOnlyList<ProjectionAssumptionInput> Assumptions);
public sealed record ProjectionScenarioSave(Guid RequestId, int Version, string Name, IReadOnlyList<ProjectionAssumptionInput> Assumptions);
public sealed record ProjectionScenarioDelete(Guid RequestId, int Version);
public sealed record ProjectionScenarioDto(Guid Id, string Name, int Version, IReadOnlyList<ProjectionAssumptionInput> Assumptions);
