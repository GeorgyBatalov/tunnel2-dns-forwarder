using System.Net;

namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Rate limiter для защиты от DDoS атак
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Проверить, разрешен ли запрос с указанного IP адреса
    /// </summary>
    /// <param name="ipAddress">IP адрес клиента</param>
    /// <returns>true если запрос разрешен, false если превышен лимит</returns>
    bool IsAllowed(IPAddress ipAddress);

    /// <summary>
    /// Получить статистику rate limiting
    /// </summary>
    RateLimitStatistics GetStatistics();
}

/// <summary>
/// Статистика rate limiting
/// </summary>
public sealed class RateLimitStatistics
{
    public long TotalAllowed { get; set; }
    public long TotalBlocked { get; set; }
    public int CurrentTrackedIps { get; set; }
}
