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
