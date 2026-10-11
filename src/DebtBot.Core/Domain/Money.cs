using System.Globalization;
using System.Text.RegularExpressions;

namespace DebtBot.Core.Domain;

public static partial class Money
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public const decimal MaxAmount = 100_000m;

    /// <summary>Parses "12,50", "12.50", "12" or "12,50€". Returns null for anything else, zero or negative values.</summary>
    public static decimal? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var cleaned = input.Trim().Replace("€", "").Replace(',', '.');
        if (!AmountPattern().IsMatch(cleaned)) return null;
        var amount = decimal.Parse(cleaned, CultureInfo.InvariantCulture);
        return amount is > 0 and <= MaxAmount ? amount : null;
    }

    /// <summary>German format with two decimals, e.g. "1.234,50 €".</summary>
    public static string Format(decimal amount) => amount.ToString("N2", German) + " €";

    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex AmountPattern();
}
