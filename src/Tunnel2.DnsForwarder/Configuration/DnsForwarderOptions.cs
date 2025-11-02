namespace Tunnel2.DnsForwarder.Configuration;

/// <summary>
/// Конфигурация DNS форвардера
/// </summary>
public sealed class DnsForwarderOptions
{
    /// <summary>
    /// IPv4 адрес для прослушивания (по умолчанию 0.0.0.0)
    /// </summary>
    public string ListenIpv4 { get; set; } = "0.0.0.0";

    /// <summary>
    /// UDP порт для DNS сервера (по умолчанию 53)
    /// </summary>
    public int UdpPort { get; set; } = 53;

    /// <summary>
    /// TCP порт для DNS сервера (по умолчанию 53)
    /// </summary>
    public int TcpPort { get; set; } = 53;

    /// <summary>
    /// Адрес upstream DNS сервера (primary DNS)
    /// </summary>
    public string UpstreamDnsAddress { get; set; } = string.Empty;

    /// <summary>
    /// UDP порт upstream DNS сервера
    /// </summary>
    public int UpstreamDnsPort { get; set; } = 53;

    /// <summary>
    /// Таймаут запроса к upstream DNS (в секундах)
    /// </summary>
    public int UpstreamTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Количество retry попыток к upstream DNS
    /// </summary>
    public int UpstreamRetryCount { get; set; } = 2;

    /// <summary>
    /// IP адрес для health check ответов (127.0.0.1 по умолчанию)
    /// </summary>
    public string HealthCheckIpAddress { get; set; } = "127.0.0.1";
}
