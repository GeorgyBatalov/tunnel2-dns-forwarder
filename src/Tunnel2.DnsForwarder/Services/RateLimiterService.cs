using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tunnel2.DnsForwarder.Configuration;

namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Rate limiter с sliding window для защиты от DDoS
/// </summary>
public sealed class RateLimiterService : IRateLimiter, IDisposable
{
    private readonly ILogger<RateLimiterService> _logger;
    private readonly IOptionsMonitor<RateLimitOptions> _rateLimitOptionsMonitor;
    private readonly ConcurrentDictionary<string, IpRequestWindow> _requestWindows;
    private readonly Timer _cleanupTimer;
    private long _totalAllowed;
    private long _totalBlocked;

    public RateLimiterService(
        ILogger<RateLimiterService> logger,
        IOptionsMonitor<RateLimitOptions> rateLimitOptionsMonitor)
    {
        _logger = logger;
        _rateLimitOptionsMonitor = rateLimitOptionsMonitor;
        _requestWindows = new ConcurrentDictionary<string, IpRequestWindow>();

        // Таймер для очистки старых записей (каждые 60 секунд)
        _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    public bool IsAllowed(IPAddress ipAddress)
    {
        RateLimitOptions options = _rateLimitOptionsMonitor.CurrentValue;

        if (!options.IsEnabled)
        {
            Interlocked.Increment(ref _totalAllowed);
            return true;
        }

        string ipKey = ipAddress.ToString();
        DateTime now = DateTime.UtcNow;
        TimeSpan timeWindow = TimeSpan.FromSeconds(options.TimeWindowSeconds);

        IpRequestWindow window = _requestWindows.AddOrUpdate(
            ipKey,
            _ => new IpRequestWindow(),
            (_, existingWindow) =>
            {
                // Очистка старых timestamps за пределами окна
                existingWindow.CleanOldTimestamps(now, timeWindow);
                return existingWindow;
            });

        // Проверяем лимит ПЕРЕД добавлением нового timestamp
        if (window.GetRequestCount() >= options.MaxRequestsPerIp)
        {
            Interlocked.Increment(ref _totalBlocked);
            _logger.LogWarning("Rate limit exceeded for IP: {IpAddress}, requests: {Count}/{Max}",
                ipAddress, window.GetRequestCount(), options.MaxRequestsPerIp);
            return false;
        }

        // Добавляем новый timestamp
        window.AddRequest(now);
        Interlocked.Increment(ref _totalAllowed);

        return true;
    }

    public RateLimitStatistics GetStatistics()
    {
        return new RateLimitStatistics
        {
            TotalAllowed = Interlocked.Read(ref _totalAllowed),
            TotalBlocked = Interlocked.Read(ref _totalBlocked),
            CurrentTrackedIps = _requestWindows.Count
        };
    }

    private void CleanupOldEntries(object? state)
    {
        try
        {
            RateLimitOptions options = _rateLimitOptionsMonitor.CurrentValue;
            DateTime now = DateTime.UtcNow;
            TimeSpan timeWindow = TimeSpan.FromSeconds(options.TimeWindowSeconds);

            // Удаляем IP адреса без активности
            List<string> keysToRemove = new List<string>();

            foreach (var kvp in _requestWindows)
            {
                kvp.Value.CleanOldTimestamps(now, timeWindow);

                // Если нет активных запросов, удаляем
                if (kvp.Value.GetRequestCount() == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (string key in keysToRemove)
            {
                _requestWindows.TryRemove(key, out _);
            }

            // Если превышен лимит tracked IPs, удаляем самые старые
            if (_requestWindows.Count > options.MaxTrackedIps)
            {
                int toRemove = _requestWindows.Count - options.MaxTrackedIps;
                var oldestEntries = _requestWindows
                    .OrderBy(kvp => kvp.Value.GetOldestTimestamp())
                    .Take(toRemove)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (string key in oldestEntries)
                {
                    _requestWindows.TryRemove(key, out _);
                }

                _logger.LogWarning("Rate limiter cache overflow, removed {Count} oldest entries", toRemove);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} inactive IP entries from rate limiter", keysToRemove.Count);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error during rate limiter cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _requestWindows.Clear();
    }

    /// <summary>
    /// Окно запросов для одного IP адреса
    /// </summary>
    private sealed class IpRequestWindow
    {
        private readonly object _lock = new object();
        private readonly List<DateTime> _timestamps;

        public IpRequestWindow()
        {
            _timestamps = new List<DateTime>();
        }

        public void AddRequest(DateTime timestamp)
        {
            lock (_lock)
            {
                _timestamps.Add(timestamp);
            }
        }

        public void CleanOldTimestamps(DateTime now, TimeSpan timeWindow)
        {
            lock (_lock)
            {
                DateTime cutoff = now - timeWindow;
                _timestamps.RemoveAll(t => t < cutoff);
            }
        }

        public int GetRequestCount()
        {
            lock (_lock)
            {
                return _timestamps.Count;
            }
        }

        public DateTime GetOldestTimestamp()
        {
            lock (_lock)
            {
                return _timestamps.Count > 0 ? _timestamps[0] : DateTime.MinValue;
            }
        }
    }
}
