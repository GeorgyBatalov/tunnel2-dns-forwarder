using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tunnel2.DnsForwarder.Configuration;

namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Клиент для отправки DNS запросов к upstream DNS серверу по UDP
/// </summary>
public sealed class UpstreamDnsClient : IUpstreamDnsClient, IDisposable
{
    private readonly ILogger<UpstreamDnsClient> _logger;
    private readonly IOptionsMonitor<DnsForwarderOptions> _dnsForwarderOptionsMonitor;
    private readonly UdpClient _udpClient;
    private long _totalRequests;
    private long _totalSuccesses;
    private long _totalFailures;
    private long _totalTimeouts;
    private long _totalResponseTimeMs;

    public UpstreamDnsClient(
        ILogger<UpstreamDnsClient> logger,
        IOptionsMonitor<DnsForwarderOptions> dnsForwarderOptionsMonitor)
    {
        _logger = logger;
        _dnsForwarderOptionsMonitor = dnsForwarderOptionsMonitor;
        _udpClient = new UdpClient();
    }

    public async Task<byte[]?> QueryAsync(byte[] requestData, CancellationToken cancellationToken = default)
    {
        DnsForwarderOptions options = _dnsForwarderOptionsMonitor.CurrentValue;

        if (string.IsNullOrEmpty(options.UpstreamDnsAddress))
        {
            _logger.LogError("Upstream DNS address is not configured");
            Interlocked.Increment(ref _totalFailures);
            return null;
        }

        IPEndPoint upstreamEndpoint = new IPEndPoint(
            IPAddress.Parse(options.UpstreamDnsAddress),
            options.UpstreamDnsPort);

        Stopwatch stopwatch = Stopwatch.StartNew();
        Interlocked.Increment(ref _totalRequests);

        for (int attempt = 0; attempt <= options.UpstreamRetryCount; attempt++)
        {
            try
            {
                _logger.LogDebug("Sending DNS query to upstream {Endpoint}, attempt {Attempt}/{MaxAttempts}",
                    upstreamEndpoint, attempt + 1, options.UpstreamRetryCount + 1);

                // Отправляем запрос
                await _udpClient.SendAsync(requestData, requestData.Length, upstreamEndpoint);

                // Ждем ответ с таймаутом
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(options.UpstreamTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                UdpReceiveResult result = await _udpClient.ReceiveAsync(linkedCts.Token);

                stopwatch.Stop();
                Interlocked.Add(ref _totalResponseTimeMs, stopwatch.ElapsedMilliseconds);
                Interlocked.Increment(ref _totalSuccesses);

                _logger.LogInformation("Received DNS response from upstream {Endpoint}, size: {Size} bytes, time: {Time}ms",
                    upstreamEndpoint, result.Buffer.Length, stopwatch.ElapsedMilliseconds);

                return result.Buffer;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("DNS query to upstream {Endpoint} was cancelled", upstreamEndpoint);
                    Interlocked.Increment(ref _totalFailures);
                    throw;
                }

                // Timeout
                _logger.LogWarning("DNS query to upstream {Endpoint} timed out (attempt {Attempt}/{MaxAttempts})",
                    upstreamEndpoint, attempt + 1, options.UpstreamRetryCount + 1);

                if (attempt == options.UpstreamRetryCount)
                {
                    Interlocked.Increment(ref _totalTimeouts);
                    Interlocked.Increment(ref _totalFailures);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Error sending DNS query to upstream {Endpoint} (attempt {Attempt}/{MaxAttempts})",
                    upstreamEndpoint, attempt + 1, options.UpstreamRetryCount + 1);

                if (attempt == options.UpstreamRetryCount)
                {
                    Interlocked.Increment(ref _totalFailures);
                }
            }

            // Небольшая задержка между retry
            if (attempt < options.UpstreamRetryCount)
            {
                await Task.Delay(100 * (attempt + 1), cancellationToken);
            }
        }

        return null;
    }

    public UpstreamStatistics GetStatistics()
    {
        long totalRequests = Interlocked.Read(ref _totalRequests);
        long totalResponseTime = Interlocked.Read(ref _totalResponseTimeMs);
        long totalSuccesses = Interlocked.Read(ref _totalSuccesses);

        return new UpstreamStatistics
        {
            TotalRequests = totalRequests,
            TotalSuccesses = totalSuccesses,
            TotalFailures = Interlocked.Read(ref _totalFailures),
            TotalTimeouts = Interlocked.Read(ref _totalTimeouts),
            AverageResponseTimeMs = totalSuccesses > 0 ? (double)totalResponseTime / totalSuccesses : 0
        };
    }

    public void Dispose()
    {
        _udpClient.Dispose();
    }
}
