using DebtBot.Core.Domain;
using Microsoft.EntityFrameworkCore;
using static DebtBot.Tests.BotHarness;

namespace DebtBot.Tests;

public class GroupTests : IAsyncLifetime
{
    private readonly BotHarness _bot = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _bot.DisposeAsync();

    [Fact]
    public async Task First_group_becomes_default_and_creator_is_admin()
    {
        var reply = (await _bot.Send(Justin, "/group create WG @Anna @Ben")).Reply();

        Assert.Contains("👥 *WG* ⭐", reply);
        Assert.Contains("*Mitglieder (3)*", reply);
        Assert.Contains("- Justin 👑", reply);
        Assert.Contains("- Anna\n", reply);
        Assert.Contains("Erinnerung: nach 3 Tagen, wiederholt, max. 5×", reply);
        Assert.Contains("*Offen:* 0 Anfragen · 0,00 €", reply);
    }

    [Fact]
    public async Task Create_validates_input()
    {
        Assert.Contains("mindestens 2 Mitglieder", (await _bot.Send(Justin, "/group create WG")).Reply());
        Assert.Contains("mindestens 2 Mitglieder", (await _bot.Send(Justin, "/group create WG @Justin")).Reply());
        Assert.Contains("nicht zuordnen", (await _bot.Send(Justin, "/group create WG @Paul")).Reply());
        Assert.Contains("Mich selbst", (await _bot.Send(Justin, "/group create WG @Bot @Anna")).Reply());
        Assert.Contains("1–30 Zeichen", (await _bot.Send(Justin, $"/group create \"{new string('x', 31)}\" @Anna")).Reply());
        Assert.Contains("nur in einem Gruppenchat", (await _bot.SendPrivate(Justin, "/group create WG @Anna")).To(Justin).Text);

        await _bot.CreateWg();
        Assert.Contains("gibt es hier schon", (await _bot.Send(Anna, "/group create wg @Ben")).Reply());
    }

    [Fact]
    public async Task There_is_always_exactly_one_default_group()
    {
        await _bot.CreateWg();
        var reply = (await _bot.Send(Anna, "/group create Freunde @Ben")).Reply();
        Assert.DoesNotContain("⭐", reply);

        Assert.Contains("nur Admins", (await _bot.Send(Ben, "/group default Freunde")).Reply());
        Assert.Contains("*Freunde* ist jetzt die Standard-Gruppe", (await _bot.Send(Anna, "/group default freunde")).Reply());

        await using var db = _bot.NewDb();
        Assert.Equal(["Freunde"], await db.Groups.Where(g => g.IsDefault).Select(g => g.Name).ToListAsync());
        Assert.Contains("• *Freunde* ⭐", (await _bot.Send(Ben, "/GROUP LIST")).Reply());
    }

    [Fact]
    public async Task Admins_manage_members_and_the_last_admin_stays()
    {
        await _bot.CreateWg(Anna, Ben);

        Assert.Contains("nur Admins", (await _bot.Send(Anna, "/group add WG @Chris")).Reply());
        var added = (await _bot.Send(Justin, "/group add WG @Chris @Anna")).Reply();
        Assert.Contains("hinzugefügt: Chris", added);
        Assert.Contains("Schon dabei: Anna", added);

        Assert.Contains("letzte Admin", (await _bot.Send(Justin, "/group admin WG @Justin")).Reply());
        Assert.Contains("letzte Admin", (await _bot.Send(Justin, "/group remove WG @Justin")).Reply());
        Assert.Contains("👑 Anna ist jetzt Admin", (await _bot.Send(Justin, "/group admin WG @Anna")).Reply());
        Assert.Contains("kein Admin von *WG* mehr", (await _bot.Send(Anna, "/group admin WG @Justin")).Reply());

        Assert.Contains("Chris ist nicht mehr in *WG*", (await _bot.Send(Anna, "/group remove WG @Chris")).Reply());
        Assert.Contains("*Mitglieder (3)*", (await _bot.Send(Ben, "/group info")).Reply());
    }

    [Fact]
    public async Task Removing_someone_with_open_shares_needs_confirmation()
    {
        await _bot.CreateWg();
        await _bot.CreateRequest("30 Pizza");

        var warning = (await _bot.Send(Justin, "/group remove WG @Anna")).Reply();
        Assert.Contains("schuldet 7,50 € für #1 Pizza", warning);
        Assert.Contains("/yes trotzdem entfernen", warning);

        Assert.Contains("nicht mehr in *WG*", (await _bot.Send(Justin, "/yes")).Reply());
        await using var db = _bot.NewDb();
        Assert.True(await db.Shares.AnyAsync(s => s.DebtorId == Anna.Id && s.Status == ShareStatus.Open));
    }

    [Fact]
    public async Task Rename_and_settings()
    {
        await _bot.CreateWg();
        Assert.Contains("*WG* heißt jetzt *WG Küche*", (await _bot.Send(Justin, "/group rename WG \"WG Küche\"")).Reply());

        var shown = (await _bot.Send(Anna, "/group settings \"wg küche\"")).Reply();
        Assert.Contains("Prüfung: aus (Vertrauensbasis)", shown);

        // A bad value cancels the whole command, so remind=5 is not saved either.
        Assert.Contains("Ungültige Einstellung", (await _bot.Send(Justin, "/group settings \"WG Küche\" remind=5 maxremind=x")).Reply());
        Assert.Contains("nach 3 Tagen", (await _bot.Send(Justin, "/group settings \"WG Küche\"")).Reply());

        var saved = (await _bot.Send(Justin, "/group settings \"WG Küche\" remind=1 repeat=off")).Reply();
        Assert.Contains("Erinnerung: nach 1 Tag, einmalig", saved);
        Assert.Contains("nur Admins", (await _bot.Send(Anna, "/group settings \"WG Küche\" remind=2")).Reply());
        Assert.Contains("späteren Update", (await _bot.Send(Justin, "/group settings \"WG Küche\" verify=on")).Reply());
    }

    [Fact]
    public async Task Deleting_a_group_cancels_its_requests_and_moves_the_default()
    {
        await _bot.CreateWg();
        await _bot.Send(Justin, "/group create Freunde @Anna");
        await _bot.CreateRequest("30 Pizza");

        Assert.Contains("1 offene Anfrage", (await _bot.Send(Justin, "/group delete WG")).Reply());
        var deleted = await _bot.Send(Justin, "/yes");

        Assert.Contains("⭐ *Freunde* ist jetzt die Standard-Gruppe", deleted.Reply());
        foreach (var debtor in new[] { Anna, Ben, Chris })
        {
            Assert.Contains("wurde gelöscht – du musst nichts mehr zahlen", deleted.To(debtor).Text);
        }

        await using var db = _bot.NewDb();
        Assert.Equal(RequestStatus.Deleted, (await db.Requests.SingleAsync()).Status);
        // The name is free again.
        Assert.Contains("Gruppe erstellt", (await _bot.Send(Justin, "/group create WG @Anna")).Reply());
    }

    [Fact]
    public async Task No_cancels_a_question()
    {
        await _bot.CreateWg();
        await _bot.Send(Justin, "/group delete WG");
        Assert.Equal("❌ Abgebrochen.", (await _bot.Send(Justin, "/no")).Reply());
        Assert.Contains("nichts zu bestätigen", (await _bot.Send(Justin, "/yes")).Reply());
    }
}
