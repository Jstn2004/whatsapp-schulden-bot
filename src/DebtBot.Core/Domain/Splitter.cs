namespace DebtBot.Core.Domain;

public static class Splitter
{
    /// <summary>
    /// Splits an amount into <paramref name="count"/> parts rounded to cents.
    /// Leftover cents go to the first parts, so the sum always matches exactly (10 / 3 = 3.34, 3.33, 3.33).
    /// </summary>
    public static decimal[] Equal(decimal total, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var cents = (long)decimal.Round(total * 100);
        var baseCents = cents / count;
        var rest = cents % count;
        return Enumerable.Range(0, count)
            .Select(i => (baseCents + (i < rest ? 1 : 0)) / 100m)
            .ToArray();
    }
}
