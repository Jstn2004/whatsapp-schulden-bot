using DebtBot.Core.Domain;

namespace DebtBot.Core.Application;

/// <summary>A mistake by the user. The message is sent back to the chat as is; nothing is saved.</summary>
public sealed class UserError(string message) : Exception(message);

/// <summary>Small helpers for the German texts the bot sends.</summary>
public static class Text
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>The text that @mentions a person; the ID also has to go into the message's mention list.</summary>
    public static string Tag(string personId) => "@" + personId.Split('@')[0];

    public static DateTime ToBerlin(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Berlin);

    public static string Date(DateTime utc) => ToBerlin(utc).ToString("dd.MM.yyyy");

    /// <summary>Calendar days between two UTC times in Berlin time.</summary>
    public static int DaysBetween(DateTime fromUtc, DateTime toUtc) => (ToBerlin(toUtc).Date - ToBerlin(fromUtc).Date).Days;

    /// <summary>"seit heute", "seit 1 Tag", "seit 3 Tagen".</summary>
    public static string Since(DateTime fromUtc, DateTime nowUtc) => DaysBetween(fromUtc, nowUtc) switch
    {
        <= 0 => "seit heute",
        1 => "seit 1 Tag",
        var days => $"seit {days} Tagen",
    };

    /// <summary>"1 Tag" / "3 Tagen", for use after "nach".</summary>
    public static string Days(int days) => days == 1 ? "1 Tag" : $"{days} Tagen";

    public static string Name(Person person) => person.DisplayName;

    public static string Count(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";

    public static string Euro(decimal amount) => Money.Format(amount);
}
