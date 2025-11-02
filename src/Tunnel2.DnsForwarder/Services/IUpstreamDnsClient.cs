namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Клиент для отправки DNS запросов к upstream DNS серверу (primary DNS)
/// </summary>
public interface IUpstreamDnsClient
{
    /// <summary>
    /// Отправить DNS запрос к upstream серверу
    /// </summary>
    /// <param name="requestData">Сырые байты DNS запроса</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Сырые байты DNS ответа или null в случае ошибки</returns>
    Task<byte[]?> QueryAsync(byte[] requestData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить статистику upstream запросов
    /// </summary>
    UpstreamStatistics GetStatistics();
}

/// <summary>
/// Статистика upstream запросов
/// </summary>
public sealed class UpstreamStatistics
{
    public long TotalRequests { get; set; }
    public long TotalSuccesses { get; set; }
    public long TotalFailures { get; set; }
    public long TotalTimeouts { get; set; }
    public double AverageResponseTimeMs { get; set; }
}
