using Microsoft.EntityFrameworkCore;
using EnigmaBudget.Api.Workspaces;
using EnigmaBudget.Api.Identity;

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
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
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
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
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
        model.Entity<FinanceOperation>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<FinanceOperation>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
        model.Entity<FinancePayment>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<FinancePayment>().HasIndex(x => new { x.WorkspaceId, x.Id }).IsUnique();
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
    }
}
