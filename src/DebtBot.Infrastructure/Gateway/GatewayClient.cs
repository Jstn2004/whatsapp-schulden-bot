using System.Net.Http.Json;
using DebtBot.Core.Messaging;

namespace DebtBot.Infrastructure.Gateway;

public sealed class GatewayOptions
{
    public string Url { get; set; } = "http://localhost:3000";

    /// <summary>Shared secret sent as X-Gateway-Token; empty disables the check.</summary>
    public string Token { get; set; } = "";
}

/// <summary>Sends messages through the Node.js WhatsApp gateway.</summary>
public sealed class GatewayClient(HttpClient http) : IMessagingGateway
{
    private sealed record SendRequest(string ChatId, string Text, IReadOnlyList<string> Mentions);

    private sealed record SendResponse(string MessageId);

    public async Task<string> SendAsync(string chatId, string text, IReadOnlyList<string> mentions, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("/api/send", new SendRequest(chatId, text, mentions), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gateway answered {(int)response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<SendResponse>(ct);
        return result?.MessageId ?? throw new HttpRequestException("Gateway returned no message ID.");
    }
}
