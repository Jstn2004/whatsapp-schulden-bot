using Microsoft.EntityFrameworkCore;
using static DebtBot.Tests.BotHarness;

namespace DebtBot.Tests;

public class EngineTests
{
    [Fact]
    public async Task Ignores_normal_messages_and_answers_unknown_commands()
    {
        await using var bot = new BotHarness();
        Assert.Empty(await bot.Send(Justin, "Hallo zusammen"));
        Assert.Contains("Den Befehl /dance kenne ich nicht", (await bot.Send(Justin, "/dance")).Reply());
    }

    [Fact]
    public async Task Help_lists_commands_and_explains_one()
    {
        await using var bot = new BotHarness();
        Assert.Contains("*/request create* – Geld anfordern", (await bot.Send(Justin, "/help")).Reply());

        var details = (await bot.Send(Justin, "/HELP group create")).Reply();
        Assert.Contains("```/group create <name> @Person …```", details);
        Assert.DoesNotContain("group add", details);
        Assert.Contains("*Mitglieder hinzufügen*", (await bot.Send(Justin, "/help group")).Reply());
    }

    [Fact]
    public async Task Rate_limit_warns_once_and_then_stays_silent()
    {
        await using var bot = new BotHarness(commandsPerMinute: 2);
        await bot.Send(Justin, "/help");
        await bot.Send(Justin, "/help");
        Assert.Contains("Zu viele Befehle", (await bot.Send(Justin, "/help")).Reply());
        Assert.Empty(await bot.Send(Justin, "/help"));

        bot.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Contains("Schulden-Bot", (await bot.Send(Justin, "/help")).Reply());
    }

    [Fact]
    public async Task Display_names_follow_the_sender()
    {
        await using var bot = new BotHarness();
        await bot.CreateWg();
        await bot.Send(Anna with { Name = "Anna B." }, "/group list");

        await using var db = bot.NewDb();
        Assert.Equal("Anna B.", (await db.People.FindAsync(Anna.Id))!.DisplayName);
    }

    [Fact]
    public async Task Failed_commands_save_nothing_and_changes_are_audited()
    {
        await using var bot = new BotHarness();
        await bot.Send(Justin, "/group create WG");
        await bot.CreateWg();

        await using var db = bot.NewDb();
        Assert.Equal(1, await db.Groups.CountAsync());
        Assert.Contains(await db.AuditLog.ToListAsync(), a => a.Action == "group.create" && a.PersonId == Justin.Id);
    }
}
