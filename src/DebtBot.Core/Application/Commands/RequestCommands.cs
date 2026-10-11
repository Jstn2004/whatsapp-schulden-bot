using DebtBot.Core.Domain;
using DebtBot.Core.Messaging;
using DebtBot.Core.Parsing;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Core.Application.Commands;

public static class RequestCommands
{
    public sealed record DraftShare(string PersonId, decimal Amount);

    /// <summary>A request waiting for /yes in the preview dialog.</summary>
    public sealed record RequestDraft(int GroupId, decimal Total, string Reason, decimal? CreatorShare, List<DraftShare> Shares, string OriginalCommand);

    private const string CreateUsage = "So geht's: /request create <betrag> \"<grund>\" [group=<name>] [includeme=yes|no] [@Person …]\n"
        + "Beispiel: /request create 40 \"Pizza Freitag\"";

    private static readonly HashSet<string> CreateOptions = ["group", "split", "includeme"];

    public static async Task HandleAsync(CommandContext ctx)
    {
        if (ctx.Args.FirstOrDefault() is not { Kind: TokenKind.Word } sub) throw new UserError(CreateUsage);
        switch (sub.Text.ToLowerInvariant())
        {
            case "create":
                await CreateAsync(ctx, ctx.Args.Skip(1).ToList());
                break;
            case "delete" or "info" or "remind" or "edit":
                throw new UserError($"/request {sub.Text.ToLowerInvariant()} kommt in einem späteren Update.");
            default:
                throw new UserError(CreateUsage);
        }
    }

    private static async Task CreateAsync(CommandContext ctx, List<Token> args)
    {
        ctx.RequireGroupChat();
        if (args.Count == 0 || !args[0].IsText) throw new UserError(CreateUsage);
        var total = Money.Parse(args[0].Text)
            ?? throw new UserError($"\"{args[0].Text}\" ist kein gültiger Betrag (z. B. 12,50).\n{CreateUsage}");

        var reasonParts = new List<string>();
        var options = new Dictionary<string, string>();
        var mentions = new List<string>();
        foreach (var token in args.Skip(1))
        {
            switch (token.Kind)
            {
                case TokenKind.Option when !CreateOptions.Contains(token.Key!):
                    throw new UserError($"Unbekannte Option {token.Key}=. Erlaubt: group=, includeme=\n{CreateUsage}");
                case TokenKind.Option when !options.TryAdd(token.Key!, token.Text):
                    throw new UserError($"Die Option {token.Key}= ist doppelt angegeben.");
                case TokenKind.Option:
                    break;
                case TokenKind.Mention:
                    mentions.Add(token.PersonId!);
                    break;
                case TokenKind.Word when token.Text.StartsWith('@'):
                    throw new UserError($"Ich kann {token.Text} nicht zuordnen. Erwähne Personen über die @-Auswahl von WhatsApp.");
                default:
                    reasonParts.Add(token.Text);
                    break;
            }
        }

        var reason = string.Join(' ', reasonParts).Trim();
        if (reason.Length == 0) throw new UserError($"Bitte gib einen Grund an.\n{CreateUsage}");
        if (reason.Length > 100) throw new UserError("Der Grund darf höchstens 100 Zeichen lang sein.");

        switch (options.GetValueOrDefault("split", "equal").ToLowerInvariant())
        {
            case "equal":
                break;
            case "custom":
                throw new UserError("Individuelle Beträge (split=custom) kommen in einem späteren Update. Vorerst wird gleichmäßig aufgeteilt.");
            default:
                throw new UserError("split= kann equal oder custom sein.");
        }

        var includeMe = options.GetValueOrDefault("includeme", "yes").ToLowerInvariant() switch
        {
            "yes" => true,
            "no" => false,
            _ => throw new UserError("includeme= kann yes oder no sein."),
        };

        var group = await ctx.GroupOrDefaultAsync(options.GetValueOrDefault("group"));
        if (group.Members.All(m => m.PersonId != ctx.Sender.Id))
        {
            throw new UserError($"Du bist nicht in *{group.Name}*, deshalb kannst du dort nichts anfordern.");
        }

        var debtorIds = mentions.Count > 0
            ? ValidateMentions(ctx, group, mentions)
            : group.Members.OrderBy(m => m.JoinedAt).Select(m => m.PersonId).Where(id => id != ctx.Sender.Id).ToList();
        if (debtorIds.Count == 0) throw new UserError("Mindestens eine andere Person muss zahlen.");

        // With includeme=yes the creator carries the last part, so leftover cents go to the first debtors.
        var parts = Splitter.Equal(total, debtorIds.Count + (includeMe ? 1 : 0));
        if (parts.Any(p => p == 0)) throw new UserError($"{Text.Euro(total)} ist zu wenig, um es auf {parts.Length} Personen aufzuteilen.");

        var draft = new RequestDraft(
            group.Id,
            total,
            reason,
            includeMe ? parts[^1] : null,
            debtorIds.Select((id, i) => new DraftShare(id, parts[i])).ToList(),
            ctx.Command.Raw);
        await ctx.SetDialogAsync(DialogType.RequestPreview, draft);
        ctx.Reply(PreviewText(group, draft, roundingApplied: parts.Distinct().Count() > 1));
    }

    private static List<string> ValidateMentions(CommandContext ctx, BotGroup group, List<string> mentions)
    {
        var names = group.Members.ToDictionary(m => m.PersonId, m => m.Person.DisplayName);
        var seen = new HashSet<string>();
        foreach (var id in mentions)
        {
            if (id == ctx.Message.BotId) throw new UserError("Ich zahle nichts, ich bin nur der Bot. 🤖");
            if (!names.ContainsKey(id))
            {
                throw new UserError($"{Text.Tag(id)} ist nicht in *{group.Name}*. Hinzufügen: /group add {group.Name} @Person");
            }

            if (!seen.Add(id)) throw new UserError($"Du hast {names[id]} doppelt erwähnt.");
        }

        // Mentioning yourself changes nothing: your own part is controlled by includeme.
        return mentions.Where(id => id != ctx.Sender.Id).ToList();
    }

    private static string PreviewText(BotGroup group, RequestDraft draft, bool roundingApplied)
    {
        var names = group.Members.ToDictionary(m => m.PersonId, m => m.Person.DisplayName);
        var lines = draft.Shares.Select(s => $"• {names[s.PersonId]}: {Text.Euro(s.Amount)}");
        var text = $"📝 *Vorschau Anfrage*\n{draft.Reason} · {Text.Euro(draft.Total)} · Gruppe {group.Name}\n{string.Join('\n', lines)}";
        if (draft.CreatorShare is { } own) text += $"\n(dein Anteil: {Text.Euro(own)})";
        if (roundingApplied) text += "\n_Rundung: Die übrigen Cent tragen die ersten Personen._";
        return text + "\n\n/yes erstellen · /edit neu eingeben · /no abbrechen";
    }

    /// <summary>Runs after /yes on the preview: saves the request, announces it and notifies every debtor.</summary>
    public static async Task ConfirmCreateAsync(CommandContext ctx, RequestDraft draft)
    {
        var group = await ctx.Db.Groups
            .Include(g => g.Members).ThenInclude(m => m.Person)
            .FirstOrDefaultAsync(g => g.Id == draft.GroupId && g.DeletedAt == null)
            ?? throw new UserError("Die Gruppe gibt es nicht mehr. Bitte neu erstellen.");
        var chat = await ctx.Db.Chats.FindAsync(group.ChatId) ?? throw new InvalidOperationException($"Unknown chat {group.ChatId}");

        var request = new PaymentRequest
        {
            ChatId = group.ChatId,
            Number = chat.NextRequestNumber++,
            GroupId = group.Id,
            Group = group,
            CreatedById = ctx.Sender.Id,
            CreatedBy = ctx.Sender,
            TotalAmount = draft.Total,
            CreatorShare = draft.CreatorShare,
            Reason = draft.Reason,
            SplitMode = SplitMode.Equal,
            Verify = group.Verify,
            Status = RequestStatus.Open,
            CreatedAt = ctx.Now,
        };
        foreach (var draftShare in draft.Shares)
        {
            var debtor = await ctx.Db.People.FindAsync(draftShare.PersonId) ?? throw new InvalidOperationException($"Unknown person {draftShare.PersonId}");
            request.Shares.Add(new Share { DebtorId = debtor.Id, Debtor = debtor, Amount = draftShare.Amount, Status = ShareStatus.Open });
        }

        ctx.Db.Requests.Add(request);
        ctx.Audit("request.create", $"#{request.Number} {request.Reason} {request.TotalAmount} in {group.Name}");

        ctx.Send(new OutgoingMessage(request.ChatId, AnnouncementText(request), request.Shares.Select(s => s.DebtorId).ToList())
        {
            AnnouncesRequest = new RequestRef(request.ChatId, request.Number),
        });
        foreach (var share in request.Shares)
        {
            ctx.SendPrivate(share.DebtorId, NotificationText(request, share), request.ChatId);
        }
    }

    private static string AnnouncementText(PaymentRequest request)
    {
        var lines = request.Shares.Select(s => $"❌ {Text.Tag(s.DebtorId)} · {Text.Euro(s.Amount)}");
        var text = $"💸 *Anfrage #{request.Number}: {request.Reason}*\n"
            + $"{request.CreatedBy.DisplayName} hat {Text.Euro(request.TotalAmount)} ausgelegt · Gruppe {request.Group.Name}\n\n"
            + string.Join('\n', lines);
        if (request.CreatorShare is { } own) text += $"\n(Anteil von {request.CreatedBy.DisplayName}: {Text.Euro(own)})";
        return text + $"\n\nBezahlt? → /pay {request.Number}";
    }

    private static string NotificationText(PaymentRequest request, Share share)
    {
        var people = request.Shares.Count + (request.CreatorShare is null ? 0 : 1);
        var mode = request.Verify ? "mit Prüfung" : "Vertrauensbasis";
        return "💸 *Neue Zahlungsaufforderung*\n"
            + $"Du schuldest *{request.CreatedBy.DisplayName} {Text.Euro(share.Amount)}* für *{request.Reason}*\n"
            + $"Gruppe: {request.Group.Name} · Anfrage #{request.Number} · Gesamt {Text.Euro(request.TotalAmount)} ({people} Personen)\n"
            + $"Modus: {mode}\n\n"
            + $"Bezahlt? Antworte mit /pay {request.Number}";
    }
}
