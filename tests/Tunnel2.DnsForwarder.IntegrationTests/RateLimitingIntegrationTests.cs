using FluentAssertions;
using Makaretu.Dns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tunnel2.DnsForwarder.Configuration;
using Tunnel2.DnsForwarder.IntegrationTests.Helpers;
using Tunnel2.DnsForwarder.Services;

namespace Tunnel2.DnsForwarder.IntegrationTests;

/// <summary>
/// Integration тесты для rate limiting
/// </summary>
public class RateLimitingIntegrationTests : IAsyncDisposable
{
    private readonly MockUpstreamDnsServer _mockUpstreamServer;
    private readonly RawDnsClient _dnsClient;
    private readonly IHost _forwarderHost;
    private readonly int _forwarderPort;

    public RateLimitingIntegrationTests()
    {
        _mockUpstreamServer = new MockUpstreamDnsServer();
        _forwarderPort = Random.Shared.Next(20000, 30000);
        _forwarderHost = CreateForwarderHostWithStrictRateLimit();
        _dnsClient = new RawDnsClient("127.0.0.1", _forwarderPort);

        _forwarderHost.StartAsync().Wait();
        Thread.Sleep(500);
    }

    private IHost CreateForwarderHostWithStrictRateLimit()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.Configure<DnsForwarderOptions>(options =>
        {
            options.ListenIpv4 = "127.0.0.1";
            options.UdpPort = _forwarderPort;
            options.UpstreamDnsAddress = "127.0.0.1";
            options.UpstreamDnsPort = _mockUpstreamServer.Port;
            options.UpstreamTimeoutSeconds = 2;
            options.UpstreamRetryCount = 0;
        });

        builder.Services.Configure<CacheOptions>(options =>
        {
            options.IsEnabled = false; // Отключаем кэш для тестирования rate limit
        });

        builder.Services.Configure<RateLimitOptions>(options =>
        {
            options.IsEnabled = true;
            options.MaxRequestsPerIp = 5; // Строгий лимит для тестов
            options.TimeWindowSeconds = 60;
            options.MaxTrackedIps = 100;
        });

        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IDnsCacheService, DnsCacheService>();
        builder.Services.AddSingleton<IRateLimiter, RateLimiterService>();
        builder.Services.AddSingleton<IUpstreamDnsClient, UpstreamDnsClient>();
        builder.Services.AddSingleton<IHealthCheckService, HealthCheckService>();
        builder.Services.AddHostedService<DnsForwarderService>();

        return builder.Build();
    }

    [Fact]
    public async Task Query_WithinRateLimit_ShouldSucceed()
    {
        // Arrange
        _mockUpstreamServer.AddARecord("test.com", "192.0.2.1");

        // Act - 5 запросов (в пределах лимита)
        List<DnsResponse> responses = new List<DnsResponse>();
        for (int i = 0; i < 5; i++)
        {
            responses.Add(await _dnsClient.QueryARecordAsync("test.com"));
        }

        // Assert - все должны пройти
        responses.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        _mockUpstreamServer.RequestCount.Should().Be(5);
    }

    [Fact]
    public async Task Query_ExceedingRateLimit_ShouldBeBlocked()
    {
        // Arrange
        _mockUpstreamServer.AddARecord("test.com", "192.0.2.1");

        // Act - 7 запросов (5 allowed + 2 blocked)
        List<DnsResponse> responses = new List<DnsResponse>();
        for (int i = 0; i < 7; i++)
        {
            responses.Add(await _dnsClient.QueryARecordAsync("test.com"));
            await Task.Delay(50); // Небольшая задержка между запросами
        }

        // Assert
        // Первые 5 должны пройти, остальные будут timeout (rate limiter не отвечает)
        int successCount = responses.Count(r => r.IsSuccess);
        int timeoutCount = responses.Count(r => r.IsTimeout);

        successCount.Should().Be(5, "first 5 requests should succeed");
        timeoutCount.Should().BeGreaterThanOrEqualTo(1, "requests over limit should timeout");

        // Upstream должен получить только разрешенные запросы
        _mockUpstreamServer.RequestCount.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task Query_ConcurrentRequests_ShouldRespectRateLimit()
    {
        // Arrange
        _mockUpstreamServer.AddARecord("concurrent.test", "192.0.2.50");

        // Act - 10 параллельных запросов
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _dnsClient.QueryARecordAsync("concurrent.test"))
            .ToArray();

        DnsResponse[] responses = await Task.WhenAll(tasks);

        // Assert
        int successCount = responses.Count(r => r.IsSuccess);
        int timeoutCount = responses.Count(r => r.IsTimeout);

        // Максимум 5 должны пройти (rate limit)
        successCount.Should().BeLessThanOrEqualTo(5);
        timeoutCount.Should().BeGreaterThanOrEqualTo(5);

        _mockUpstreamServer.RequestCount.Should().BeLessThanOrEqualTo(5);
    }

    public async ValueTask DisposeAsync()
    {
        await _forwarderHost.StopAsync();
        _forwarderHost.Dispose();
        _mockUpstreamServer.Dispose();
    }
}
