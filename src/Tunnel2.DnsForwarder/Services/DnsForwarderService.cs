using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tunnel2.DnsForwarder.Configuration;
using Tunnel2.DnsForwarder.Models;

namespace Tunnel2.DnsForwarder.Services;

/// <summary>
/// Главный сервис DNS форвардера (Background Service)
/// Принимает UDP запросы, проверяет кэш, форвардит к upstream DNS
/// </summary>
public sealed class DnsForwarderService : BackgroundService
{
    private readonly ILogger<DnsForwarderService> _logger;
    private readonly IOptionsMonitor<DnsForwarderOptions> _dnsForwarderOptionsMonitor;
    private readonly IDnsCacheService _cacheService;
    private readonly IRateLimiter _rateLimiter;
    private readonly IUpstreamDnsClient _upstreamClient;
    private readonly IHealthCheckService _healthCheckService;
    private UdpClient? _udpListener;

    public DnsForwarderService(
        ILogger<DnsForwarderService> logger,
        IOptionsMonitor<DnsForwarderOptions> dnsForwarderOptionsMonitor,
        IDnsCacheService cacheService,
        IRateLimiter rateLimiter,
        IUpstreamDnsClient upstreamClient,
        IHealthCheckService healthCheckService)
    {
        _logger = logger;
        _dnsForwarderOptionsMonitor = dnsForwarderOptionsMonitor;
        _cacheService = cacheService;
        _rateLimiter = rateLimiter;
        _upstreamClient = upstreamClient;
        _healthCheckService = healthCheckService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DnsForwarderOptions options = _dnsForwarderOptionsMonitor.CurrentValue;

        try
        {
            IPEndPoint listenEndpoint = new IPEndPoint(
                IPAddress.Parse(options.ListenIpv4),
                options.UdpPort);

            _udpListener = new UdpClient(listenEndpoint);

            _logger.LogInformation("DNS Forwarder started, listening on {Endpoint}", listenEndpoint);
            _logger.LogInformation("Upstream DNS: {UpstreamAddress}:{UpstreamPort}",
                options.UpstreamDnsAddress, options.UpstreamDnsPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Получаем UDP запрос
                    UdpReceiveResult result = await _udpListener.ReceiveAsync(stoppingToken);

                    // Обрабатываем запрос асинхронно (fire and forget)
                    _ = Task.Run(() => HandleDnsRequestAsync(result, stoppingToken), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error receiving DNS request");
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Fatal error in DNS Forwarder service");
            throw;
        }
        finally
        {
            _udpListener?.Dispose();
            _logger.LogInformation("DNS Forwarder stopped");
        }
    }

    private async Task HandleDnsRequestAsync(UdpReceiveResult udpResult, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        IPEndPoint clientEndpoint = udpResult.RemoteEndPoint;
        byte[] requestData = udpResult.Buffer;

        try
        {
            // Rate limiting
            if (!_rateLimiter.IsAllowed(clientEndpoint.Address))
            {
                _logger.LogWarning("Rate limit exceeded for {ClientIp}, dropping request", clientEndpoint.Address);
                return;
            }

            // Парсим DNS запрос
            Message request = new Message();
            request.Read(requestData, 0, requestData.Length);

            if (request.Questions.Count == 0)
            {
                _logger.LogWarning("Received DNS request with no questions from {ClientIp}", clientEndpoint.Address);
                return;
            }

            Question question = request.Questions[0];
            string hostname = question.Name.ToString().TrimEnd('.');

            _logger.LogDebug("DNS query from {ClientIp}: {Name} {Type}",
                clientEndpoint.Address, question.Name, question.Type);

            // 0. Health check запрос
            if (_healthCheckService.IsHealthCheckQuery(hostname))
            {
                _logger.LogInformation("Health check query from {ClientIp}: {Name}",
                    clientEndpoint.Address, hostname);

                byte[] healthResponse = CreateHealthCheckResponse(request);
                if (_udpListener != null)
                {
                    await _udpListener.SendAsync(healthResponse, healthResponse.Length, clientEndpoint);
                }
                return;
            }

            // 1. Проверяем кэш
            string cacheKey = BuildCacheKey(question);
            CachedDnsResponse? cachedResponse = _cacheService.Get(cacheKey);

            byte[]? responseData;

            if (cachedResponse != null)
            {
                // Кэш hit - возвращаем кэшированный ответ
                responseData = cachedResponse.ResponseData;
                _logger.LogInformation("Cache HIT for {Name} {Type} from {ClientIp}",
                    question.Name, question.Type, clientEndpoint.Address);
            }
            else
            {
                // Кэш miss - форвардим к upstream DNS
                _logger.LogInformation("Cache MISS for {Name} {Type}, forwarding to upstream",
                    question.Name, question.Type);

                responseData = await _upstreamClient.QueryAsync(requestData, cancellationToken);

                if (responseData != null)
                {
                    // Парсим ответ для определения TTL и типа записи
                    Message response = new Message();
                    response.Read(responseData, 0, responseData.Length);

                    // Определяем тип записи (positive/negative)
                    CacheEntryType entryType = DetermineEntryType(response);
                    int ttl = GetMinimalTtl(response);

                    // Кэшируем ответ
                    _cacheService.Set(cacheKey, new CachedDnsResponse
                    {
                        ResponseData = responseData,
                        OriginalTtl = ttl,
                        CachedAt = DateTime.UtcNow,
                        EntryType = entryType
                    });

                    _logger.LogInformation("Upstream response for {Name} {Type}: status={Status}, answers={AnswerCount}, TTL={Ttl}",
                        question.Name, question.Type, response.Status, response.Answers.Count, ttl);
                }
                else
                {
                    _logger.LogError("Failed to get response from upstream DNS for {Name} {Type}",
                        question.Name, question.Type);
                    return;
                }
            }

            // Отправляем ответ клиенту
            if (responseData != null && _udpListener != null)
            {
                await _udpListener.SendAsync(responseData, responseData.Length, clientEndpoint);

                stopwatch.Stop();
                _logger.LogDebug("Sent DNS response to {ClientIp}, size: {Size} bytes, total time: {Time}ms",
                    clientEndpoint.Address, responseData.Length, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error handling DNS request from {ClientIp}", clientEndpoint.Address);
        }
    }

    private byte[] CreateHealthCheckResponse(Message request)
    {
        DnsForwarderOptions options = _dnsForwarderOptionsMonitor.CurrentValue;
        Message response = request.CreateResponse();

        Question question = request.Questions[0];

        // Возвращаем A запись с configured IP адресом для health check
        if (question.Type == DnsType.A)
        {
            ARecord aRecord = new ARecord
            {
                Name = question.Name,
                Address = IPAddress.Parse(options.HealthCheckIpAddress),
                TTL = TimeSpan.FromSeconds(1) // Короткий TTL для health check
            };

            response.Answers.Add(aRecord);
            response.Status = MessageStatus.NoError;
        }
        else
        {
            // Для других типов запросов возвращаем пустой успешный ответ
            response.Status = MessageStatus.NoError;
        }

        return response.ToByteArray();
    }

    private static string BuildCacheKey(Question question)
    {
        // Ключ: hostname + query type
        return $"{question.Name}|{question.Type}";
    }

    private static CacheEntryType DetermineEntryType(Message response)
    {
        // Positive: NOERROR с записями
        // Negative: NXDOMAIN или NOERROR без записей
        if (response.Status == MessageStatus.NoError && response.Answers.Count > 0)
        {
            return CacheEntryType.Positive;
        }

        return CacheEntryType.Negative;
    }

    private static int GetMinimalTtl(Message response)
    {
        // Берем минимальный TTL из всех ответов
        if (response.Answers.Count == 0)
        {
            return 60; // Default TTL для negative responses
        }

        int minTtl = int.MaxValue;

        foreach (ResourceRecord answer in response.Answers)
        {
            int ttlSeconds = (int)answer.TTL.TotalSeconds;
            if (ttlSeconds < minTtl)
            {
                minTtl = ttlSeconds;
            }
        }

        return minTtl > 0 ? minTtl : 60;
    }

    public override void Dispose()
    {
        _udpListener?.Dispose();
        base.Dispose();
    }
}
