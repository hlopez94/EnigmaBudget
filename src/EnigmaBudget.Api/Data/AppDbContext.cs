using Microsoft.EntityFrameworkCore;
using EnigmaBudget.Api.Workspaces;
using EnigmaBudget.Api.Identity;
using EnigmaBudget.Api.Savings;
using EnigmaBudget.Api.ExchangeRates;
using EnigmaBudget.Api.Finance;

namespace EnigmaBudget.Api.Data;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OwnerSubject { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public Guid OwnerUserId { get; set; }
    public ApplicationUser OwnerUser { get; set; } = null!;
    public bool IsArchived { get; set; }
    public int Version { get; set; } = 1;
    public OnboardingDraft? Draft { get; set; }
    public List<BankAccount> Accounts { get; set; } = [];
    public List<PaymentCard> Cards { get; set; } = [];
    public List<WorkspaceMembership> Memberships { get; set; } = [];
}

public sealed class OnboardingDraft
{
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public int Version { get; set; }
    public bool Completed { get; set; }
    public string AccountsJson { get; set; } = "[]";
    public string CardsJson { get; set; } = "[]";
}

public sealed class BankAccount
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public string Name { get; set; } = "";
    public string BankName { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal OpeningBalance { get; set; }
    public DateOnly OpeningDate { get; set; }
    public DateOnly? FundsAvailableFrom { get; set; }
    public bool IsArchived { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class PaymentCard
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public string Name { get; set; } = "";
    public string BankName { get; set; } = "";
    public int? ClosingDay { get; set; }
    public int? DueDay { get; set; }
    public decimal StatementDebtArs { get; set; }
    public decimal StatementDebtUsd { get; set; }
    public DateOnly AsOfDate { get; set; }
    public bool IsArchived { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class OpeningBalanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid AccountId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

public sealed class FinanceOperation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Kind { get; set; } = "";
    public Guid? AccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public Guid? MerchantId { get; set; }
    public Guid? IncomeSourceId { get; set; }
    public string? MerchantNameSnapshot { get; set; }
    public string? IncomeSourceNameSnapshot { get; set; }
    public string? CatalogSelectionHash { get; set; }
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsVoided { get; set; }
    public int Version { get; set; } = 1;
    public bool InitiallyPaid { get; set; }
    public Guid? VoidRequestId { get; set; }
    public string? VoidReason { get; set; }
    public List<FinancePayment> Payments { get; set; } = [];
}

public sealed class FinancePayment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid OperationId { get; set; }
    public Guid? LegacyFinanceOperationId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string? CommandPayloadHash { get; set; }
}

public sealed class LedgerEntry
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public string Source { get; set; } = "";
    public Guid SourceId { get; set; }
    public decimal? ObservedBalance { get; set; }
    public string? Reason { get; set; }
}

public sealed class TransferRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public decimal DestinationAmount { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<WorkspaceAuditEvent> WorkspaceAuditEvents => Set<WorkspaceAuditEvent>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();
    public DbSet<PersonalReadGrant> PersonalReadGrants => Set<PersonalReadGrant>();
    public DbSet<PersonalReadGrantAudience> PersonalReadGrantAudience => Set<PersonalReadGrantAudience>();
    public DbSet<OnboardingDraft> Drafts => Set<OnboardingDraft>();
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<PaymentCard> Cards => Set<PaymentCard>();
    public DbSet<OpeningBalanceRecord> OpeningBalances => Set<OpeningBalanceRecord>();
    public DbSet<FinanceOperation> FinanceOperations => Set<FinanceOperation>();
    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();
    public DbSet<RecurringRuleRevision> RecurringRuleRevisions => Set<RecurringRuleRevision>();
    public DbSet<RecurringOccurrenceAction> RecurringOccurrenceActions => Set<RecurringOccurrenceAction>();
    public DbSet<HealthcareComponent> HealthcareComponents => Set<HealthcareComponent>();
    public DbSet<NonCashSettlement> NonCashSettlements => Set<NonCashSettlement>();
    public DbSet<RecurringRuleCommand> RecurringRuleCommands => Set<RecurringRuleCommand>();
    public DbSet<FinanceCategory> FinanceCategories => Set<FinanceCategory>();
    public DbSet<FinanceMerchant> FinanceMerchants => Set<FinanceMerchant>();
    public DbSet<FinanceIncomeSource> FinanceIncomeSources => Set<FinanceIncomeSource>();
    public DbSet<CatalogCommand> CatalogCommands => Set<CatalogCommand>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<BudgetCommand> BudgetCommands => Set<BudgetCommand>();
    public DbSet<FinancePayment> FinancePayments => Set<FinancePayment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<TransferRecord> Transfers => Set<TransferRecord>();
    public DbSet<SharedGroup> Groups => Set<SharedGroup>();
    public DbSet<GroupParticipant> Participants => Set<GroupParticipant>();
    public DbSet<MonthlyRule> MonthlyRules => Set<MonthlyRule>();
    public DbSet<RuleWeight> RuleWeights => Set<RuleWeight>();
    public DbSet<CardPurchase> Purchases => Set<CardPurchase>();
    public DbSet<CardInstallment> Installments => Set<CardInstallment>();
    public DbSet<SharedCollection> Collections => Set<SharedCollection>();
    public DbSet<CardStatement> CardStatements => Set<CardStatement>();
    public DbSet<StatementAdjustment> StatementAdjustments => Set<StatementAdjustment>();
    public DbSet<CardPaymentRecord> CardPayments => Set<CardPaymentRecord>();
    public DbSet<NativeAuthChallenge> NativeAuthChallenges => Set<NativeAuthChallenge>();
    public DbSet<NativeSession> NativeSessions => Set<NativeSession>();
    public DbSet<NativeRefreshToken> NativeRefreshTokens => Set<NativeRefreshToken>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<SavingsAllocation> SavingsAllocations => Set<SavingsAllocation>();
    public DbSet<SavingsMovement> SavingsMovements => Set<SavingsMovement>();
    public DbSet<SavingsCommand> SavingsCommands => Set<SavingsCommand>();
    public DbSet<ProjectionScenario> ProjectionScenarios => Set<ProjectionScenario>();
    public DbSet<ProjectionAssumption> ProjectionAssumptions => Set<ProjectionAssumption>();
    public DbSet<ExchangeRateSnapshot> ExchangeRateSnapshots => Set<ExchangeRateSnapshot>();
    public DbSet<SavingsGoalRateChoiceEntity> SavingsGoalRateChoices => Set<SavingsGoalRateChoiceEntity>();
    public DbSet<ExchangeRateRefreshCommand> ExchangeRateRefreshCommands => Set<ExchangeRateRefreshCommand>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Workspace>().HasIndex(x => x.OwnerSubject).IsUnique().HasFilter("\"OwnerSubject\" IS NOT NULL");
        model.Entity<Workspace>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<Workspace>().Property(x => x.Kind).HasMaxLength(20);
        model.Entity<Workspace>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Workspace>().ToTable(table => table.HasCheckConstraint(
            "CK_Workspaces_KindAndOwnerSubject",
            "\"Kind\" IN ('Personal','Household') AND ((\"Kind\" = 'Personal' AND \"OwnerSubject\" IS NOT NULL) OR (\"Kind\" <> 'Personal' AND \"OwnerSubject\" IS NULL))"));
        model.Entity<ApplicationUser>().HasIndex(x => x.Subject).IsUnique();
        model.Entity<ApplicationUser>().Property(x => x.Subject).HasMaxLength(512);
        model.Entity<ApplicationUser>().Property(x => x.DisplayName).HasMaxLength(256);
        model.Entity<ApplicationUser>().Property(x => x.Email).HasMaxLength(320);
        model.Entity<NativeAuthChallenge>().Property(x => x.NonceDigest).HasMaxLength(32);
        model.Entity<NativeAuthChallenge>().Property(x => x.Platform).HasMaxLength(20);
        model.Entity<NativeSession>().Property(x => x.Subject).HasMaxLength(512);
        model.Entity<NativeSession>().Property(x => x.DisplayName).HasMaxLength(256);
        model.Entity<NativeSession>().Property(x => x.DeviceName).HasMaxLength(120);
        model.Entity<NativeSession>().Property(x => x.AccessTokenDigest).HasMaxLength(32);
        model.Entity<NativeSession>().HasIndex(x => x.AccessTokenDigest).IsUnique();
        model.Entity<NativeSession>().HasIndex(x => new { x.UserId, x.RevokedAt });
        model.Entity<NativeRefreshToken>().Property(x => x.TokenDigest).HasMaxLength(32);
        model.Entity<NativeRefreshToken>().HasIndex(x => x.TokenDigest).IsUnique();
        model.Entity<NativeRefreshToken>().HasIndex(x => x.SessionId);
        model.Entity<NativeRefreshToken>().HasOne(x => x.Session).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<WorkspaceMembership>().HasKey(x => new { x.WorkspaceId, x.UserId });
        model.Entity<WorkspaceMembership>().HasIndex(x => new { x.UserId, x.Active });
        model.Entity<WorkspaceMembership>().Property(x => x.Role).HasMaxLength(20);
        model.Entity<WorkspaceMembership>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<WorkspaceMembership>().Property(x => x.ActivationGeneration).ValueGeneratedNever();
        model.Entity<WorkspaceInvitation>().Property(x => x.Email).HasMaxLength(320);
        model.Entity<WorkspaceInvitation>().Property(x => x.Role).HasMaxLength(20);
        model.Entity<WorkspaceInvitation>().Property(x => x.Status).HasMaxLength(20);
        model.Entity<WorkspaceInvitation>().Property(x => x.TokenHash).HasMaxLength(32);
        model.Entity<WorkspaceInvitation>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<WorkspaceInvitation>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model.Entity<WorkspaceInvitation>().HasIndex(x => x.IssuedForUserId);
        model.Entity<WorkspaceInvitation>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<WorkspaceInvitation>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<WorkspaceInvitation>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AcceptedByUserId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<PersonalReadGrant>().HasKey(x => new { x.PersonalWorkspaceId, x.HouseholdWorkspaceId });
        model.Entity<PersonalReadGrant>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<PersonalReadGrant>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.PersonalWorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PersonalReadGrant>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.HouseholdWorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PersonalReadGrantAudience>().HasKey(x => new { x.PersonalWorkspaceId, x.HouseholdWorkspaceId, x.UserId });
        model.Entity<PersonalReadGrantAudience>().HasOne<PersonalReadGrant>().WithMany(x => x.Audience)
            .HasForeignKey(x => new { x.PersonalWorkspaceId, x.HouseholdWorkspaceId }).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PersonalReadGrantAudience>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<WorkspaceAuditEvent>().Property(x => x.Action).HasMaxLength(80);
        model.Entity<WorkspaceAuditEvent>().Property(x => x.ResourceType).HasMaxLength(80);
        model.Entity<WorkspaceAuditEvent>().Property(x => x.ResourceId).HasMaxLength(80);
        model.Entity<WorkspaceAuditEvent>().Property(x => x.ChangesJson).HasColumnType("jsonb");
        model.Entity<WorkspaceAuditEvent>().HasIndex(x => new { x.WorkspaceId, x.OccurredAt });
        model.Entity<Workspace>().HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<WorkspaceMembership>().HasOne(x => x.Workspace).WithMany(x => x.Memberships).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<WorkspaceMembership>().HasOne(x => x.User).WithMany(x => x.Memberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<WorkspaceAuditEvent>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<WorkspaceAuditEvent>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<OnboardingDraft>().HasKey(x => x.WorkspaceId);
        model.Entity<OnboardingDraft>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<BankAccount>().Property(x => x.OpeningBalance).HasPrecision(18, 2);
        model.Entity<BankAccount>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<BankAccount>().Property(x => x.BankName).HasMaxLength(120);
        model.Entity<BankAccount>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<BankAccount>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<PaymentCard>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<PaymentCard>().Property(x => x.BankName).HasMaxLength(120);
        model.Entity<PaymentCard>().Property(x => x.StatementDebtArs).HasPrecision(18, 2);
        model.Entity<PaymentCard>().Property(x => x.StatementDebtUsd).HasPrecision(18, 2);
        model.Entity<PaymentCard>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<OpeningBalanceRecord>().Property(x => x.Amount).HasPrecision(18, 2);
        model
            .Entity<OpeningBalanceRecord>()
            .HasIndex(x => new { x.WorkspaceId, x.AccountId })
            .IsUnique();
        model
            .Entity<OpeningBalanceRecord>()
            .HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
        model
            .Entity<OpeningBalanceRecord>()
            .HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
        model.Entity<FinanceOperation>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<FinanceOperation>().Property(x => x.CatalogSelectionHash).HasMaxLength(64);
        model.Entity<FinanceOperation>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<FinanceOperation>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model.Entity<FinanceOperation>().HasOne<BankAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FinanceCategory>().HasKey(x => x.Id);
        model.Entity<FinanceCategory>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<FinanceCategory>().Property(x => x.Kind).HasMaxLength(20);
        model.Entity<FinanceCategory>().Property(x => x.Name).HasColumnType("text");
        model.Entity<FinanceCategory>().Property(x => x.IsArchived).HasDefaultValue(false);
        model.Entity<FinanceCategory>().Property(x => x.IsDefault).HasDefaultValue(false);
        model.Entity<FinanceCategory>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<FinanceCategory>().ToTable(table => table.HasCheckConstraint("CK_FinanceCategories_Kind", "\"Kind\" IN ('Expense','Income')"));
        model.Entity<FinanceCategory>().HasIndex(x => new { x.WorkspaceId, x.Kind, x.Name }).IsUnique();
        model.Entity<FinanceCategory>().HasIndex(x => new { x.WorkspaceId, x.Kind }).IsUnique().HasFilter("\"IsDefault\"");
        model.Entity<FinanceCategory>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<FinanceMerchant>().HasKey(x => x.Id);
        model.Entity<FinanceMerchant>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<FinanceMerchant>().Property(x => x.Name).HasColumnType("text");
        model.Entity<FinanceMerchant>().Property(x => x.IsArchived).HasDefaultValue(false);
        model.Entity<FinanceMerchant>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<FinanceMerchant>().HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        model.Entity<FinanceMerchant>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<FinanceIncomeSource>().HasKey(x => x.Id);
        model.Entity<FinanceIncomeSource>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<FinanceIncomeSource>().Property(x => x.Kind).HasMaxLength(30);
        model.Entity<FinanceIncomeSource>().Property(x => x.Name).HasColumnType("text");
        model.Entity<FinanceIncomeSource>().Property(x => x.IsArchived).HasDefaultValue(false);
        model.Entity<FinanceIncomeSource>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<FinanceIncomeSource>().ToTable(table => table.HasCheckConstraint("CK_FinanceIncomeSources_Kind", "\"Kind\" IN ('Salary','Extra','AnnualBonus','IndependentWork','Other')"));
        model.Entity<FinanceIncomeSource>().HasIndex(x => new { x.WorkspaceId, x.Kind, x.Name }).IsUnique();
        model.Entity<FinanceIncomeSource>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<CatalogCommand>().HasKey(x => new { x.WorkspaceId, x.RequestId });
        model.Entity<CatalogCommand>().Property(x => x.CommandKind).HasMaxLength(80);
        model.Entity<CatalogCommand>().Property(x => x.PayloadHash).HasMaxLength(64);
        model.Entity<CatalogCommand>().Property(x => x.ResultJson).HasColumnType("jsonb");
        model.Entity<CatalogCommand>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<CatalogCommand>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FinanceOperation>().HasOne<FinanceCategory>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CategoryId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FinanceOperation>().HasOne<FinanceMerchant>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.MerchantId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FinanceOperation>().HasOne<FinanceIncomeSource>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.IncomeSourceId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FinancePayment>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<FinancePayment>().Property(x => x.CommandPayloadHash).HasMaxLength(64);
        model.Entity<FinancePayment>().Property(x => x.LegacyFinanceOperationId).HasColumnName("FinanceOperationId");
        model.Entity<FinancePayment>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<FinancePayment>().HasOne<BankAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FinancePayment>().HasOne<FinanceOperation>().WithMany().HasForeignKey(x => x.LegacyFinanceOperationId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FinancePayment>().HasOne<FinanceOperation>().WithMany(x => x.Payments).HasForeignKey(x => new { x.WorkspaceId, x.OperationId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        model.Entity<BankAccount>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<LedgerEntry>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<LedgerEntry>().Property(x => x.ObservedBalance).HasPrecision(18, 2);
        model
            .Entity<LedgerEntry>()
            .HasIndex(x => new
            {
                x.WorkspaceId,
                x.Source,
                x.SourceId,
            })
            .IsUnique();
        model.Entity<TransferRecord>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<TransferRecord>().Property(x => x.DestinationAmount).HasPrecision(18, 2);
        model.Entity<TransferRecord>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model.Entity<SharedGroup>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model
            .Entity<MonthlyRule>()
            .HasIndex(x => new
            {
                x.WorkspaceId,
                x.GroupId,
                x.Period,
            })
            .IsUnique();
        model.Entity<MonthlyRule>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<MonthlyRule>().Property(x => x.MutationVersion).IsConcurrencyToken();
        model.Entity<RuleWeight>().Property(x => x.Value).HasPrecision(18, 2);
        model.Entity<CardPurchase>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        model.Entity<CardPurchase>().Property(x => x.CatalogSelectionHash).HasMaxLength(64);
        model.Entity<CardPurchase>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<CardPurchase>().HasIndex(x => new { x.WorkspaceId, x.ExpenseOperationId }).IsUnique().HasFilter("\"ExpenseOperationId\" IS NOT NULL");
        model.Entity<CardPurchase>().HasOne<FinanceOperation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ExpenseOperationId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CardPurchase>().HasOne<FinanceCategory>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CategoryId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CardPurchase>().HasOne<FinanceMerchant>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.MerchantId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CardInstallment>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<SharedCollection>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<SharedCollection>().Property(x => x.AppliedAmount).HasPrecision(18, 2);
        model.Entity<SharedCollection>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model
            .Entity<CardStatement>()
            .HasIndex(x => new
            {
                x.WorkspaceId,
                x.CardId,
                x.Period,
                x.Currency,
            })
            .IsUnique();
        model.Entity<CardStatement>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<StatementAdjustment>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<StatementAdjustment>().HasOne<FinanceCategory>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CategoryId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CardPaymentRecord>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<CardPaymentRecord>().Property(x => x.AppliedAmount).HasPrecision(18, 2);
        model.Entity<CardPaymentRecord>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model
            .Entity<Workspace>()
            .HasMany(x => x.Accounts)
            .WithOne(x => x.Workspace)
            .HasForeignKey(x => x.WorkspaceId);
        model
            .Entity<Workspace>()
            .HasMany(x => x.Cards)
            .WithOne(x => x.Workspace)
            .HasForeignKey(x => x.WorkspaceId);
        model
            .Entity<Workspace>()
            .HasOne(x => x.Draft)
            .WithOne(x => x.Workspace)
            .HasForeignKey<OnboardingDraft>(x => x.WorkspaceId);

        model.Entity<SavingsGoal>().HasKey(x => x.Id);
        model.Entity<SavingsGoal>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<SavingsGoal>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<SavingsGoal>().Property(x => x.TargetCurrency).HasMaxLength(3);
        model.Entity<SavingsGoal>().Property(x => x.TargetAmount).HasPrecision(18, 2);
        model.Entity<SavingsGoal>().Property(x => x.State).HasMaxLength(20);
        model.Entity<SavingsGoal>().Property(x => x.MonthlyContribution).HasPrecision(18, 2);
        model.Entity<SavingsGoal>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<SavingsGoal>().ToTable(table => table.HasCheckConstraint("CK_SavingsGoals_TargetAmount_Positive", "\"TargetAmount\" > 0"));
        model.Entity<SavingsGoal>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

        model.Entity<SavingsAllocation>().HasKey(x => x.Id);
        model.Entity<SavingsAllocation>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<SavingsAllocation>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<SavingsAllocation>().Property(x => x.ReservedAmount).HasPrecision(18, 2);
        model.Entity<SavingsAllocation>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<SavingsAllocation>().ToTable(table => table.HasCheckConstraint("CK_SavingsAllocations_ReservedAmount_NonNegative", "\"ReservedAmount\" >= 0"));
        model.Entity<SavingsAllocation>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<SavingsAllocation>().HasOne<SavingsGoal>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.GoalId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SavingsAllocation>().HasOne<BankAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        model.Entity<SavingsCommand>().HasKey(x => new { x.WorkspaceId, x.RequestId });
        model.Entity<SavingsCommand>().Property(x => x.CommandKind).HasMaxLength(80);
        model.Entity<SavingsCommand>().Property(x => x.PayloadHash).HasMaxLength(64);
        model.Entity<SavingsCommand>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

        model.Entity<SavingsMovement>().HasKey(x => x.Id);
        model.Entity<SavingsMovement>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<SavingsMovement>().Property(x => x.Kind).HasMaxLength(20);
        model.Entity<SavingsMovement>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<SavingsMovement>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<SavingsMovement>().Property(x => x.Version).HasDefaultValue(1);
        model.Entity<SavingsMovement>().Property(x => x.Reason).HasMaxLength(240);
        model.Entity<SavingsMovement>().HasIndex(x => new { x.WorkspaceId, x.CommandId, x.AllocationId, x.Kind }).IsUnique().HasFilter("\"Kind\" <> 'Reversal'");
        model.Entity<SavingsMovement>().HasIndex(x => new { x.WorkspaceId, x.ReversesMovementId }).IsUnique();
        model.Entity<SavingsMovement>().ToTable(table => { table.HasCheckConstraint("CK_SavingsMovements_Amount_Positive", "\"Amount\" > 0"); table.HasCheckConstraint("CK_SavingsMovements_Kind_Allowed", "\"Kind\" IN ('Allocate','Release','Apply','Reversal')"); });
        model.Entity<SavingsMovement>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<SavingsMovement>().HasOne<SavingsAllocation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AllocationId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SavingsMovement>().HasOne<SavingsCommand>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CommandId }).HasPrincipalKey(x => new { x.WorkspaceId, x.RequestId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SavingsMovement>().HasOne<FinancePayment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.FinancePaymentId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SavingsMovement>().HasOne<SavingsMovement>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ReversesMovementId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SavingsMovement>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<ProjectionScenario>().HasKey(x => x.Id);
        model.Entity<ProjectionScenario>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<ProjectionScenario>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<ProjectionScenario>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<ProjectionScenario>().Property(x => x.IsDeleted).HasDefaultValue(false);
        model.Entity<ProjectionScenario>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ProjectionScenario>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<ProjectionAssumption>().HasKey(x => x.Id);
        model.Entity<ProjectionAssumption>().Property(x => x.Kind).HasMaxLength(40);
        model.Entity<ProjectionAssumption>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<ProjectionAssumption>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<ProjectionAssumption>().Property(x => x.Description).HasMaxLength(500);
        model.Entity<ProjectionAssumption>().Property(x => x.SourceKind).HasMaxLength(64);
        model.Entity<ProjectionAssumption>().Property(x => x.ComponentId).HasMaxLength(160);
        model.Entity<ProjectionAssumption>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<ProjectionAssumption>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ProjectionAssumption>().HasOne<ProjectionScenario>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ScenarioId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Cascade);

        model.Entity<ExchangeRateSnapshot>().HasKey(x => x.Id);
        model.Entity<ExchangeRateSnapshot>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<ExchangeRateSnapshot>().Property(x => x.House).HasMaxLength(80);
        model.Entity<ExchangeRateSnapshot>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<ExchangeRateSnapshot>().Property(x => x.Buy).HasPrecision(18, 8);
        model.Entity<ExchangeRateSnapshot>().Property(x => x.Sell).HasPrecision(18, 8);
        model.Entity<ExchangeRateSnapshot>().HasIndex(x => new { x.Currency, x.House, x.ProviderUpdatedAt, x.Buy, x.Sell }).IsUnique();
        model.Entity<ExchangeRateSnapshot>().ToTable(table => table.HasCheckConstraint("CK_ExchangeRateSnapshots_Usd", "\"Currency\" = 'USD' AND (\"Buy\" IS NULL OR \"Buy\" > 0) AND (\"Sell\" IS NULL OR \"Sell\" > 0) AND (\"Buy\" IS NOT NULL OR \"Sell\" IS NOT NULL)"));
        model.Entity<SavingsGoalRateChoiceEntity>().HasKey(x => new { x.WorkspaceId, x.GoalId });
        model.Entity<SavingsGoalRateChoiceEntity>().Property(x => x.Mode).HasMaxLength(12);
        model.Entity<SavingsGoalRateChoiceEntity>().Property(x => x.House).HasMaxLength(80);
        model.Entity<SavingsGoalRateChoiceEntity>().Property(x => x.Side).HasMaxLength(8);
        model.Entity<SavingsGoalRateChoiceEntity>().Property(x => x.ManualArsPerUsd).HasPrecision(18, 8);
        model.Entity<SavingsGoalRateChoiceEntity>().Property(x => x.ManualReason).HasMaxLength(240);
        model.Entity<SavingsGoalRateChoiceEntity>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<SavingsGoalRateChoiceEntity>().ToTable(table => table.HasCheckConstraint("CK_SavingsGoalRateChoices_Mode", "(\"Mode\" = 'Manual' AND \"SnapshotId\" IS NULL AND \"House\" IS NULL AND \"Side\" IS NULL AND \"ManualArsPerUsd\" > 0 AND \"ManualReason\" IS NOT NULL) OR (\"Mode\" = 'Snapshot' AND \"SnapshotId\" IS NOT NULL AND \"House\" IS NOT NULL AND \"Side\" IN ('Compra','Venta') AND \"ManualArsPerUsd\" IS NULL AND \"ManualReason\" IS NULL)"));
        model.Entity<SavingsGoalRateChoiceEntity>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<SavingsGoalRateChoiceEntity>().HasOne<SavingsGoal>().WithOne().HasForeignKey<SavingsGoalRateChoiceEntity>(x => new { x.WorkspaceId, x.GoalId }).HasPrincipalKey<SavingsGoal>(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        model.Entity<SavingsGoalRateChoiceEntity>().HasOne<ExchangeRateSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ExchangeRateRefreshCommand>().HasKey(x => new { x.WorkspaceId, x.RequestId });
        model.Entity<ExchangeRateRefreshCommand>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

        model.Entity<PaymentCard>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<RecurringRule>().HasKey(x => x.Id);
        model.Entity<RecurringRule>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<RecurringRule>().Property(x => x.State).HasMaxLength(20);
        model.Entity<RecurringRule>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model.Entity<RecurringRule>().ToTable(t => t.HasCheckConstraint("CK_RecurringRules_State", "\"State\" IN ('Active','Paused','Ended') AND \"CurrentVersion\" > 0"));
        model.Entity<RecurringRule>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

        model.Entity<RecurringRuleRevision>().HasKey(x => x.Id);
        model.Entity<RecurringRuleRevision>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<RecurringRuleRevision>().HasAlternateKey(x => new { x.WorkspaceId, x.RuleId, x.Id });
        model.Entity<RecurringRuleRevision>().Property(x => x.State).HasMaxLength(20);
        model.Entity<RecurringRuleRevision>().Property(x => x.Flow).HasMaxLength(20);
        model.Entity<RecurringRuleRevision>().Property(x => x.Description).HasMaxLength(240);
        model.Entity<RecurringRuleRevision>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<RecurringRuleRevision>().Property(x => x.AmountMode).HasMaxLength(20);
        model.Entity<RecurringRuleRevision>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<RecurringRuleRevision>().Property(x => x.Frequency).HasMaxLength(20);
        model.Entity<RecurringRuleRevision>().HasIndex(x => new { x.WorkspaceId, x.RuleId, x.Version }).IsUnique();
        model.Entity<RecurringRuleRevision>().HasIndex(x => new { x.WorkspaceId, x.RuleId, x.EffectiveFrom, x.Version });
        model.Entity<RecurringRuleRevision>().ToTable(t => {
            t.HasCheckConstraint("CK_RecurringRuleRevisions_State", "\"State\" IN ('Active','Paused','Ended')");
            t.HasCheckConstraint("CK_RecurringRuleRevisions_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_RecurringRuleRevisions_Amount", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_RecurringRuleRevisions_Interval", "\"Interval\" > 0");
            t.HasCheckConstraint("CK_RecurringRuleRevisions_FlowAndAmountMode", "\"Flow\" IN ('Income','Expense') AND \"AmountMode\" IN ('Fixed','Estimated')");
            t.HasCheckConstraint("CK_RecurringRuleRevisions_Frequency", "\"Frequency\" IN ('Monthly','Weekly','Annual')");
            t.HasCheckConstraint("CK_RecurringRuleRevisions_DueDay", "\"DueDay\" IS NULL OR \"DueDay\" BETWEEN 1 AND 31");
            t.HasCheckConstraint("CK_RecurringRuleRevisions_Currency", "\"Currency\" IN ('ARS','USD')");
        });
        model.Entity<RecurringRuleRevision>().HasOne<RecurringRule>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RuleId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RecurringRuleRevision>().HasOne<FinanceCategory>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CategoryId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringRuleRevision>().HasOne<FinanceMerchant>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.MerchantId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringRuleRevision>().HasOne<FinanceIncomeSource>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.IncomeSourceId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringRuleRevision>().HasOne<BankAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.PreferredAccountId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringRuleRevision>().HasOne<PaymentCard>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.PreferredCardId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        model.Entity<RecurringOccurrenceAction>().HasKey(x => x.Id);
        model.Entity<RecurringOccurrenceAction>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<RecurringOccurrenceAction>().HasAlternateKey(x => new { x.WorkspaceId, x.Id, x.RuleRevisionId });
        model.Entity<RecurringOccurrenceAction>().Property(x => x.Outcome).HasMaxLength(20);
        model.Entity<RecurringOccurrenceAction>().Property(x => x.Reason).HasMaxLength(240);
        model.Entity<RecurringOccurrenceAction>().Property(x => x.PayloadHash).HasMaxLength(64);
        model.Entity<RecurringOccurrenceAction>().HasIndex(x => new { x.WorkspaceId, x.RuleId, x.ScheduledDate }).IsUnique();
        model.Entity<RecurringOccurrenceAction>().HasIndex(x => new { x.WorkspaceId, x.RequestId }).IsUnique();
        model.Entity<RecurringOccurrenceAction>().ToTable(t => t.HasCheckConstraint("CK_RecurringOccurrenceActions_OutcomeSources", "(\"Outcome\" = 'Recorded' AND \"FinanceOperationId\" IS NOT NULL) OR (\"Outcome\" = 'Skipped' AND \"FinanceOperationId\" IS NULL AND \"CardPurchaseId\" IS NULL)"));
        model.Entity<RecurringOccurrenceAction>().HasOne<RecurringRule>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RuleId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringOccurrenceAction>().HasOne<RecurringRuleRevision>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RuleId, x.RuleRevisionId }).HasPrincipalKey(x => new { x.WorkspaceId, x.RuleId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringOccurrenceAction>().HasOne<FinanceOperation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.FinanceOperationId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringOccurrenceAction>().HasOne<CardPurchase>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardPurchaseId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        model.Entity<HealthcareComponent>().HasKey(x => x.Id);
        model.Entity<HealthcareComponent>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<HealthcareComponent>().Property(x => x.Kind).HasMaxLength(32);
        model.Entity<HealthcareComponent>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<HealthcareComponent>().ToTable(t => {
            t.HasCheckConstraint("CK_HealthcareComponents_Kind", "\"Kind\" IN ('ProviderCharge','ProviderDiscount','PayrollWithholding','HouseholdContribution','ExpectedRefund','Other')");
            t.HasCheckConstraint("CK_HealthcareComponents_Amount", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_HealthcareComponents_AppliesToProviderBalance", "NOT \"AppliesToProviderBalance\" OR \"Kind\" = 'PayrollWithholding'");
        });
        model.Entity<HealthcareComponent>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<HealthcareComponent>().HasOne<RecurringRuleRevision>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RuleRevisionId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<HealthcareComponent>().HasOne<RecurringOccurrenceAction>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.OccurrenceActionId, x.RuleRevisionId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id, x.RuleRevisionId }).OnDelete(DeleteBehavior.Restrict);

        model.Entity<NonCashSettlement>().HasKey(x => x.Id);
        model.Entity<NonCashSettlement>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<NonCashSettlement>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<NonCashSettlement>().Property(x => x.Kind).HasMaxLength(24);
        model.Entity<NonCashSettlement>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<NonCashSettlement>().Property(x => x.PayloadHash).HasMaxLength(64);
        model.Entity<NonCashSettlement>().HasIndex(x => new { x.WorkspaceId, x.RequestId }).IsUnique();
        model.Entity<NonCashSettlement>().HasIndex(x => new { x.WorkspaceId, x.ReversesSettlementId }).IsUnique().HasFilter("\"ReversesSettlementId\" IS NOT NULL");
        model.Entity<NonCashSettlement>().ToTable(t => {
            t.HasCheckConstraint("CK_NonCashSettlements_Amount", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_NonCashSettlements_KindSource", "(\"Kind\" = 'PayrollWithholding' AND \"HealthcareComponentId\" IS NOT NULL AND \"CardPurchaseId\" IS NULL AND \"ReversesSettlementId\" IS NULL) OR (\"Kind\" = 'CardCharge' AND \"HealthcareComponentId\" IS NULL AND \"CardPurchaseId\" IS NOT NULL AND \"ReversesSettlementId\" IS NULL) OR (\"Kind\" = 'Reversal' AND \"HealthcareComponentId\" IS NULL AND \"CardPurchaseId\" IS NULL AND \"ReversesSettlementId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_NonCashSettlements_Currency", "\"Currency\" IN ('ARS','USD')");
        });
        model.Entity<NonCashSettlement>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<NonCashSettlement>().HasOne<FinanceOperation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.OperationId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NonCashSettlement>().HasOne<HealthcareComponent>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.HealthcareComponentId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NonCashSettlement>().HasOne<CardPurchase>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardPurchaseId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NonCashSettlement>().HasOne<NonCashSettlement>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ReversesSettlementId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NonCashSettlement>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<RecurringRuleCommand>().HasKey(x => x.Id);
        model.Entity<RecurringRuleCommand>().HasIndex(x => new { x.WorkspaceId, x.RequestId }).IsUnique();
        model.Entity<RecurringRuleCommand>().Property(x => x.CommandKind).HasMaxLength(80);
        model.Entity<RecurringRuleCommand>().Property(x => x.PayloadHash).HasMaxLength(64);
        model.Entity<RecurringRuleCommand>().Property(x => x.ResultJson).HasColumnType("jsonb");
        model.Entity<RecurringRuleCommand>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RecurringRuleCommand>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecurringRuleCommand>().HasOne<RecurringRule>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RuleId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        model.Entity<MonthlyBudget>().HasKey(x => x.Id);
        model.Entity<MonthlyBudget>().HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        model.Entity<MonthlyBudget>().Property(x => x.Period).HasMaxLength(7);
        model.Entity<MonthlyBudget>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<MonthlyBudget>().Property(x => x.Limit).HasPrecision(18, 2);
        model.Entity<MonthlyBudget>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        model.Entity<MonthlyBudget>().HasIndex(x => new { x.WorkspaceId, x.Period, x.CategoryId, x.Currency }).IsUnique();
        model.Entity<MonthlyBudget>().ToTable(table => table.HasCheckConstraint("CK_MonthlyBudgets_Limit_NonNegative", "\"Limit\" >= 0"));
        model.Entity<MonthlyBudget>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<MonthlyBudget>().HasOne<FinanceCategory>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CategoryId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BudgetCommand>().HasKey(x => new { x.WorkspaceId, x.RequestId });
        model.Entity<BudgetCommand>().Property(x => x.CommandKind).HasMaxLength(40);
        model.Entity<BudgetCommand>().Property(x => x.PayloadHash).HasMaxLength(64);
        model.Entity<BudgetCommand>().Property(x => x.ResultJson).HasColumnType("jsonb");
        model.Entity<BudgetCommand>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<BudgetCommand>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BudgetCommand>().HasOne<MonthlyBudget>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.TargetBudgetId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
