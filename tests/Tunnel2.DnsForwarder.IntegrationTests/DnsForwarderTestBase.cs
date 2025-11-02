using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tunnel2.DnsForwarder.Configuration;
using Tunnel2.DnsForwarder.IntegrationTests.Helpers;
using Tunnel2.DnsForwarder.Services;

namespace Tunnel2.DnsForwarder.IntegrationTests;

/// <summary>
/// Base класс для integration тестов DNS форвардера
/// </summary>
public abstract class DnsForwarderTestBase : IAsyncDisposable
{
    protected MockUpstreamDnsServer MockUpstreamServer { get; }
    protected RawDnsClient DnsClient { get; }
    protected IHost ForwarderHost { get; }
    protected int ForwarderPort { get; }

    protected DnsForwarderTestBase()
    {
        // Создаем mock upstream DNS сервер
        MockUpstreamServer = new MockUpstreamDnsServer();

        // Случайный порт для форвардера
        ForwarderPort = Random.Shared.Next(20000, 30000);

        // Создаем и настраиваем DNS форвардер
        ForwarderHost = CreateForwarderHost();

        // Создаем DNS клиент для тестов
        DnsClient = new RawDnsClient("127.0.0.1", ForwarderPort);

        // Запускаем форвардер
        ForwarderHost.StartAsync().Wait();

        // Даем время на старт
        Thread.Sleep(500);
    }

    private IHost CreateForwarderHost()
    {
        var builder = Host.CreateApplicationBuilder();

        // Минимальное логирование для тестов
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Конфигурация для тестов
        builder.Services.Configure<DnsForwarderOptions>(options =>
        {
            options.ListenIpv4 = "127.0.0.1";
            options.UdpPort = ForwarderPort;
            options.UpstreamDnsAddress = "127.0.0.1";
            options.UpstreamDnsPort = MockUpstreamServer.Port;
            options.UpstreamTimeoutSeconds = 2;
            options.UpstreamRetryCount = 1;
        });

        builder.Services.Configure<CacheOptions>(options =>
        {
            options.IsEnabled = true;
            options.MaxPositiveCacheTtlSeconds = 60;
            options.NegativeCacheTtlSeconds = 30;
            options.MaxCacheSize = 1000;
            options.AbsoluteCacheExpirationSeconds = 300;
        });

        builder.Services.Configure<RateLimitOptions>(options =>
        {
            options.IsEnabled = true;
            options.MaxRequestsPerIp = 100;
            options.TimeWindowSeconds = 60;
            options.MaxTrackedIps = 100;
        });

        // Регистрация сервисов
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IDnsCacheService, DnsCacheService>();
        builder.Services.AddSingleton<IRateLimiter, RateLimiterService>();
        builder.Services.AddSingleton<IUpstreamDnsClient, UpstreamDnsClient>();
        builder.Services.AddSingleton<IHealthCheckService, HealthCheckService>();
        builder.Services.AddHostedService<DnsForwarderService>();

        return builder.Build();
    }

    public async ValueTask DisposeAsync()
    {
        await ForwarderHost.StopAsync();
        ForwarderHost.Dispose();
        MockUpstreamServer.Dispose();
    }
}
