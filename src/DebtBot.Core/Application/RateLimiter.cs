namespace DebtBot.Core.Application;

public enum RateDecision { Allowed, BlockedWarn, Blocked }

/// <summary>Allows each person a limited number of commands per minute. Kept in memory on purpose.</summary>
public sealed class RateLimiter(int maxPerMinute = 10)
{
    private readonly Dictionary<string, Queue<DateTime>> _recent = [];
    private readonly HashSet<string> _warned = [];
    private readonly Lock _lock = new();

    public RateDecision Check(string personId, DateTime now)
    {
        lock (_lock)
        {
            if (!_recent.TryGetValue(personId, out var times)) _recent[personId] = times = new Queue<DateTime>();
            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(1)) times.Dequeue();

            if (times.Count < maxPerMinute)
            {
                times.Enqueue(now);
                _warned.Remove(personId);
                return RateDecision.Allowed;
            }

            // Warn once per blocked streak, then stay silent.
            return _warned.Add(personId) ? RateDecision.BlockedWarn : RateDecision.Blocked;
        }
    }
}
