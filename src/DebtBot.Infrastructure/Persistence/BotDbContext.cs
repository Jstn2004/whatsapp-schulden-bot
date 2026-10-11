using DebtBot.Core.Application;
using DebtBot.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DebtBot.Infrastructure.Persistence;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options), IBotDb
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<BotGroup> Groups => Set<BotGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<PaymentRequest> Requests => Set<PaymentRequest>();
    public DbSet<Share> Shares => Set<Share>();
    public DbSet<PendingDialog> Dialogs => Set<PendingDialog>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public void DiscardChanges() => ChangeTracker.Clear();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored as text so the database stays readable.
        builder.Properties<GroupRole>().HaveConversion<string>();
        builder.Properties<SplitMode>().HaveConversion<string>();
        builder.Properties<RequestStatus>().HaveConversion<string>();
        builder.Properties<ShareStatus>().HaveConversion<string>();
        builder.Properties<DialogType>().HaveConversion<string>();
        builder.Properties<decimal>().HavePrecision(10, 2);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Person>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.DisplayName).HasMaxLength(40);
        });

        model.Entity<Chat>().HasKey(c => c.Id);

        model.Entity<BotGroup>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(30);
            e.Property(g => g.NormalizedName).HasMaxLength(30);
            // Names are unique per chat among groups that are not deleted, so a deleted name can be reused.
            e.HasIndex(g => new { g.ChatId, g.NormalizedName }).IsUnique().HasFilter("DeletedAt IS NULL");
            e.HasMany(g => g.Members).WithOne(m => m.Group).HasForeignKey(m => m.GroupId);
        });

        model.Entity<GroupMember>(e =>
        {
            e.HasKey(m => new { m.GroupId, m.PersonId });
            e.HasOne(m => m.Person).WithMany().HasForeignKey(m => m.PersonId);
        });

        model.Entity<PaymentRequest>(e =>
        {
            e.HasIndex(r => new { r.ChatId, r.Number }).IsUnique();
            e.Property(r => r.Reason).HasMaxLength(100);
            e.HasOne(r => r.Group).WithMany().HasForeignKey(r => r.GroupId);
            e.HasOne(r => r.CreatedBy).WithMany().HasForeignKey(r => r.CreatedById);
            e.HasMany(r => r.Shares).WithOne(s => s.Request).HasForeignKey(s => s.RequestId);
        });

        model.Entity<Share>(e =>
        {
            e.HasIndex(s => new { s.RequestId, s.DebtorId }).IsUnique();
            e.HasOne(s => s.Debtor).WithMany().HasForeignKey(s => s.DebtorId);
        });

        model.Entity<PendingDialog>().HasKey(d => d.PersonId);
        model.Entity<ProcessedMessage>().HasKey(m => m.MessageId);
    }
}

/// <summary>Lets "dotnet ef" create migrations without starting the host.</summary>
public sealed class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BotDbContext>().UseSqlite("Data Source=design.db").Options);
}
