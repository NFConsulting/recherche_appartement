namespace ApartmentHunter.Core.Scrapers;

/// <summary>
/// Cache en mémoire partagé entre les exécutions du Timer dans la même instance Function.
/// Thread-safe via lock.
/// </summary>
internal static class SessionCache
{
    private static readonly Dictionary<string, (string Cookie, DateTime Expiry)> _cache = new();
    private static readonly object _lock = new();

    public static string? Get(string key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow < entry.Expiry)
                return entry.Cookie;
            return null;
        }
    }

    public static void Set(string key, string cookie, TimeSpan duration)
    {
        lock (_lock)
            _cache[key] = (cookie, DateTime.UtcNow.Add(duration));
    }

    public static void Invalidate(string key)
    {
        lock (_lock)
            _cache.Remove(key);
    }
}
