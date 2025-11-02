using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tunnel2.DnsForwarder.Configuration;

namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Health check через специальные DNS запросы
/// Делегирует health check к primary DNS server, который сам проверяет свою инфраструктуру
/// </summary>
public sealed class HealthCheckService : IHealthCheckService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IOptionsMonitor<DnsForwarderOptions> _dnsForwarderOptionsMonitor;
    private readonly DateTime _startTime;
    private readonly Stopwatch _uptime;

    // Стандартные health check домены
    private static readonly string[] HealthCheckDomains = new[]
    {
        "health.check",
        "_health",
        "health",
        "healthcheck"
    };

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        IOptionsMonitor<DnsForwarderOptions> dnsForwarderOptionsMonitor)
    {
        _logger = logger;
        _dnsForwarderOptionsMonitor = dnsForwarderOptionsMonitor;
        _startTime = DateTime.UtcNow;
        _uptime = Stopwatch.StartNew();
    }

    public bool IsHealthCheckQuery(string hostname)
    {
        string normalizedHostname = hostname.TrimEnd('.').ToLowerInvariant();

        // Проверяем стандартные health check домены
        foreach (string healthDomain in HealthCheckDomains)
        {
            if (normalizedHostname == healthDomain ||
                normalizedHostname.EndsWith("." + healthDomain))
            {
                return true;
            }
        }

        return false;
    }

    public HealthStatus GetHealthStatus()
    {
        // Простой статус - форвардер сам по себе работает
        // Реальную проверку делает primary DNS server
        return new HealthStatus
        {
            IsHealthy = true,
            Status = "ok",
            CheckedAt = DateTime.UtcNow,
            Uptime = _uptime.ElapsedMilliseconds
        };
    }
}
