using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tunnel2.DnsForwarder.Configuration;
using Tunnel2.DnsForwarder.Services;

namespace Tunnel2.DnsForwarder;

public class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Настройка логирования
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Конфигурация
        builder.Services.Configure<DnsForwarderOptions>(
            builder.Configuration.GetSection("DnsForwarder"));
        builder.Services.Configure<CacheOptions>(
            builder.Configuration.GetSection("Cache"));
        builder.Services.Configure<RateLimitOptions>(
            builder.Configuration.GetSection("RateLimit"));

        // Memory Cache
        builder.Services.AddMemoryCache();

        // Регистрация сервисов
        builder.Services.AddSingleton<IDnsCacheService, DnsCacheService>();
        builder.Services.AddSingleton<IRateLimiter, RateLimiterService>();
        builder.Services.AddSingleton<IUpstreamDnsClient, UpstreamDnsClient>();

        // Главный background service
        builder.Services.AddHostedService<DnsForwarderService>();

        // Windows Service/Systemd поддержка
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "Tunnel2 DNS Forwarder";
        });

        builder.Services.AddSystemd();

        IHost host = builder.Build();

        ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Tunnel2 DNS Forwarder starting...");
        logger.LogInformation("Environment: {Environment}", builder.Environment.EnvironmentName);

        // Вывод конфигурации при старте
        LogConfiguration(host.Services, logger);

        await host.RunAsync();
    }

    private static void LogConfiguration(IServiceProvider services, ILogger<Program> logger)
    {
        var dnsOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DnsForwarderOptions>>().Value;
        var cacheOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CacheOptions>>().Value;
        var rateLimitOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitOptions>>().Value;

        logger.LogInformation("=== DNS Forwarder Configuration ===");
        logger.LogInformation("Listen: {ListenIp}:{UdpPort}", dnsOptions.ListenIpv4, dnsOptions.UdpPort);
        logger.LogInformation("Upstream DNS: {UpstreamAddress}:{UpstreamPort}",
            dnsOptions.UpstreamDnsAddress, dnsOptions.UpstreamDnsPort);
        logger.LogInformation("Upstream Timeout: {Timeout}s, Retries: {Retries}",
            dnsOptions.UpstreamTimeoutSeconds, dnsOptions.UpstreamRetryCount);

        logger.LogInformation("Cache Enabled: {CacheEnabled}", cacheOptions.IsEnabled);
        if (cacheOptions.IsEnabled)
        {
            logger.LogInformation("Cache - Max Positive TTL: {MaxTtl}s, Negative TTL: {NegativeTtl}s",
                cacheOptions.MaxPositiveCacheTtlSeconds, cacheOptions.NegativeCacheTtlSeconds);
            logger.LogInformation("Cache - Max Size: {MaxSize}, Absolute Expiration: {AbsoluteExpiration}s",
                cacheOptions.MaxCacheSize, cacheOptions.AbsoluteCacheExpirationSeconds);
        }

        logger.LogInformation("Rate Limit Enabled: {RateLimitEnabled}", rateLimitOptions.IsEnabled);
        if (rateLimitOptions.IsEnabled)
        {
            logger.LogInformation("Rate Limit - Max Requests: {MaxRequests}/{TimeWindow}s",
                rateLimitOptions.MaxRequestsPerIp, rateLimitOptions.TimeWindowSeconds);
            logger.LogInformation("Rate Limit - Max Tracked IPs: {MaxTrackedIps}",
                rateLimitOptions.MaxTrackedIps);
        }

        logger.LogInformation("====================================");
    }
}
