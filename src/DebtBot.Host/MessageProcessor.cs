using System.Threading.Channels;
using DebtBot.Core.Application;
using DebtBot.Core.Messaging;
using DebtBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Host;

/// <summary>
/// Handles incoming messages one at a time. Processing strictly in order means two commands
/// can never change the same data at once, which keeps the state consistent without locking.
/// </summary>
public sealed class MessageProcessor(
    Channel<IncomingMessage> queue,
    IServiceScopeFactory scopes,
    ILogger<MessageProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            var gateway = scope.ServiceProvider.GetRequiredService<IMessagingGateway>();
            IReadOnlyList<OutgoingMessage> outbox;
            try
            {
                outbox = await scope.ServiceProvider.GetRequiredService<BotEngine>().HandleAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to handle message {MessageId} in {ChatId}: {Body}", message.MessageId, message.ChatId, message.Body);
                outbox = [new OutgoingMessage(message.ChatId, "⚠️ Da ist etwas schiefgelaufen. Versuch es bitte nochmal.", [])];
            }

            foreach (var outgoing in outbox)
            {
                await SendAsync(gateway, scope.ServiceProvider.GetRequiredService<BotDbContext>(), outgoing, stoppingToken);
            }
        }
    }

    private async Task SendAsync(IMessagingGateway gateway, BotDbContext db, OutgoingMessage outgoing, CancellationToken ct)
    {
        string messageId;
        try
        {
            messageId = await gateway.SendAsync(outgoing.ChatId, outgoing.Text, outgoing.Mentions, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (outgoing.FallbackChatId is null || outgoing.FallbackText is null)
            {
                logger.LogError(ex, "Could not send message to {ChatId}", outgoing.ChatId);
                return;
            }

            // Typically a private message that could not be delivered: post it in the group instead.
            logger.LogWarning(ex, "Could not send to {ChatId}, falling back to {FallbackChatId}", outgoing.ChatId, outgoing.FallbackChatId);
            try
            {
                await gateway.SendAsync(outgoing.FallbackChatId, outgoing.FallbackText, outgoing.FallbackMentions, ct);
            }
            catch (Exception fallbackEx) when (fallbackEx is not OperationCanceledException)
            {
                logger.LogError(fallbackEx, "Fallback to {FallbackChatId} failed as well", outgoing.FallbackChatId);
            }

            return;
        }

        if (outgoing.AnnouncesRequest is { } announced)
        {
            await db.Requests
                .Where(r => r.ChatId == announced.ChatId && r.Number == announced.Number)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.MessageId, messageId), ct);
        }
    }
}
