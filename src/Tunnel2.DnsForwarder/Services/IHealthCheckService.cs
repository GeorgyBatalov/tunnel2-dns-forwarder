namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Сервис для health check через DNS запросы
/// </summary>
public interface IHealthCheckService
{
    /// <summary>
    /// Проверить, является ли запрос health check запросом
    /// </summary>
    bool IsHealthCheckQuery(string hostname);

    /// <summary>
    /// Получить статус здоровья сервиса
    /// </summary>
    HealthStatus GetHealthStatus();
}

/// <summary>
/// Статус здоровья сервиса
/// </summary>
public sealed class HealthStatus
{
    public required bool IsHealthy { get; init; }
    public required string Status { get; init; }
    public required DateTime CheckedAt { get; init; }
    public long Uptime { get; init; }
}
