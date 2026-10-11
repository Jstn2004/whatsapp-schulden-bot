namespace DebtBot.Core.Application.Commands;

public static class HelpCommand
{
    private sealed record Entry(string Command, string Syntax, string Description, string Example);

    private static readonly Entry[] Entries =
    [
        new("group create", "/group create <name> @Person …", "Gruppe erstellen", "/group create WG @Anna @Ben"),
        new("group add", "/group add <gruppe> @Person …", "Mitglieder hinzufügen", "/group add WG @Chris"),
        new("group remove", "/group remove <gruppe> @Person", "Mitglied entfernen", "/group remove WG @Chris"),
        new("group rename", "/group rename <gruppe> <neuerName>", "Umbenennen", "/group rename WG \"WG Küche\""),
        new("group default", "/group default <gruppe>", "Als Standard-Gruppe setzen", "/group default WG"),
        new("group admin", "/group admin <gruppe> @Person", "Admin-Rolle geben oder entziehen", "/group admin WG @Anna"),
        new("group settings", "/group settings <gruppe> [remind=<tage>] [repeat=on|off] [maxremind=<n>]", "Einstellungen anzeigen oder ändern", "/group settings WG remind=5 maxremind=3"),
        new("group delete", "/group delete <gruppe>", "Gruppe löschen", "/group delete WG"),
        new("group list", "/group list", "Alle Gruppen", "/group list"),
        new("group info", "/group info [gruppe]", "Details einer Gruppe", "/group info WG"),
        new("request create", "/request create <betrag> \"<grund>\" [group=<name>] [includeme=yes|no] [@Person …]", "Geld anfordern, gleichmäßig aufgeteilt", "/request create 40 \"Pizza Freitag\" @Anna @Ben"),
        new("pay", "/pay [gruppe] · /pay <nr …> · /pay all · /pay #<id>", "Offene Zahlungen anzeigen und als bezahlt markieren", "/pay #12"),
        new("yes", "/yes · /no · /edit", "Auf Rückfragen des Bots antworten", "/yes"),
    ];

    public static Task HandleAsync(CommandContext ctx)
    {
        var topic = string.Join(' ', ctx.Args.Where(a => a.IsText).Select(a => a.Text.TrimStart('/').ToLowerInvariant()));
        ctx.Reply(topic.Length == 0 ? Overview() : Details(topic));
        return Task.CompletedTask;
    }

    private static string Overview()
    {
        var lines = Entries.Select(e => $"*/{e.Command}* – {e.Description}");
        return "💸 *Schulden-Bot*\n━━━━━━━━━━━━━━\n"
            + string.Join('\n', lines)
            + "\n\nDetails: z. B. /help request create";
    }

    private static string Details(string topic)
    {
        // "/help group" lists all group commands, "/help group create" shows just one.
        var matches = Entries.Where(e => e.Command == topic || e.Command.StartsWith(topic + " ")).ToList();
        if (topic is "no" or "edit") matches = Entries.Where(e => e.Command == "yes").ToList();
        if (matches.Count == 0) throw new UserError($"Zu \"{topic}\" gibt es keine Hilfe. Alle Befehle: /help");

        return string.Join("\n\n", matches.Select(e => $"*{e.Description}*\n```{e.Syntax}```\nBeispiel: {e.Example}"));
    }
}
