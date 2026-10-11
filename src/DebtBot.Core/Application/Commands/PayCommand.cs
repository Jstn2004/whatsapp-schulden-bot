using DebtBot.Core.Domain;
using DebtBot.Core.Parsing;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Core.Application.Commands;

/// <summary>
/// /pay lists the sender's open shares as a numbered list; /pay 1 2 picks from that list.
/// Without an open list, numbers are request IDs (/pay 12 or /pay #12). A share is always paid in full.
/// </summary>
public static class PayCommand
{
    public sealed record PayChoiceData(List<int> ShareIds);

    private const string Usage = "So geht's: /pay · /pay <gruppe> · /pay 1 2 · /pay all · /pay #12";

    public static async Task HandleAsync(CommandContext ctx)
    {
        var args = ctx.Args;
        var dialog = await ctx.GetDialogAsync();
        var choice = dialog is { Type: DialogType.PayChoice } && dialog.ExpiresAt > ctx.Now
            ? CommandContext.ReadDialog<PayChoiceData>(dialog)
            : null;
        await ctx.ClearDialogAsync();

        if (args.Any(a => !a.IsText)) throw new UserError(Usage);
        var words = args.Select(a => a.Text.Trim().ToLowerInvariant()).ToList();

        if (words.Count == 0 || (words.Count == 1 && words[0] != "all" && !IsNumber(words[0])))
        {
            await ListAsync(ctx, words.FirstOrDefault());
            return;
        }

        if (words is ["all"])
        {
            var all = choice is not null ? await SharesByIdAsync(ctx, choice.ShareIds) : await OpenSharesAsync(ctx);
            if (all.Count == 0)
            {
                ctx.ReplyPrivately("Du hast keine offenen Zahlungen 🎉");
                return;
            }

            MarkPaid(ctx, all, []);
            return;
        }

        if (!words.All(IsNumber)) throw new UserError(Usage);

        // Plain numbers refer to the open list if there is one; "#12" is always a request ID.
        if (choice is not null && words.All(w => !w.StartsWith('#')))
        {
            var picked = new List<int>();
            foreach (var index in words.Select(int.Parse))
            {
                if (index < 1 || index > choice.ShareIds.Count) throw new UserError($"Die Nummer {index} gibt es in deiner Liste nicht.");
                picked.Add(choice.ShareIds[index - 1]);
            }

            MarkPaid(ctx, await SharesByIdAsync(ctx, picked.Distinct().ToList()), []);
            return;
        }

        var shares = new List<Share>();
        var notes = new List<string>();
        foreach (var number in words.Select(w => int.Parse(w.TrimStart('#'))).Distinct())
        {
            var (share, note) = await ShareForRequestAsync(ctx, number);
            if (share is not null) shares.Add(share);
            if (note is not null) notes.Add(note);
        }

        MarkPaid(ctx, shares, notes);
    }

    private static bool IsNumber(string word) => int.TryParse(word.TrimStart('#'), out var n) && n > 0 && word.Count(c => c == '#') <= 1;

    private static async Task ListAsync(CommandContext ctx, string? groupName)
    {
        var shares = await OpenSharesAsync(ctx);
        if (groupName is not null)
        {
            if (ctx.IsGroupChat) await ctx.FindGroupAsync(groupName);
            shares = shares.Where(s => s.Request.Group.NormalizedName == groupName).ToList();
        }

        if (shares.Count == 0)
        {
            ctx.ReplyPrivately("Du hast keine offenen Zahlungen 🎉");
            return;
        }

        // In a private chat shares can come from several groups, then the group name helps.
        var showGroup = shares.Select(s => s.Request.GroupId).Distinct().Count() > 1;
        var lines = shares.Select((s, i) =>
            $"{i + 1}. #{s.Request.Number} {s.Request.Reason}{(showGroup ? $" ({s.Request.Group.Name})" : "")} → "
            + $"{s.Request.CreatedBy.DisplayName} · {Text.Euro(s.Amount)} ({Text.Since(s.Request.CreatedAt, ctx.Now)})");

        await ctx.SetDialogAsync(DialogType.PayChoice, new PayChoiceData(shares.Select(s => s.Id).ToList()));
        ctx.ReplyPrivately("🧾 *Deine offenen Zahlungen*\n"
            + string.Join('\n', lines)
            + $"\n*Summe: {Text.Euro(shares.Sum(s => s.Amount))}*\n\n"
            + "Bezahlt? Antworte z. B. mit /pay 1 oder /pay 1 2 oder /pay all");
    }

    /// <summary>Finds the sender's share in request #number. Returns a note instead if there is nothing to pay.</summary>
    private static async Task<(Share? Share, string? Note)> ShareForRequestAsync(CommandContext ctx, int number)
    {
        var requests = await SharesQuery(ctx)
            .Where(s => s.DebtorId == ctx.Sender.Id && s.Request.Number == number)
            .Where(s => ctx.IsGroupChat ? s.Request.ChatId == ctx.ChatId : true)
            .ToListAsync();

        if (requests.Count == 0)
        {
            var exists = ctx.IsGroupChat && await ctx.Db.Requests.AnyAsync(r => r.ChatId == ctx.ChatId && r.Number == number);
            return (null, exists || !ctx.IsGroupChat ? $"Du hast bei #{number} nichts offen." : $"Anfrage #{number} gibt es hier nicht.");
        }

        // In a private chat the same number can exist in several chats.
        var open = requests.Where(s => s.Request.Status == RequestStatus.Open && s.Status == ShareStatus.Open).ToList();
        if (open.Count > 1) throw new UserError($"Es gibt mehrere offene Anfragen #{number} in verschiedenen Chats. Schick /pay und wähl aus der Liste.");

        var share = open.FirstOrDefault() ?? requests[0];
        return share.Request.Status == RequestStatus.Deleted
            ? (null, $"Anfrage #{number} wurde gelöscht – da ist nichts zu zahlen.")
            : (share, null);
    }

    private static void MarkPaid(CommandContext ctx, List<Share> shares, List<string> notes)
    {
        var lines = new List<string>(notes);
        foreach (var share in shares)
        {
            var request = share.Request;
            if (request.Status == RequestStatus.Deleted)
            {
                lines.Add($"🗑️ Anfrage #{request.Number} wurde gelöscht – da ist nichts zu zahlen.");
                continue;
            }

            switch (share.Status)
            {
                case ShareStatus.Paid:
                    lines.Add($"☑️ #{request.Number} war schon als bezahlt markiert.");
                    continue;
                case ShareStatus.Reported:
                    lines.Add($"⏳ #{request.Number} wartet schon auf Bestätigung von {request.CreatedBy.DisplayName}.");
                    continue;
            }

            if (request.Verify)
            {
                share.Status = ShareStatus.Reported;
                share.ReportedAt = ctx.Now;
                lines.Add($"⏳ #{request.Number} gemeldet – wartet auf Bestätigung von {request.CreatedBy.DisplayName}");
                ctx.SendPrivate(request.CreatedById,
                    $"💰 {ctx.Sender.DisplayName} meldet: {Text.Euro(share.Amount)} für #{request.Number} ({request.Reason}) bezahlt.",
                    request.ChatId);
                ctx.Audit("share.report", $"#{request.Number} {share.DebtorId}");
                continue;
            }

            share.Status = ShareStatus.Paid;
            share.PaidAt = ctx.Now;
            lines.Add($"✅ #{request.Number} als bezahlt markiert");
            ctx.Audit("share.pay", $"#{request.Number} {share.DebtorId} {share.Amount}");
            AnnouncePayment(ctx, request);
        }

        ctx.ReplyPrivately(string.Join('\n', lines));
    }

    private static void AnnouncePayment(CommandContext ctx, PaymentRequest request)
    {
        var paid = request.Shares.Count(s => s.Status == ShareStatus.Paid);
        if (paid < request.Shares.Count)
        {
            ctx.Send(new(request.ChatId, $"✅ {ctx.Sender.DisplayName} hat #{request.Number} bezahlt ({paid}/{request.Shares.Count})", []));
            return;
        }

        request.Status = RequestStatus.Completed;
        request.CompletedAt = ctx.Now;
        ctx.Audit("request.complete", $"#{request.Number}");
        ctx.Send(new(request.ChatId, $"✅ Anfrage #{request.Number} ({request.Reason}) ist komplett bezahlt!", []));
        ctx.SendPrivate(request.CreatedById,
            $"🎉 Alle haben für #{request.Number} ({request.Reason}) bezahlt – {Text.Euro(request.Shares.Sum(s => s.Amount))} sind zurück.",
            null);
    }

    private static IQueryable<Share> SharesQuery(CommandContext ctx) => ctx.Db.Shares
        .Include(s => s.Request).ThenInclude(r => r.CreatedBy)
        .Include(s => s.Request).ThenInclude(r => r.Group)
        .Include(s => s.Request).ThenInclude(r => r.Shares);

    /// <summary>The sender's open shares: in a group chat only from that chat, in a private chat from all chats.</summary>
    private static Task<List<Share>> OpenSharesAsync(CommandContext ctx) => SharesQuery(ctx)
        .Where(s => s.DebtorId == ctx.Sender.Id && s.Status == ShareStatus.Open && s.Request.Status == RequestStatus.Open)
        .Where(s => ctx.IsGroupChat ? s.Request.ChatId == ctx.ChatId : true)
        .OrderBy(s => s.Request.CreatedAt)
        .ToListAsync();

    private static async Task<List<Share>> SharesByIdAsync(CommandContext ctx, List<int> ids)
    {
        var shares = await SharesQuery(ctx).Where(s => ids.Contains(s.Id) && s.DebtorId == ctx.Sender.Id).ToListAsync();
        return shares.OrderBy(s => ids.IndexOf(s.Id)).ToList();
    }
}
