namespace EnigmaBudget.Api.Onboarding;

public sealed record AccountInput(Guid Id, string Name, string BankName, string Currency, string OpeningBalance, string OpeningDate);
public sealed record CardInput(Guid Id, string Name, string BankName, int? ClosingDay, int? DueDay, string StatementDebtArs, string StatementDebtUsd, string AsOfDate);
public sealed record SaveDraftRequest(int Version, AccountInput[] Accounts, CardInput[] Cards);
public sealed record CompleteDraftRequest(int Version);
public sealed record OnboardingState(int Version, bool Completed, AccountInput[] Accounts, CardInput[] Cards);
