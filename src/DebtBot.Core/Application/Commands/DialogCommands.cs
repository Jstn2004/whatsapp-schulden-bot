using DebtBot.Core.Domain;

namespace DebtBot.Core.Application.Commands;

/// <summary>/yes, /no and /edit answer the sender's open dialog.</summary>
public static class DialogCommands
{
    public static async Task YesAsync(CommandContext ctx)
    {
        var dialog = await ActiveDialogAsync(ctx, "Es gibt gerade nichts zu bestätigen.");
        if (dialog is null) return;

        switch (dialog.Type)
        {
            case DialogType.RequestPreview:
                await ctx.ClearDialogAsync();
                await RequestCommands.ConfirmCreateAsync(ctx, CommandContext.ReadDialog<RequestCommands.RequestDraft>(dialog));
                break;
            case DialogType.GroupDelete:
                await ctx.ClearDialogAsync();
                await GroupCommands.ConfirmDeleteAsync(ctx, CommandContext.ReadDialog<GroupCommands.DeleteGroupData>(dialog));
                break;
            case DialogType.GroupRemoveMember:
                await ctx.ClearDialogAsync();
                await GroupCommands.ConfirmRemoveAsync(ctx, CommandContext.ReadDialog<GroupCommands.RemoveMemberData>(dialog));
                break;
            case DialogType.PayChoice:
                throw new UserError("Antworte mit den Nummern aus der Liste, z. B. /pay 1 2 oder /pay all.");
        }
    }

    public static async Task NoAsync(CommandContext ctx)
    {
        var dialog = await ActiveDialogAsync(ctx, "Es gibt gerade nichts abzubrechen.");
        if (dialog is null) return;

        await ctx.ClearDialogAsync();
        ctx.Reply("❌ Abgebrochen.");
    }

    public static async Task EditAsync(CommandContext ctx)
    {
        var dialog = await ActiveDialogAsync(ctx, "Es gibt gerade nichts zum Bearbeiten.");
        if (dialog is null) return;
        if (dialog.Type != DialogType.RequestPreview) throw new UserError("/edit gibt es nur bei der Vorschau einer Anfrage.");

        await ctx.ClearDialogAsync();
        var draft = CommandContext.ReadDialog<RequestCommands.RequestDraft>(dialog);
        ctx.Reply($"✏️ Kopier den Befehl, pass ihn an und schick ihn neu:\n\n{draft.OriginalCommand}");
    }

    /// <summary>The sender's dialog, or null after replying that it expired. Throws if there is none.</summary>
    private static async Task<PendingDialog?> ActiveDialogAsync(CommandContext ctx, string noneMessage)
    {
        var dialog = await ctx.GetDialogAsync() ?? throw new UserError(noneMessage);
        if (dialog.ExpiresAt > ctx.Now) return dialog;

        await ctx.ClearDialogAsync();
        ctx.Reply(dialog.Type == DialogType.RequestPreview
            ? "⌛ Vorschau abgelaufen, bitte neu erstellen."
            : "⌛ Die Rückfrage ist abgelaufen, bitte den Befehl neu senden.");
        return null;
    }
}
