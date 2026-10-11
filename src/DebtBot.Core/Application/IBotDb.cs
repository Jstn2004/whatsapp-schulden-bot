using DebtBot.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Core.Application;

/// <summary>The persistence the application needs; implemented by the EF Core context in Infrastructure.</summary>
public interface IBotDb
{
    DbSet<Person> People { get; }
    DbSet<Chat> Chats { get; }
    DbSet<BotGroup> Groups { get; }
    DbSet<GroupMember> GroupMembers { get; }
    DbSet<PaymentRequest> Requests { get; }
    DbSet<Share> Shares { get; }
    DbSet<PendingDialog> Dialogs { get; }
    DbSet<AuditEntry> AuditLog { get; }
    DbSet<ProcessedMessage> ProcessedMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Forgets all unsaved changes, used when a command fails halfway.</summary>
    void DiscardChanges();
}
