using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Data;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerSubject { get; set; }
    public OnboardingDraft? Draft { get; set; }
    public List<BankAccount> Accounts { get; set; } = [];
    public List<PaymentCard> Cards { get; set; } = [];
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

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<OnboardingDraft> Drafts => Set<OnboardingDraft>();
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<PaymentCard> Cards => Set<PaymentCard>();
    public DbSet<OpeningBalanceRecord> OpeningBalances => Set<OpeningBalanceRecord>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Workspace>().HasIndex(x => x.OwnerSubject).IsUnique();
        model.Entity<OnboardingDraft>().HasKey(x => x.WorkspaceId);
        model.Entity<OnboardingDraft>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<BankAccount>().Property(x => x.OpeningBalance).HasPrecision(18, 2);
        model.Entity<BankAccount>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<BankAccount>().Property(x => x.BankName).HasMaxLength(120);
        model.Entity<BankAccount>().Property(x => x.Currency).HasMaxLength(3);
        model.Entity<PaymentCard>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<PaymentCard>().Property(x => x.BankName).HasMaxLength(120);
        model.Entity<PaymentCard>().Property(x => x.StatementDebtArs).HasPrecision(18, 2);
        model.Entity<PaymentCard>().Property(x => x.StatementDebtUsd).HasPrecision(18, 2);
        model.Entity<OpeningBalanceRecord>().Property(x => x.Amount).HasPrecision(18, 2);
        model.Entity<OpeningBalanceRecord>().HasIndex(x => new { x.WorkspaceId, x.AccountId }).IsUnique();
        model.Entity<OpeningBalanceRecord>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<OpeningBalanceRecord>().HasOne<BankAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Workspace>().HasMany(x => x.Accounts).WithOne(x => x.Workspace).HasForeignKey(x => x.WorkspaceId);
        model.Entity<Workspace>().HasMany(x => x.Cards).WithOne(x => x.Workspace).HasForeignKey(x => x.WorkspaceId);
        model.Entity<Workspace>().HasOne(x => x.Draft).WithOne(x => x.Workspace).HasForeignKey<OnboardingDraft>(x => x.WorkspaceId);
    }
}
