using Tunnel2.DnsForwarder.Models;

namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Сервис кэширования DNS ответов (dual cache: positive + negative)
/// </summary>
public interface IDnsCacheService
{
    /// <summary>
    /// Получить кэшированный ответ для DNS запроса
    /// </summary>
    /// <param name="cacheKey">Ключ кэша (обычно hostname + query type)</param>
    /// <returns>Кэшированный ответ или null если не найден</returns>
    CachedDnsResponse? Get(string cacheKey);

    /// <summary>
    /// Сохранить DNS ответ в кэш
    /// </summary>
    /// <param name="cacheKey">Ключ кэша</param>
    /// <param name="response">DNS ответ для кэширования</param>
    void Set(string cacheKey, CachedDnsResponse response);

    /// <summary>
    /// Удалить запись из кэша
    /// </summary>
    /// <param name="cacheKey">Ключ кэша</param>
    void Remove(string cacheKey);

    /// <summary>
    /// Очистить весь кэш
    /// </summary>
    void Clear();

    /// <summary>
    /// Получить статистику кэша
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// Статистика кэша
/// </summary>
public sealed class CacheStatistics
{
    public long TotalHits { get; set; }
    public long TotalMisses { get; set; }
    public int CurrentSize { get; set; }
    public double HitRate => TotalHits + TotalMisses > 0
        ? (double)TotalHits / (TotalHits + TotalMisses)
        : 0;
}
