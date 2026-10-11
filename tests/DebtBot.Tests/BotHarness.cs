using DebtBot.Core.Application;
using DebtBot.Core.Messaging;
using DebtBot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Tests;

public sealed record Who(string Id, string Name)
{
    public string Token => Id.Split('@')[0];
}

public sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// Runs the real engine against an in-memory SQLite database. Messages are written like in WhatsApp,
/// "@Anna" in a test body becomes a real mention of Anna.
/// </summary>
public sealed class BotHarness : IAsyncDisposable
{
    public const string GroupChat = "120363000000000001@g.us";
    public const string OtherChat = "120363000000000002@g.us";
    public const string BotId = "4900000000000@c.us";

    public static readonly Who Justin = new("4915100000001@c.us", "Justin");
    public static readonly Who Anna = new("4915100000002@c.us", "Anna");
    public static readonly Who Ben = new("4915100000003@c.us", "Ben");
    public static readonly Who Chris = new("4915100000004@c.us", "Chris");
    public static readonly Who Bot = new(BotId, "Bot");

    private static readonly Who[] Everyone = [Justin, Anna, Ben, Chris, Bot];

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<BotDbContext> _options;
    private readonly RateLimiter _rateLimiter;
    private int _messageCounter;

    public FakeTime Time { get; } = new();

    public BotHarness(int commandsPerMinute = 1000)
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(_connection).Options;
        using var db = NewDb();
        db.Database.Migrate();
        _rateLimiter = new RateLimiter(commandsPerMinute);
    }

    public BotDbContext NewDb() => new(_options);

    public Task<IReadOnlyList<OutgoingMessage>> Send(Who from, string body, string chat = GroupChat, string? messageId = null)
    {
        var mentions = new List<Mention>();
        foreach (var person in Everyone)
        {
            if (!body.Contains("@" + person.Name)) continue;
            body = body.Replace("@" + person.Name, "@" + person.Token);
            mentions.Add(new Mention(person.Token, person.Id, person.Name));
        }

        var message = new IncomingMessage(
            messageId ?? $"msg-{++_messageCounter}",
            chat,
            chat.EndsWith("@g.us"),
            from.Id,
            from.Name,
            body,
            mentions,
            BotId,
            Time.Now.UtcDateTime);
        return Handle(message);
    }

    public Task<IReadOnlyList<OutgoingMessage>> SendPrivate(Who from, string body) => Send(from, body, chat: from.Id);

    private async Task<IReadOnlyList<OutgoingMessage>> Handle(IncomingMessage message)
    {
        // A fresh context per message, like one DI scope per message in the host.
        await using var db = NewDb();
        return await new BotEngine(db, Time, _rateLimiter).HandleAsync(message);
    }

    /// <summary>Creates the group "WG" with Justin as admin and the given members.</summary>
    public async Task CreateWg(params Who[] members)
    {
        var mentions = string.Join(' ', (members.Length == 0 ? [Anna, Ben, Chris] : members).Select(m => "@" + m.Name));
        await Send(Justin, $"/group create WG {mentions}");
    }

    /// <summary>Creates a request as Justin and confirms the preview.</summary>
    public async Task<IReadOnlyList<OutgoingMessage>> CreateRequest(string args, Who? creator = null)
    {
        creator ??= Justin;
        var preview = await Send(creator, "/request create " + args);
        Assert.Contains("Vorschau", preview.Single().Text);
        return await Send(creator, "/yes");
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

public static class OutboxExtensions
{
    public static OutgoingMessage To(this IReadOnlyList<OutgoingMessage> outbox, string chatId) =>
        outbox.Single(m => m.ChatId == chatId);

    public static OutgoingMessage To(this IReadOnlyList<OutgoingMessage> outbox, Who person) => outbox.To(person.Id);

    public static string Reply(this IReadOnlyList<OutgoingMessage> outbox) => outbox.To(BotHarness.GroupChat).Text;
}
