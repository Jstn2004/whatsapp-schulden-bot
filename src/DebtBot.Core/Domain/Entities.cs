namespace DebtBot.Core.Domain;

/// <summary>A WhatsApp user, identified by their WhatsApp ID (phone number), not their display name.</summary>
public class Person
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A WhatsApp group chat the bot is a member of. All data is scoped per chat.</summary>
public class Chat
{
    public required string Id { get; set; }
    public int NextRequestNumber { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
}

/// <summary>A named set of people within a chat, managed by the bot.</summary>
public class BotGroup
{
    public int Id { get; set; }
    public required string ChatId { get; set; }
    public required string Name { get; set; }

    /// <summary>Lower-case name, used for case-insensitive uniqueness per chat.</summary>
    public required string NormalizedName { get; set; }

    public required string CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool IsDefault { get; set; }

    public bool Verify { get; set; }
    public int RemindAfterDays { get; set; } = 3;
    public bool RemindRepeat { get; set; } = true;

    /// <summary>0 means unlimited.</summary>
    public int MaxReminders { get; set; } = 5;

    public List<GroupMember> Members { get; set; } = [];
}

public class GroupMember
{
    public int GroupId { get; set; }
    public BotGroup Group { get; set; } = null!;
    public required string PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public GroupRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}

/// <summary>A payment request: the creator paid in advance, the debtors each owe a share.</summary>
public class PaymentRequest
{
    public int Id { get; set; }
    public required string ChatId { get; set; }

    /// <summary>Short running number per chat, shown as #12.</summary>
    public int Number { get; set; }

    public int GroupId { get; set; }
    public BotGroup Group { get; set; } = null!;
    public required string CreatedById { get; set; }
    public Person CreatedBy { get; set; } = null!;
    public decimal TotalAmount { get; set; }

    /// <summary>The creator's own part of the total, or null if they are not included.</summary>
    public decimal? CreatorShare { get; set; }

    public required string Reason { get; set; }
    public SplitMode SplitMode { get; set; }
    public bool Verify { get; set; }
    public RequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>WhatsApp ID of the bot's announcement in the chat, used for pinning.</summary>
    public string? MessageId { get; set; }

    public DateTime? PinnedUntil { get; set; }
    public List<Share> Shares { get; set; } = [];
}

/// <summary>The amount one debtor owes for a request.</summary>
public class Share
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public PaymentRequest Request { get; set; } = null!;
    public required string DebtorId { get; set; }
    public Person Debtor { get; set; } = null!;
    public decimal Amount { get; set; }
    public ShareStatus Status { get; set; }
    public DateTime? ReportedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public int ReminderCount { get; set; }
    public DateTime? LastReminderAt { get; set; }
}

/// <summary>A question the bot is waiting for an answer to (/yes, /no, /edit or a list number).</summary>
public class PendingDialog
{
    /// <summary>Each person has at most one open dialog.</summary>
    public required string PersonId { get; set; }

    public required string ChatId { get; set; }
    public DialogType Type { get; set; }

    /// <summary>Type-specific data as JSON.</summary>
    public required string Data { get; set; }

    public DateTime ExpiresAt { get; set; }
}

public class AuditEntry
{
    public int Id { get; set; }
    public DateTime At { get; set; }
    public string? PersonId { get; set; }
    public string? ChatId { get; set; }
    public required string Action { get; set; }
    public required string Details { get; set; }
}

/// <summary>Remembers handled WhatsApp message IDs so a redelivered message is not booked twice.</summary>
public class ProcessedMessage
{
    public required string MessageId { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public enum GroupRole { Member, Admin }

public enum SplitMode { Equal, Custom }

public enum RequestStatus { Open, Completed, Deleted }

public enum ShareStatus { Open, Reported, Paid }

public enum DialogType { RequestPreview, GroupDelete, GroupRemoveMember, PayChoice }
