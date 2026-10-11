using DebtBot.Core.Domain;
using DebtBot.Core.Messaging;
using DebtBot.Core.Parsing;

namespace DebtBot.Tests;

public class BasicsTests
{
    [Theory]
    [InlineData("12", 12)]
    [InlineData("12,50", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("0,99€", 0.99)]
    public void Money_parses_german_and_english_notation(string input, decimal expected) =>
        Assert.Equal(expected, Money.Parse(input));

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12,555")]
    [InlineData("1.234,50")]
    [InlineData("zehn")]
    [InlineData("")]
    public void Money_rejects_invalid_amounts(string input) => Assert.Null(Money.Parse(input));

    [Fact]
    public void Money_formats_in_german() => Assert.Equal("1.234,50 €", Money.Format(1234.5m));

    [Fact]
    public void Splitter_gives_leftover_cents_to_the_first_parts()
    {
        var parts = Splitter.Equal(10m, 3);
        Assert.Equal([3.34m, 3.33m, 3.33m], parts);
        Assert.Equal(10m, parts.Sum());
    }

    [Fact]
    public void Splitter_splits_evenly_when_possible() => Assert.Equal([10m, 10m, 10m, 10m], Splitter.Equal(40m, 4));

    [Fact]
    public void Parser_reads_name_quotes_options_and_mentions()
    {
        var mentions = new[] { new Mention("4915100000002", "4915100000002@c.us", "Anna") };
        var command = CommandParser.Parse("/REQUEST create 40 „Pizza Freitag“ group=\"Meine WG\" @4915100000002 @Unknown", mentions)!;

        Assert.Equal("request", command.Name);
        Assert.Collection(command.Args,
            t => Assert.Equal((TokenKind.Word, "create"), (t.Kind, t.Text)),
            t => Assert.Equal((TokenKind.Word, "40"), (t.Kind, t.Text)),
            t => Assert.Equal((TokenKind.Quoted, "Pizza Freitag"), (t.Kind, t.Text)),
            t => Assert.Equal((TokenKind.Option, "group", "Meine WG"), (t.Kind, t.Key, t.Text)),
            t => Assert.Equal((TokenKind.Mention, "4915100000002@c.us"), (t.Kind, t.PersonId)),
            t => Assert.Equal((TokenKind.Word, "@Unknown"), (t.Kind, t.Text)));
    }

    [Fact]
    public void Parser_keeps_quoted_text_with_equals_sign_as_text()
    {
        var command = CommandParser.Parse("/request create 5 \"a=b\"", [])!;
        Assert.Equal(TokenKind.Quoted, command.Args[2].Kind);
    }

    [Theory]
    [InlineData("hallo")]
    [InlineData("/")]
    [InlineData("  ")]
    public void Parser_ignores_non_commands(string body) => Assert.Null(CommandParser.Parse(body, []));
}
