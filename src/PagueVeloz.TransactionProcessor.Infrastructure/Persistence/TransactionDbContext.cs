using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Persistence;

public sealed class TransactionDbContext : DbContext
{
  public TransactionDbContext(DbContextOptions<TransactionDbContext> options) : base(options) { }

  public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
  public DbSet<TransactionEntity> Transactions => Set<TransactionEntity>();
  public DbSet<AccountEventEntity> AccountEvents => Set<AccountEventEntity>();
  public DbSet<OutboxEventEntity> OutboxEvents => Set<OutboxEventEntity>();
  public DbSet<QueuedCommandEntity> QueuedCommands => Set<QueuedCommandEntity>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<AccountEntity>(b =>
    {
      b.ToTable("Accounts");
      b.HasKey(x => x.AccountId);
      b.Property(x => x.AccountId).HasMaxLength(64);
      b.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
      b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
      b.Property(x => x.Balance).IsRequired();
      b.Property(x => x.ReservedBalance).IsRequired();
      b.Property(x => x.CreditLimit).IsRequired();
      b.Property(x => x.Status).IsRequired();
      b.Property(x => x.LedgerSequence).IsRequired();
      b.Property(x => x.RowVersion).IsRowVersion();
      b.HasIndex(x => x.ClientId);
    });

    modelBuilder.Entity<TransactionEntity>(b =>
    {
      b.ToTable("Transactions");
      b.HasKey(x => x.TransactionId);
      b.Property(x => x.TransactionId).HasMaxLength(160);
      b.Property(x => x.ReferenceId).HasMaxLength(128).IsRequired();
      b.Property(x => x.AccountId).HasMaxLength(64).IsRequired();
      b.Property(x => x.Operation).IsRequired();
      b.Property(x => x.Amount).IsRequired();
      b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
      b.Property(x => x.Status).IsRequired();
      b.Property(x => x.ErrorMessage).HasMaxLength(4000);
      b.Property(x => x.TargetAccountId).HasMaxLength(64);
      b.Property(x => x.RelatedReferenceId).HasMaxLength(128);
      b.Property(x => x.IsReversed).IsRequired();

      b.HasIndex(x => x.ReferenceId).IsUnique();
      b.HasIndex(x => x.AccountId);
    });

    modelBuilder.Entity<AccountEventEntity>(b =>
    {
      b.ToTable("AccountEvents");
      b.HasKey(x => x.EventId);
      b.Property(x => x.AccountId).HasMaxLength(64).IsRequired();
      b.Property(x => x.Sequence).IsRequired();
      b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
      b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
      b.Property(x => x.ReferenceId).HasMaxLength(128).IsRequired();
      b.Property(x => x.RelatedReferenceId).HasMaxLength(128);
      b.Property(x => x.TargetAccountId).HasMaxLength(64);
      b.Property(x => x.MetadataJson).HasMaxLength(8000).IsRequired();

      b.HasIndex(x => new { x.AccountId, x.Sequence }).IsUnique();
      b.HasIndex(x => x.ReferenceId);
    });

    modelBuilder.Entity<OutboxEventEntity>(b =>
    {
      b.ToTable("OutboxEvents");
      b.HasKey(x => x.EventId);
      b.Property(x => x.AggregateId).HasMaxLength(64).IsRequired();
      b.Property(x => x.EventType).HasMaxLength(128).IsRequired();
      b.Property(x => x.PayloadJson).IsRequired();
      b.Property(x => x.Attempts).IsRequired();
      b.Property(x => x.NextAttemptAt).IsRequired();
      b.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
    });

    modelBuilder.Entity<QueuedCommandEntity>(b =>
    {
      b.ToTable("QueuedCommands");
      b.HasKey(x => x.CommandId);
      b.Property(x => x.PayloadJson).IsRequired();
      b.Property(x => x.Status).HasMaxLength(32).IsRequired();
      b.Property(x => x.ErrorMessage).HasMaxLength(2000);
      b.HasIndex(x => new { x.Status, x.EnqueuedAt });
    });
  }
}
