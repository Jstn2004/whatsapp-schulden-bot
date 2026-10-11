using DebtBot.Core.Domain;
using Microsoft.EntityFrameworkCore;
using static DebtBot.Tests.BotHarness;

namespace DebtBot.Tests;

public class RequestAndPayTests : IAsyncLifetime
{
    private readonly BotHarness _bot = new();

    public async Task InitializeAsync() => await _bot.CreateWg();

    public async Task DisposeAsync() => await _bot.DisposeAsync();

    [Fact]
    public async Task Request_in_group_of_four_creates_three_shares_of_ten_euros()
    {
        var preview = (await _bot.Send(Justin, "/request create 40 \"Pizza Freitag\"")).Reply();
        Assert.Contains("Pizza Freitag · 40,00 € · Gruppe WG", preview);
        Assert.Contains("• Anna: 10,00 €", preview);
        Assert.Contains("(dein Anteil: 10,00 €)", preview);
        Assert.Contains("/yes erstellen · /edit neu eingeben · /no abbrechen", preview);

        var created = await _bot.Send(Justin, "/yes");

        var announcement = created.To(GroupChat);
        Assert.Contains("💸 *Anfrage #1: Pizza Freitag*", announcement.Text);
        Assert.Contains($"❌ @{Anna.Token} · 10,00 €", announcement.Text);
        Assert.Equal([Anna.Id, Ben.Id, Chris.Id], announcement.Mentions);
        Assert.Equal(new(GroupChat, 1), announcement.AnnouncesRequest);

        // Exactly one private message per debtor.
        Assert.Equal(4, created.Count);
        var dm = created.To(Anna);
        Assert.Contains("Du schuldest *Justin 10,00 €* für *Pizza Freitag*", dm.Text);
        Assert.Contains("Gruppe: WG · Anfrage #1 · Gesamt 40,00 € (4 Personen)", dm.Text);
        Assert.Contains("Antworte mit /pay 1", dm.Text);
        Assert.Equal(GroupChat, dm.FallbackChatId);

        await using var db = _bot.NewDb();
        var request = await db.Requests.Include(r => r.Shares).SingleAsync();
        Assert.Equal(RequestStatus.Open, request.Status);
        Assert.All(request.Shares, s => Assert.Equal(10m, s.Amount));
    }

    [Fact]
    public async Task Leftover_cents_go_to_the_first_debtors()
    {
        await _bot.CreateRequest("10 Kino includeme=no");

        await using var db = _bot.NewDb();
        var amounts = await db.Shares.OrderBy(s => s.Id).Select(s => s.Amount).ToListAsync();
        Assert.Equal([3.34m, 3.33m, 3.33m], amounts);
    }

    [Fact]
    public async Task Mentions_pick_the_debtors()
    {
        var preview = (await _bot.Send(Justin, "/request create 30 Kino @Anna @Ben")).Reply();
        Assert.Contains("• Anna: 10,00 €\n• Ben: 10,00 €\n(dein Anteil: 10,00 €)", preview);
        Assert.DoesNotContain("Chris", preview);
    }

    [Fact]
    public async Task Request_validation()
    {
        Assert.Contains("kein gültiger Betrag", (await _bot.Send(Justin, "/request create zehn Pizza")).Reply());
        Assert.Contains("Grund an", (await _bot.Send(Justin, "/request create 10")).Reply());
        Assert.Contains("doppelt erwähnt", (await _bot.Send(Justin, "/request create 10 Eis @Anna @Anna")).Reply());
        Assert.Contains("späteren Update", (await _bot.Send(Justin, "/request create 10 Eis split=custom")).Reply());
        Assert.Contains("Unbekannte Option", (await _bot.Send(Justin, "/request create 10 Eis colour=red")).Reply());
        Assert.Contains("gibt es hier nicht", (await _bot.Send(Justin, "/request create 10 Eis group=Urlaub")).Reply());

        await _bot.Send(Anna, "/group create Duo @Ben");
        Assert.Contains("nicht in *Duo*", (await _bot.Send(Justin, "/request create 10 Eis group=Duo")).Reply());
        Assert.Contains("ist nicht in *Duo*", (await _bot.Send(Anna, "/request create 10 Eis group=duo @Chris")).Reply());
    }

    [Fact]
    public async Task Preview_expires_after_five_minutes()
    {
        await _bot.Send(Justin, "/request create 40 Pizza");
        _bot.Time.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal("⌛ Vorschau abgelaufen, bitte neu erstellen.", (await _bot.Send(Justin, "/yes")).Reply());
        await using var db = _bot.NewDb();
        Assert.False(await db.Requests.AnyAsync());
    }

    [Fact]
    public async Task Only_the_creator_answers_the_preview_and_edit_returns_the_command()
    {
        await _bot.Send(Justin, "/request create 40 \"Pizza Freitag\"");
        Assert.Contains("nichts zu bestätigen", (await _bot.Send(Anna, "/yes")).Reply());

        var edit = (await _bot.Send(Justin, "/edit")).Reply();
        Assert.EndsWith("\n\n/request create 40 \"Pizza Freitag\"", edit);
        Assert.Contains("nichts zu bestätigen", (await _bot.Send(Justin, "/yes")).Reply());
    }

    [Fact]
    public async Task A_new_command_discards_the_open_preview()
    {
        await _bot.Send(Justin, "/request create 40 Pizza");
        await _bot.Send(Justin, "/group list");
        Assert.Contains("nichts zu bestätigen", (await _bot.Send(Justin, "/yes")).Reply());
    }

    [Fact]
    public async Task Pay_from_the_list_completes_the_request()
    {
        await _bot.CreateRequest("30 Pizza includeme=no");
        await _bot.CreateRequest("9 Eis @Anna includeme=no", creator: Ben);

        // In the group the list is sent privately.
        var list = await _bot.Send(Anna, "/pay");
        Assert.Equal("📩 Hab dir privat geschrieben.", list.Reply());
        var dm = list.To(Anna).Text;
        Assert.Contains("1. #1 Pizza → Justin · 10,00 € (seit heute)", dm);
        Assert.Contains("2. #2 Eis → Ben · 9,00 € (seit heute)", dm);
        Assert.Contains("*Summe: 19,00 €*", dm);

        var paid = await _bot.Send(Anna, "/pay 2");
        Assert.Contains("✅ #2 als bezahlt markiert", paid.To(Anna).Text);
        Assert.Contains(paid, m => m.ChatId == GroupChat && m.Text == "✅ Anfrage #2 (Eis) ist komplett bezahlt!");
        Assert.Contains("Alle haben für #2 (Eis) bezahlt", paid.To(Ben).Text);

        // The list is gone after use, so "1" is now request #1.
        var again = await _bot.Send(Anna, "/pay 1");
        Assert.Contains(again, m => m.Text == "✅ Anna hat #1 bezahlt (1/3)");

        await using var db = _bot.NewDb();
        Assert.Equal(RequestStatus.Completed, (await db.Requests.SingleAsync(r => r.Number == 2)).Status);
    }

    [Fact]
    public async Task Pay_all_and_paying_twice()
    {
        await _bot.CreateRequest("30 Pizza includeme=no");
        await _bot.CreateRequest("20 Kino @Anna @Ben includeme=no");

        var all = (await _bot.Send(Anna, "/pay all")).To(Anna).Text;
        Assert.Contains("✅ #1 als bezahlt markiert\n✅ #2 als bezahlt markiert", all);

        Assert.Contains("#1 war schon als bezahlt markiert", (await _bot.Send(Anna, "/pay #1")).To(Anna).Text);
        Assert.Contains("keine offenen Zahlungen 🎉", (await _bot.Send(Anna, "/pay all")).To(Anna).Text);
        Assert.Contains("Du hast bei #2 nichts offen", (await _bot.Send(Chris, "/pay 2")).To(Chris).Text);
        Assert.Contains("Anfrage #9 gibt es hier nicht", (await _bot.Send(Chris, "/pay 9")).To(Chris).Text);
    }

    [Fact]
    public async Task The_same_message_is_never_booked_twice()
    {
        await _bot.CreateRequest("30 Pizza includeme=no");
        await _bot.Send(Anna, "/pay 1", messageId: "duplicate");
        Assert.Empty(await _bot.Send(Anna, "/pay 1", messageId: "duplicate"));
    }

    [Fact]
    public async Task Pay_works_in_a_private_chat()
    {
        await _bot.CreateRequest("30 Pizza includeme=no");

        var list = await _bot.SendPrivate(Anna, "/pay");
        Assert.Contains("1. #1 Pizza → Justin", list.Single().Text);
        Assert.Equal(Anna.Id, list.Single().ChatId);

        var paid = await _bot.SendPrivate(Anna, "/pay 1");
        Assert.Contains("✅ #1 als bezahlt markiert", paid.To(Anna).Text);
        Assert.Contains(paid, m => m.ChatId == GroupChat && m.Text == "✅ Anna hat #1 bezahlt (1/3)");
    }
}
