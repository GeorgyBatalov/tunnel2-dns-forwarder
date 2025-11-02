using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tunnel2.DnsForwarder.Configuration;
using Tunnel2.DnsForwarder.Models;
using Tunnel2.DnsForwarder.Services;

namespace Tunnel2.DnsForwarder.UnitTests;

public class DnsCacheServiceTests : IDisposable
{
    private readonly MemoryCache _memoryCache;
    private readonly Mock<ILogger<DnsCacheService>> _loggerMock;
    private readonly Mock<IOptionsMonitor<CacheOptions>> _optionsMonitorMock;
    private readonly DnsCacheService _cacheService;

    public DnsCacheServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<DnsCacheService>>();
        _optionsMonitorMock = new Mock<IOptionsMonitor<CacheOptions>>();

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(new CacheOptions
        {
            IsEnabled = true,
            MaxPositiveCacheTtlSeconds = 300,
            NegativeCacheTtlSeconds = 60,
            MaxCacheSize = 100,
            AbsoluteCacheExpirationSeconds = 3600
        });

        _cacheService = new DnsCacheService(_memoryCache, _loggerMock.Object, _optionsMonitorMock.Object);
    }

    [Fact]
    public void Get_WhenCacheDisabled_ShouldReturnNull()
    {
        // Arrange
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(new CacheOptions { IsEnabled = false });

        // Act
        CachedDnsResponse? result = _cacheService.Get("test-key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Get_WhenKeyNotExists_ShouldReturnNull()
    {
        // Act
        CachedDnsResponse? result = _cacheService.Get("non-existent-key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Set_AndGet_ShouldReturnCachedResponse()
    {
        // Arrange
        string cacheKey = "example.com|A";
        CachedDnsResponse cachedResponse = new CachedDnsResponse
        {
            ResponseData = new byte[] { 1, 2, 3, 4 },
            OriginalTtl = 60,
            CachedAt = DateTime.UtcNow,
            EntryType = CacheEntryType.Positive
        };

        // Act
        _cacheService.Set(cacheKey, cachedResponse);
        CachedDnsResponse? result = _cacheService.Get(cacheKey);

        // Assert
        result.Should().NotBeNull();
        result!.ResponseData.Should().Equal(new byte[] { 1, 2, 3, 4 });
        result.OriginalTtl.Should().Be(60);
        result.EntryType.Should().Be(CacheEntryType.Positive);
    }

    [Fact]
    public void Remove_ShouldDeleteCachedEntry()
    {
        // Arrange
        string cacheKey = "example.com|A";
        CachedDnsResponse cachedResponse = new CachedDnsResponse
        {
            ResponseData = new byte[] { 1, 2, 3 },
            OriginalTtl = 60,
            CachedAt = DateTime.UtcNow,
            EntryType = CacheEntryType.Positive
        };

        _cacheService.Set(cacheKey, cachedResponse);

        // Act
        _cacheService.Remove(cacheKey);
        CachedDnsResponse? result = _cacheService.Get(cacheKey);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetStatistics_ShouldTrackHitsAndMisses()
    {
        // Arrange
        string cacheKey = "example.com|A";
        CachedDnsResponse cachedResponse = new CachedDnsResponse
        {
            ResponseData = new byte[] { 1, 2, 3 },
            OriginalTtl = 60,
            CachedAt = DateTime.UtcNow,
            EntryType = CacheEntryType.Positive
        };

        _cacheService.Set(cacheKey, cachedResponse);

        // Act
        _cacheService.Get(cacheKey); // Hit
        _cacheService.Get("non-existent"); // Miss
        _cacheService.Get(cacheKey); // Hit

        CacheStatistics stats = _cacheService.GetStatistics();

        // Assert
        stats.TotalHits.Should().Be(2);
        stats.TotalMisses.Should().Be(1);
        stats.CurrentSize.Should().Be(1);
        stats.HitRate.Should().BeApproximately(0.666, 0.01);
    }

    public void Dispose()
    {
        _cacheService.Dispose();
        _memoryCache.Dispose();
    }
}