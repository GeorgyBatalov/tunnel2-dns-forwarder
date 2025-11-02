using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tunnel2.DnsForwarder.Configuration;
using Tunnel2.DnsForwarder.Models;

namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Реализация dual cache для DNS ответов (positive + negative)
/// </summary>
public sealed class DnsCacheService : IDnsCacheService, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<DnsCacheService> _logger;
    private readonly IOptionsMonitor<CacheOptions> _cacheOptionsMonitor;
    private readonly ConcurrentDictionary<string, byte> _cacheKeys; // для отслеживания размера
    private long _totalHits;
    private long _totalMisses;

    public DnsCacheService(
        IMemoryCache cache,
        ILogger<DnsCacheService> logger,
        IOptionsMonitor<CacheOptions> cacheOptionsMonitor)
    {
        _cache = cache;
        _logger = logger;
        _cacheOptionsMonitor = cacheOptionsMonitor;
        _cacheKeys = new ConcurrentDictionary<string, byte>();
    }

    public CachedDnsResponse? Get(string cacheKey)
    {
        CacheOptions options = _cacheOptionsMonitor.CurrentValue;

        if (!options.IsEnabled)
        {
            return null;
        }

        if (_cache.TryGetValue(cacheKey, out CachedDnsResponse? cachedResponse))
        {
            Interlocked.Increment(ref _totalHits);
            _logger.LogDebug("Cache HIT for key: {CacheKey}, type: {EntryType}",
                cacheKey, cachedResponse?.EntryType);
            return cachedResponse;
        }

        Interlocked.Increment(ref _totalMisses);
        _logger.LogDebug("Cache MISS for key: {CacheKey}", cacheKey);
        return null;
    }

    public void Set(string cacheKey, CachedDnsResponse response)
    {
        CacheOptions options = _cacheOptionsMonitor.CurrentValue;

        if (!options.IsEnabled)
        {
            return;
        }

        // Проверка лимита размера кэша
        if (_cacheKeys.Count >= options.MaxCacheSize && !_cacheKeys.ContainsKey(cacheKey))
        {
            _logger.LogWarning("Cache size limit reached ({MaxSize}), skipping cache for key: {CacheKey}",
                options.MaxCacheSize, cacheKey);
            return;
        }

        // Вычисляем TTL в зависимости от типа записи
        TimeSpan slidingExpiration;
        TimeSpan absoluteExpiration;

        if (response.EntryType == CacheEntryType.Positive)
        {
            // Для positive: min(TTL из ответа, MaxPositiveCacheTtlSeconds)
            int effectiveTtl = Math.Min(response.OriginalTtl, options.MaxPositiveCacheTtlSeconds);
            slidingExpiration = TimeSpan.FromSeconds(effectiveTtl);
            absoluteExpiration = TimeSpan.FromSeconds(options.AbsoluteCacheExpirationSeconds);
        }
        else
        {
            // Для negative: фиксированный NegativeCacheTtlSeconds
            slidingExpiration = TimeSpan.FromSeconds(options.NegativeCacheTtlSeconds);
            absoluteExpiration = TimeSpan.FromSeconds(options.NegativeCacheTtlSeconds * 2);
        }

        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = slidingExpiration,
            AbsoluteExpirationRelativeToNow = absoluteExpiration
        };

        // Callback для удаления из tracking dictionary
        cacheEntryOptions.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            if (key is string stringKey)
            {
                _cacheKeys.TryRemove(stringKey, out _);
                _logger.LogDebug("Cache entry evicted: {Key}, reason: {Reason}", stringKey, reason);
            }
        });

        _cache.Set(cacheKey, response, cacheEntryOptions);
        _cacheKeys.TryAdd(cacheKey, 0);

        _logger.LogInformation(
            "Cached DNS response: key={CacheKey}, type={EntryType}, TTL={Ttl}s, sliding={Sliding}s",
            cacheKey, response.EntryType, response.OriginalTtl, (int)slidingExpiration.TotalSeconds);
    }

    public void Remove(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _cacheKeys.TryRemove(cacheKey, out _);
        _logger.LogDebug("Removed cache entry: {CacheKey}", cacheKey);
    }

    public void Clear()
    {
        // MemoryCache не поддерживает Clear(), поэтому удаляем все известные ключи
        foreach (string key in _cacheKeys.Keys)
        {
            _cache.Remove(key);
        }

        _cacheKeys.Clear();
        _logger.LogInformation("Cache cleared");
    }

    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            TotalHits = Interlocked.Read(ref _totalHits),
            TotalMisses = Interlocked.Read(ref _totalMisses),
            CurrentSize = _cacheKeys.Count
        };
    }

    public void Dispose()
    {
        _cacheKeys.Clear();
    }
}
