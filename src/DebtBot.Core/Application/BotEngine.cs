using DebtBot.Core.Application.Commands;
using DebtBot.Core.Domain;
using DebtBot.Core.Messaging;
using DebtBot.Core.Parsing;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Core.Application;

/// <summary>
/// Entry point for every incoming message: deduplicates, rate-limits, records people,
/// runs the command and saves all changes in one go.
/// </summary>
public sealed class BotEngine(IBotDb db, TimeProvider time, RateLimiter rateLimiter)
{
    private static readonly Dictionary<string, Func<CommandContext, Task>> Handlers = new()
    {
        ["help"] = HelpCommand.HandleAsync,
        ["group"] = GroupCommands.HandleAsync,
        ["request"] = RequestCommands.HandleAsync,
        ["pay"] = PayCommand.HandleAsync,
        ["yes"] = DialogCommands.YesAsync,
        ["no"] = DialogCommands.NoAsync,
        ["edit"] = DialogCommands.EditAsync,
    };

    // These commands answer an open dialog; every other command discards it.
    private static readonly HashSet<string> DialogAnswers = ["yes", "no", "edit", "pay"];

    public async Task<IReadOnlyList<OutgoingMessage>> HandleAsync(IncomingMessage message, CancellationToken ct = default)
    {
        var command = CommandParser.Parse(message.Body, message.Mentions);
        if (command is null) return [];
        if (await db.ProcessedMessages.AnyAsync(m => m.MessageId == message.MessageId, ct)) return [];

        var now = time.GetUtcNow().UtcDateTime;
        switch (rateLimiter.Check(message.SenderId, now))
        {
            case RateDecision.Blocked:
                return [];
            case RateDecision.BlockedWarn:
                return [new OutgoingMessage(message.ChatId, "⏳ Zu viele Befehle auf einmal. Warte kurz und versuch es dann nochmal.", [])];
        }

        var sender = await UpsertPersonAsync(message.SenderId, message.SenderName, now, updateName: true);
        foreach (var mention in message.Mentions.Where(m => m.Id != message.BotId))
        {
            await UpsertPersonAsync(mention.Id, mention.Name, now, updateName: false);
        }

        if (message.IsGroup && await db.Chats.FindAsync([message.ChatId], ct) is null)
        {
            db.Chats.Add(new Chat { Id = message.ChatId, CreatedAt = now });
        }

        var ctx = new CommandContext(db, message, command, sender, now);
        try
        {
            if (!Handlers.TryGetValue(command.Name, out var handler))
            {
                throw new UserError($"🤷 Den Befehl /{command.Name} kenne ich nicht. Alle Befehle: /help");
            }

            if (!DialogAnswers.Contains(command.Name)) await ctx.ClearDialogAsync();
            await handler(ctx);
        }
        catch (UserError error)
        {
            // Nothing of a failed command is saved; the message may be retried after fixing it.
            db.DiscardChanges();
            return [new OutgoingMessage(message.ChatId, error.Message, [])];
        }

        db.ProcessedMessages.Add(new ProcessedMessage { MessageId = message.MessageId, ProcessedAt = now });
        await db.SaveChangesAsync(ct);
        return ctx.Outbox;
    }

    private async Task<Person> UpsertPersonAsync(string id, string? name, DateTime now, bool updateName)
    {
        var cleaned = string.IsNullOrWhiteSpace(name) ? null : name.Trim()[..Math.Min(name.Trim().Length, 40)];
        var person = await db.People.FindAsync(id);
        if (person is null)
        {
            person = new Person { Id = id, DisplayName = cleaned ?? "+" + id.Split('@')[0], CreatedAt = now, UpdatedAt = now };
            db.People.Add(person);
            return person;
        }

        // People keep the name from their own last message; a mention only fills in an unknown name.
        var nameUnknown = person.DisplayName.StartsWith('+');
        if (cleaned is not null && cleaned != person.DisplayName && (updateName || nameUnknown))
        {
            person.DisplayName = cleaned;
            person.UpdatedAt = now;
        }

        return person;
    }
}
