namespace Tunnel2.DnsForwarder.Configuration;

/// <summary>
/// Конфигурация кэширования DNS ответов
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// Включен ли кэш
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Максимальное sliding expiration для positive кэша (успешные ответы)
    /// Реальный TTL = min(TTL из DNS ответа, MaxPositiveCacheTtlSeconds)
    /// </summary>
    public int MaxPositiveCacheTtlSeconds { get; set; } = 300; // 5 минут

    /// <summary>
    /// Sliding expiration для negative кэша (NXDOMAIN и другие ошибки)
    /// </summary>
    public int NegativeCacheTtlSeconds { get; set; } = 60; // 1 минута

    /// <summary>
    /// Максимальное количество записей в кэше
    /// </summary>
    public int MaxCacheSize { get; set; } = 10000;

    /// <summary>
    /// Абсолютное время жизни записи в кэше (независимо от TTL)
    /// </summary>
    public int AbsoluteCacheExpirationSeconds { get; set; } = 3600; // 1 час
}
