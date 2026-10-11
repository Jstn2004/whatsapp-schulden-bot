using DebtBot.Core.Domain;
using DebtBot.Core.Parsing;
using Microsoft.EntityFrameworkCore;

namespace DebtBot.Core.Application.Commands;

public static class GroupCommands
{
    public sealed record RemoveMemberData(int GroupId, string PersonId);

    public sealed record DeleteGroupData(int GroupId);

    private const string Usage = "So geht's: /group create|add|remove|rename|default|admin|settings|delete|list|info\nDetails: /help group";

    public static async Task HandleAsync(CommandContext ctx)
    {
        ctx.RequireGroupChat();
        if (ctx.Args.FirstOrDefault() is not { Kind: TokenKind.Word } sub) throw new UserError(Usage);

        var args = ctx.Args.Skip(1).ToList();
        switch (sub.Text.ToLowerInvariant())
        {
            case "create": await CreateAsync(ctx, args); break;
            case "add": await AddAsync(ctx, args); break;
            case "remove": await RemoveAsync(ctx, args); break;
            case "rename": await RenameAsync(ctx, args); break;
            case "default": await SetDefaultAsync(ctx, args); break;
            case "admin": await ToggleAdminAsync(ctx, args); break;
            case "settings": await SettingsAsync(ctx, args); break;
            case "delete": await DeleteAsync(ctx, args); break;
            case "list": await ListAsync(ctx); break;
            case "info": await InfoAsync(ctx, args); break;
            default: throw new UserError(Usage);
        }
    }

    // ---- Commands ----

    private static async Task CreateAsync(CommandContext ctx, List<Token> args)
    {
        const string usage = "So geht's: /group create <name> @Person …\nBeispiel: /group create WG @Anna @Ben";
        if (args.Count == 0 || !args[0].IsText) throw new UserError(usage);

        var name = ValidateName(args[0].Text);
        var others = MentionedIds(ctx, args.Skip(1), usage).Where(id => id != ctx.Sender.Id).ToList();
        if (others.Count == 0) throw new UserError("Eine Gruppe braucht mindestens 2 Mitglieder (inkl. dir). Erwähne die anderen mit @.\n" + usage);
        await EnsureNameFreeAsync(ctx, name, exceptGroupId: null);

        var group = new BotGroup
        {
            ChatId = ctx.ChatId,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            CreatedById = ctx.Sender.Id,
            CreatedAt = ctx.Now,
            IsDefault = !await ctx.GroupsInChat.AnyAsync(),
        };
        group.Members.Add(await NewMemberAsync(ctx, ctx.Sender.Id, GroupRole.Admin));
        foreach (var id in others) group.Members.Add(await NewMemberAsync(ctx, id, GroupRole.Member));
        ctx.Db.Groups.Add(group);

        ctx.Audit("group.create", $"{name}: {string.Join(", ", group.Members.Select(m => m.PersonId))}");
        ctx.Reply("✅ Gruppe erstellt\n\n" + await OverviewAsync(ctx, group));
    }

    private static async Task AddAsync(CommandContext ctx, List<Token> args)
    {
        const string usage = "So geht's: /group add <gruppe> @Person …";
        var group = await AdminGroupAsync(ctx, args, usage);
        var ids = MentionedIds(ctx, args.Skip(1), usage);
        if (ids.Count == 0) throw new UserError(usage);

        var added = new List<string>();
        var already = new List<string>();
        foreach (var id in ids)
        {
            var existing = group.Members.FirstOrDefault(m => m.PersonId == id);
            if (existing is not null)
            {
                already.Add(existing.Person.DisplayName);
                continue;
            }

            var member = await NewMemberAsync(ctx, id, GroupRole.Member);
            group.Members.Add(member);
            added.Add(member.Person.DisplayName);
        }

        if (added.Count > 0) ctx.Audit("group.add", $"{group.Name}: {string.Join(", ", added)}");
        var reply = added.Count > 0 ? $"👥 Zu *{group.Name}* hinzugefügt: {string.Join(", ", added)}" : $"👥 Niemand Neues für *{group.Name}*.";
        if (already.Count > 0) reply += $"\nSchon dabei: {string.Join(", ", already)}";
        ctx.Reply(reply);
    }

    private static async Task RemoveAsync(CommandContext ctx, List<Token> args)
    {
        const string usage = "So geht's: /group remove <gruppe> @Person";
        var group = await AdminGroupAsync(ctx, args, usage);
        var ids = MentionedIds(ctx, args.Skip(1), usage);
        if (ids.Count != 1) throw new UserError(usage);

        var member = MemberOf(group, ids[0]);
        EnsureRemovable(group, member);

        // People with open money matters are only removed after confirmation.
        var openShares = await OpenSharesOfAsync(ctx, group.Id, member.PersonId);
        var openRequests = await OpenRequestsByAsync(ctx, group.Id, member.PersonId);
        if (openShares.Count > 0 || openRequests.Count > 0)
        {
            var lines = openShares.Select(s => $"• schuldet {Text.Euro(s.Amount)} für #{s.Request.Number} {s.Request.Reason}")
                .Concat(openRequests.Select(r => $"• hat die offene Anfrage #{r.Number} {r.Reason}"));
            await ctx.SetDialogAsync(DialogType.GroupRemoveMember, new RemoveMemberData(group.Id, member.PersonId));
            ctx.Reply($"⚠️ *{member.Person.DisplayName}* hat in *{group.Name}* noch Offenes:\n{string.Join('\n', lines)}\n\n"
                + "Diese Posten bleiben gültig und zahlbar.\n/yes trotzdem entfernen · /no abbrechen");
            return;
        }

        RemoveMember(ctx, group, member);
    }

    /// <summary>Runs after /yes on the warning about open items.</summary>
    public static async Task ConfirmRemoveAsync(CommandContext ctx, RemoveMemberData data)
    {
        var group = await ctx.GroupsInChat.FirstOrDefaultAsync(g => g.Id == data.GroupId)
            ?? throw new UserError("Die Gruppe gibt es nicht mehr.");
        RequireAdmin(ctx, group);
        var member = MemberOf(group, data.PersonId);
        EnsureRemovable(group, member);
        RemoveMember(ctx, group, member);
    }

    private static async Task RenameAsync(CommandContext ctx, List<Token> args)
    {
        const string usage = "So geht's: /group rename <gruppe> <neuerName>";
        var group = await AdminGroupAsync(ctx, args, usage);
        if (args.Count != 2 || !args[1].IsText) throw new UserError(usage);

        var newName = ValidateName(args[1].Text);
        await EnsureNameFreeAsync(ctx, newName, exceptGroupId: group.Id);
        ctx.Audit("group.rename", $"{group.Name} -> {newName}");
        var oldName = group.Name;
        group.Name = newName;
        group.NormalizedName = newName.ToLowerInvariant();
        ctx.Reply($"✏️ *{oldName}* heißt jetzt *{newName}*.");
    }

    private static async Task SetDefaultAsync(CommandContext ctx, List<Token> args)
    {
        var group = await AdminGroupAsync(ctx, args, "So geht's: /group default <gruppe>");
        foreach (var other in await ctx.GroupsInChat.ToListAsync()) other.IsDefault = other.Id == group.Id;
        ctx.Audit("group.default", group.Name);
        ctx.Reply($"⭐ *{group.Name}* ist jetzt die Standard-Gruppe.");
    }

    private static async Task ToggleAdminAsync(CommandContext ctx, List<Token> args)
    {
        const string usage = "So geht's: /group admin <gruppe> @Person";
        var group = await AdminGroupAsync(ctx, args, usage);
        var ids = MentionedIds(ctx, args.Skip(1), usage);
        if (ids.Count != 1) throw new UserError(usage);

        var member = MemberOf(group, ids[0]);
        if (member.Role == GroupRole.Admin)
        {
            if (group.Members.Count(m => m.Role == GroupRole.Admin) == 1)
            {
                throw new UserError($"{member.Person.DisplayName} ist der letzte Admin von *{group.Name}*. Mach erst jemand anderen zum Admin.");
            }

            member.Role = GroupRole.Member;
            ctx.Audit("group.admin.revoke", $"{group.Name}: {member.PersonId}");
            ctx.Reply($"{member.Person.DisplayName} ist kein Admin von *{group.Name}* mehr.");
            return;
        }

        member.Role = GroupRole.Admin;
        ctx.Audit("group.admin.grant", $"{group.Name}: {member.PersonId}");
        ctx.Reply($"👑 {member.Person.DisplayName} ist jetzt Admin von *{group.Name}*.");
    }

    private static async Task SettingsAsync(CommandContext ctx, List<Token> args)
    {
        const string usage = "So geht's: /group settings <gruppe> [remind=<tage>] [repeat=on|off] [maxremind=<n>]";
        if (args.Count == 0 || !args[0].IsText) throw new UserError(usage);
        var group = await ctx.FindGroupAsync(args[0].Text);
        var options = args.Skip(1).ToList();

        if (options.Count == 0)
        {
            MemberOf(group, ctx.Sender.Id, $"Du bist nicht in *{group.Name}*.");
            ctx.Reply($"⚙️ *Einstellungen {group.Name}*\n{SettingsText(group)}");
            return;
        }

        RequireAdmin(ctx, group);
        if (options.Any(o => o.Kind != TokenKind.Option)) throw new UserError(usage);

        // Validate everything first, so a typo in one option changes nothing.
        var changes = new List<Action>();
        foreach (var option in options)
        {
            var value = option.Text.ToLowerInvariant();
            switch (option.Key)
            {
                case "verify" when value == "off":
                    changes.Add(() => group.Verify = false);
                    break;
                case "verify" when value == "on":
                    throw new UserError("Der Prüfmodus (verify=on) kommt in einem späteren Update.");
                case "remind" when int.TryParse(value, out var days) && days is >= 1 and <= 30:
                    changes.Add(() => group.RemindAfterDays = days);
                    break;
                case "repeat" when value is "on" or "off":
                    changes.Add(() => group.RemindRepeat = value == "on");
                    break;
                case "maxremind" when int.TryParse(value, out var max) && max is >= 0 and <= 100:
                    changes.Add(() => group.MaxReminders = max);
                    break;
                default:
                    throw new UserError($"Ungültige Einstellung: {option.Key}={option.Text}\n"
                        + "Erlaubt: remind=1–30, repeat=on|off, maxremind=0–100 (0 = unbegrenzt)");
            }
        }

        changes.ForEach(change => change());
        ctx.Audit("group.settings", $"{group.Name}: {string.Join(' ', options.Select(o => $"{o.Key}={o.Text}"))}");
        ctx.Reply($"⚙️ *Einstellungen {group.Name}* gespeichert\n{SettingsText(group)}");
    }

    private static async Task DeleteAsync(CommandContext ctx, List<Token> args)
    {
        var group = await AdminGroupAsync(ctx, args, "So geht's: /group delete <gruppe>");
        var openRequests = await ctx.Db.Requests.CountAsync(r => r.GroupId == group.Id && r.Status == RequestStatus.Open);

        await ctx.SetDialogAsync(DialogType.GroupDelete, new DeleteGroupData(group.Id));
        var warning = openRequests > 0
            ? $"\nDabei werden {Text.Count(openRequests, "offene Anfrage", "offene Anfragen")} gelöscht, die Schuldner werden benachrichtigt."
            : "";
        ctx.Reply($"🗑️ Gruppe *{group.Name}* wirklich löschen?{warning}\n\n/yes löschen · /no abbrechen");
    }

    /// <summary>Runs after /yes on the delete question.</summary>
    public static async Task ConfirmDeleteAsync(CommandContext ctx, DeleteGroupData data)
    {
        var group = await ctx.GroupsInChat.FirstOrDefaultAsync(g => g.Id == data.GroupId)
            ?? throw new UserError("Die Gruppe gibt es nicht mehr.");
        RequireAdmin(ctx, group);

        var openRequests = await ctx.Db.Requests
            .Include(r => r.Shares)
            .Include(r => r.CreatedBy)
            .Where(r => r.GroupId == group.Id && r.Status == RequestStatus.Open)
            .ToListAsync();
        foreach (var request in openRequests)
        {
            request.Status = RequestStatus.Deleted;
            request.DeletedAt = ctx.Now;
            foreach (var share in request.Shares.Where(s => s.Status != ShareStatus.Paid))
            {
                ctx.SendPrivate(share.DebtorId,
                    $"🗑️ Anfrage #{request.Number} ({request.Reason}) von {request.CreatedBy.DisplayName} wurde gelöscht – du musst nichts mehr zahlen.",
                    request.ChatId);
            }
        }

        group.DeletedAt = ctx.Now;
        var reply = $"🗑️ Gruppe *{group.Name}* gelöscht.";
        if (group.IsDefault)
        {
            group.IsDefault = false;
            var next = await ctx.GroupsInChat.Where(g => g.Id != group.Id).OrderBy(g => g.CreatedAt).FirstOrDefaultAsync();
            if (next is not null)
            {
                next.IsDefault = true;
                reply += $"\n⭐ *{next.Name}* ist jetzt die Standard-Gruppe.";
            }
        }

        ctx.Audit("group.delete", $"{group.Name}, {openRequests.Count} open requests deleted");
        ctx.Reply(reply);
    }

    private static async Task ListAsync(CommandContext ctx)
    {
        var groups = await ctx.GroupsInChat.OrderBy(g => g.CreatedAt).ToListAsync();
        if (groups.Count == 0) throw new UserError("Hier gibt es noch keine Gruppe. Erstell eine:\n/group create WG @Anna @Ben");

        var lines = new List<string>();
        foreach (var group in groups)
        {
            var open = await ctx.Db.Requests.CountAsync(r => r.GroupId == group.Id && r.Status == RequestStatus.Open);
            var star = group.IsDefault ? " ⭐" : "";
            lines.Add($"• *{group.Name}*{star} · {Text.Count(group.Members.Count, "Mitglied", "Mitglieder")} · {open} offen");
        }

        ctx.Reply($"👥 *Gruppen in diesem Chat*\n━━━━━━━━━━━━━━\n{string.Join('\n', lines)}\n\nDetails: /group info <gruppe>");
    }

    private static async Task InfoAsync(CommandContext ctx, List<Token> args)
    {
        if (args.Count > 1 || args.Any(a => !a.IsText)) throw new UserError("So geht's: /group info [gruppe]");
        var group = await ctx.GroupOrDefaultAsync(args.FirstOrDefault()?.Text);
        ctx.Reply(await OverviewAsync(ctx, group));
    }

    // ---- Helpers ----

    public static async Task<string> OverviewAsync(CommandContext ctx, BotGroup group)
    {
        var members = group.Members
            .OrderBy(m => m.JoinedAt).ThenBy(m => m.Person.DisplayName)
            .Select(m => $"- {m.Person.DisplayName}{(m.Role == GroupRole.Admin ? " 👑" : "")}");

        // SQLite cannot sum decimals, so the open amount is added up in memory.
        var openRequests = group.Id == 0
            ? []
            : await ctx.Db.Requests.Include(r => r.Shares)
                .Where(r => r.GroupId == group.Id && r.Status == RequestStatus.Open)
                .ToListAsync();
        var openAmount = openRequests.SelectMany(r => r.Shares).Where(s => s.Status != ShareStatus.Paid).Sum(s => s.Amount);

        return $"👥 *{group.Name}*{(group.IsDefault ? " ⭐" : "")}\n━━━━━━━━━━━━━━\n"
            + $"*Mitglieder ({group.Members.Count})*\n{string.Join('\n', members)}\n\n"
            + $"*Einstellungen*\n{SettingsText(group)}\n\n"
            + $"*Offen:* {Text.Count(openRequests.Count, "Anfrage", "Anfragen")} · {Text.Euro(openAmount)}";
    }

    private static string SettingsText(BotGroup group)
    {
        var verify = group.Verify ? "an (Zahlungen müssen bestätigt werden)" : "aus (Vertrauensbasis)";
        var repeat = !group.RemindRepeat ? "einmalig"
            : group.MaxReminders == 0 ? "wiederholt, unbegrenzt"
            : $"wiederholt, max. {group.MaxReminders}×";
        return $"Prüfung: {verify}\nErinnerung: nach {Text.Days(group.RemindAfterDays)}, {repeat}";
    }

    private static string ValidateName(string raw)
    {
        var name = raw.Trim();
        if (name.Length is < 1 or > 30) throw new UserError("Der Gruppenname muss 1–30 Zeichen lang sein.");
        if (name[0] is '@' or '#' or '/' || name.Contains('='))
        {
            throw new UserError("Der Gruppenname darf nicht mit @, # oder / beginnen und kein = enthalten.");
        }

        return name;
    }

    private static async Task EnsureNameFreeAsync(CommandContext ctx, string name, int? exceptGroupId)
    {
        var normalized = name.ToLowerInvariant();
        if (await ctx.GroupsInChat.AnyAsync(g => g.NormalizedName == normalized && g.Id != exceptGroupId))
        {
            throw new UserError($"Eine Gruppe *{name}* gibt es hier schon.");
        }
    }

    /// <summary>The IDs of all @mentioned people. Plain words are rejected so typos don't go unnoticed.</summary>
    public static List<string> MentionedIds(CommandContext ctx, IEnumerable<Token> tokens, string usage)
    {
        var ids = new List<string>();
        foreach (var token in tokens)
        {
            if (token.Kind != TokenKind.Mention)
            {
                throw new UserError(token.Text.StartsWith('@')
                    ? $"Ich kann {token.Text} nicht zuordnen. Erwähne Personen über die @-Auswahl von WhatsApp.\n{usage}"
                    : usage);
            }

            if (token.PersonId == ctx.Message.BotId) throw new UserError("Mich selbst kannst du nicht hinzufügen. 🤖");
            if (!ids.Contains(token.PersonId!)) ids.Add(token.PersonId!);
        }

        return ids;
    }

    private static async Task<GroupMember> NewMemberAsync(CommandContext ctx, string personId, GroupRole role)
    {
        var person = await ctx.Db.People.FindAsync(personId) ?? throw new InvalidOperationException($"Unknown person {personId}");
        return new GroupMember { PersonId = personId, Person = person, Role = role, JoinedAt = ctx.Now };
    }

    /// <summary>Finds the group named in the first argument and checks that the sender is one of its admins.</summary>
    private static async Task<BotGroup> AdminGroupAsync(CommandContext ctx, List<Token> args, string usage)
    {
        if (args.Count == 0 || !args[0].IsText) throw new UserError(usage);
        var group = await ctx.FindGroupAsync(args[0].Text);
        RequireAdmin(ctx, group);
        return group;
    }

    private static void RequireAdmin(CommandContext ctx, BotGroup group)
    {
        if (group.Members.Any(m => m.PersonId == ctx.Sender.Id && m.Role == GroupRole.Admin)) return;
        throw new UserError($"Das dürfen nur Admins von *{group.Name}*.");
    }

    private static GroupMember MemberOf(BotGroup group, string personId, string? error = null) =>
        group.Members.FirstOrDefault(m => m.PersonId == personId)
        ?? throw new UserError(error ?? $"Diese Person ist nicht in *{group.Name}*.");

    private static void EnsureRemovable(BotGroup group, GroupMember member)
    {
        if (group.Members.Count == 1)
        {
            throw new UserError($"Dann wäre *{group.Name}* leer. Lösch die Gruppe stattdessen: /group delete {group.Name}");
        }

        if (member.Role == GroupRole.Admin && group.Members.Count(m => m.Role == GroupRole.Admin) == 1)
        {
            throw new UserError($"{member.Person.DisplayName} ist der letzte Admin von *{group.Name}*. Mach erst jemand anderen zum Admin: /group admin {group.Name} @Person");
        }
    }

    private static void RemoveMember(CommandContext ctx, BotGroup group, GroupMember member)
    {
        group.Members.Remove(member);
        ctx.Db.GroupMembers.Remove(member);
        ctx.Audit("group.remove", $"{group.Name}: {member.PersonId}");
        ctx.Reply($"👋 {member.Person.DisplayName} ist nicht mehr in *{group.Name}*.");
    }

    private static Task<List<Share>> OpenSharesOfAsync(CommandContext ctx, int groupId, string personId) =>
        ctx.Db.Shares.Include(s => s.Request)
            .Where(s => s.DebtorId == personId && s.Status != ShareStatus.Paid
                && s.Request.GroupId == groupId && s.Request.Status == RequestStatus.Open)
            .ToListAsync();

    private static Task<List<PaymentRequest>> OpenRequestsByAsync(CommandContext ctx, int groupId, string personId) =>
        ctx.Db.Requests
            .Where(r => r.CreatedById == personId && r.GroupId == groupId && r.Status == RequestStatus.Open)
            .ToListAsync();
}
