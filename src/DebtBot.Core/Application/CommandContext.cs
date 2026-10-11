using System.Text.Json;
using DebtBot.Core.Domain;
using DebtBot.Core.Messaging;
using DebtBot.Core.Parsing;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Core.Application;

/// <summary>Everything a command handler needs: the message, the sender, the database and the outbox.</summary>
public sealed class CommandContext(IBotDb db, IncomingMessage message, ParsedCommand command, Person sender, DateTime now)
{
    public static readonly TimeSpan DialogLifetime = TimeSpan.FromMinutes(5);

    public IBotDb Db => db;
    public IncomingMessage Message => message;
    public ParsedCommand Command => command;
    public IReadOnlyList<Token> Args => command.Args;
    public Person Sender => sender;
    public DateTime Now => now;
    public string ChatId => message.ChatId;
    public bool IsGroupChat => message.IsGroup;
    public List<OutgoingMessage> Outbox { get; } = [];

    public void Send(OutgoingMessage outgoing)
    {
        // The gateway forwards every "/" message, so the bot must never start one itself.
        if (outgoing.Text.StartsWith('/')) throw new InvalidOperationException("Bot messages must not start with '/'.");
        Outbox.Add(outgoing);
    }

    public void Reply(string text, IReadOnlyList<string>? mentions = null) => Send(new OutgoingMessage(ChatId, text, mentions ?? []));

    /// <summary>Sends a private message; if that fails, the text is posted in <paramref name="fallbackChatId"/> with an @mention.</summary>
    public void SendPrivate(string personId, string text, string? fallbackChatId) => Send(new OutgoingMessage(personId, text, [])
    {
        FallbackChatId = fallbackChatId,
        FallbackText = fallbackChatId is null ? null : $"{Text.Tag(personId)} {text}",
        FallbackMentions = [personId],
    });

    /// <summary>Answers the sender privately. In a group chat the group only gets a short note.</summary>
    public void ReplyPrivately(string text)
    {
        if (!IsGroupChat)
        {
            Reply(text);
            return;
        }

        SendPrivate(Sender.Id, text, ChatId);
        Reply("📩 Hab dir privat geschrieben.");
    }

    public void Audit(string action, string details) => db.AuditLog.Add(new AuditEntry
    {
        At = now,
        PersonId = sender.Id,
        ChatId = ChatId,
        Action = action,
        Details = details,
    });

    public void RequireGroupChat()
    {
        if (!IsGroupChat) throw new UserError("Das geht nur in einem Gruppenchat.");
    }

    // ---- Dialogs ----

    public async Task<PendingDialog?> GetDialogAsync() => await db.Dialogs.FindAsync(sender.Id);

    public async Task SetDialogAsync(DialogType type, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var dialog = await GetDialogAsync();
        if (dialog is null)
        {
            db.Dialogs.Add(new PendingDialog { PersonId = sender.Id, ChatId = ChatId, Type = type, Data = json, ExpiresAt = now + DialogLifetime });
            return;
        }

        dialog.ChatId = ChatId;
        dialog.Type = type;
        dialog.Data = json;
        dialog.ExpiresAt = now + DialogLifetime;
    }

    public async Task ClearDialogAsync()
    {
        var dialog = await GetDialogAsync();
        if (dialog is not null) db.Dialogs.Remove(dialog);
    }

    public static T ReadDialog<T>(PendingDialog dialog) => JsonSerializer.Deserialize<T>(dialog.Data)!;

    // ---- Groups ----

    public IQueryable<BotGroup> GroupsInChat => db.Groups
        .Include(g => g.Members).ThenInclude(m => m.Person)
        .Where(g => g.ChatId == ChatId && g.DeletedAt == null);

    public async Task<BotGroup> FindGroupAsync(string name)
    {
        var normalized = name.ToLowerInvariant();
        return await GroupsInChat.FirstOrDefaultAsync(g => g.NormalizedName == normalized)
            ?? throw new UserError($"Die Gruppe *{name}* gibt es hier nicht. Alle Gruppen: /group list");
    }

    /// <summary>The named group, or the default group if no name is given.</summary>
    public async Task<BotGroup> GroupOrDefaultAsync(string? name)
    {
        if (name is not null) return await FindGroupAsync(name);
        return await GroupsInChat.FirstOrDefaultAsync(g => g.IsDefault)
            ?? throw new UserError("Hier gibt es noch keine Gruppe. Erstell eine:\n/group create WG @Anna @Ben");
    }
}
