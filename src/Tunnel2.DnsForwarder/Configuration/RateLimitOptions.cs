namespace Tunnel2.DnsForwarder.Configuration;

/// <summary>
/// Конфигурация rate limiting для защиты от DDoS
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>
    /// Включен ли rate limiting
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Максимальное количество запросов с одного IP адреса в течение временного окна
    /// </summary>
    public int MaxRequestsPerIp { get; set; } = 100;

    /// <summary>
    /// Размер временного окна для rate limiting (в секундах)
    /// </summary>
    public int TimeWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Максимальное количество IP адресов в rate limiter кэше
    /// </summary>
    public int MaxTrackedIps { get; set; } = 10000;
}
