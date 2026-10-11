namespace DebtBot.Core.Messaging;

/// <summary>A message received from WhatsApp, as delivered by the gateway.</summary>
public sealed record IncomingMessage(
    string MessageId,
    string ChatId,
    bool IsGroup,
    string SenderId,
    string? SenderName,
    string Body,
    IReadOnlyList<Mention> Mentions,
    string BotId,
    DateTime Timestamp);

/// <summary>A person @mentioned in a message. <see cref="Token"/> is how it appears in the text ("@token").</summary>
public sealed record Mention(string Token, string Id, string? Name);

/// <summary>A message the bot wants to send.</summary>
public sealed record OutgoingMessage(string ChatId, string Text, IReadOnlyList<string> Mentions)
{
    /// <summary>Where to send <see cref="FallbackText"/> if sending to <see cref="ChatId"/> fails (e.g. private message not deliverable).</summary>
    public string? FallbackChatId { get; init; }

    public string? FallbackText { get; init; }
    public IReadOnlyList<string> FallbackMentions { get; init; } = [];

    /// <summary>Set when this message announces a request; its WhatsApp message ID is stored on the request.</summary>
    public RequestRef? AnnouncesRequest { get; init; }
}

public sealed record RequestRef(string ChatId, int Number);

/// <summary>Sends messages to WhatsApp. Keeps the business logic independent of the platform.</summary>
public interface IMessagingGateway
{
    /// <summary>Sends a message and returns its WhatsApp message ID. Throws if it could not be delivered.</summary>
    Task<string> SendAsync(string chatId, string text, IReadOnlyList<string> mentions, CancellationToken ct = default);
}
