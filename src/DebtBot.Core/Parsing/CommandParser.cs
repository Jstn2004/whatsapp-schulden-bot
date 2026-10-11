using System.Text;
using System.Text.RegularExpressions;
using DebtBot.Core.Messaging;

namespace DebtBot.Core.Parsing;

public enum TokenKind { Word, Quoted, Option, Mention }

/// <summary>
/// One argument of a command. Options carry <see cref="Key"/> (lower-case) and the value in <see cref="Text"/>;
/// mentions carry the <see cref="PersonId"/>.
/// </summary>
public sealed record Token(TokenKind Kind, string Text, string? Key = null, string? PersonId = null)
{
    public bool IsText => Kind is TokenKind.Word or TokenKind.Quoted;
}

/// <summary>A parsed command: "/Group Create WG" becomes name "group" with arguments "Create" and "WG".</summary>
public sealed record ParsedCommand(string Name, IReadOnlyList<Token> Args, string Raw);

public static partial class CommandParser
{
    // WhatsApp keyboards often turn " into typographic quotes, so all of them count.
    private static readonly HashSet<char> Quotes = ['"', '“', '”', '„'];

    /// <summary>Returns null if the text is not a command.</summary>
    public static ParsedCommand? Parse(string body, IReadOnlyList<Mention> mentions)
    {
        var raw = body.Trim();
        if (!raw.StartsWith('/')) return null;

        var parts = Split(raw);
        var name = parts[0].Text[1..].ToLowerInvariant();
        if (name.Length == 0) return null;

        var args = parts.Skip(1).Select(p => Classify(p.Text, p.StartsQuoted, mentions)).ToList();
        return new ParsedCommand(name, args, raw);
    }

    private static Token Classify(string text, bool startsQuoted, IReadOnlyList<Mention> mentions)
    {
        if (startsQuoted) return new Token(TokenKind.Quoted, text);

        if (text.StartsWith('@'))
        {
            var mention = mentions.FirstOrDefault(m => m.Token == text[1..]);
            if (mention is not null) return new Token(TokenKind.Mention, text, PersonId: mention.Id);
        }

        var option = OptionPattern().Match(text);
        if (option.Success)
        {
            return new Token(TokenKind.Option, option.Groups[2].Value, Key: option.Groups[1].Value.ToLowerInvariant());
        }

        return new Token(TokenKind.Word, text);
    }

    /// <summary>Splits on whitespace; quoted parts stay together. key="a b" keeps its spaces too.</summary>
    private static List<(string Text, bool StartsQuoted)> Split(string text)
    {
        var parts = new List<(string, bool)>();
        var current = new StringBuilder();
        var inToken = false;
        var quoted = false;
        var startsQuoted = false;

        foreach (var ch in text)
        {
            if (Quotes.Contains(ch))
            {
                if (!inToken) startsQuoted = true;
                quoted = !quoted;
                inToken = true;
            }
            else if (!quoted && char.IsWhiteSpace(ch))
            {
                if (inToken) parts.Add((current.ToString(), startsQuoted));
                current.Clear();
                inToken = false;
                startsQuoted = false;
            }
            else
            {
                current.Append(ch);
                inToken = true;
            }
        }

        if (inToken) parts.Add((current.ToString(), startsQuoted));
        return parts;
    }

    [GeneratedRegex(@"^([a-zA-Z]+)=(.*)$", RegexOptions.Singleline)]
    private static partial Regex OptionPattern();
}
