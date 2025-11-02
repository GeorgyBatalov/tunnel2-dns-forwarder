using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;

namespace Tunnel2.DnsForwarder.IntegrationTests.Helpers;

/// <summary>
/// Raw UDP DNS клиент для integration тестов (обходит все кэши)
/// </summary>
public sealed class RawDnsClient
{
    private readonly IPEndPoint _dnsEndpoint;

    public RawDnsClient(string dnsServerAddress, int dnsServerPort)
    {
        _dnsEndpoint = new IPEndPoint(IPAddress.Parse(dnsServerAddress), dnsServerPort);
    }

    /// <summary>
    /// Выполнить A запрос
    /// </summary>
    public async Task<DnsResponse> QueryARecordAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        using var udpClient = new UdpClient();

        // Строим DNS запрос
        Message request = new Message
        {
            Id = (ushort)Random.Shared.Next(0, 65535),
            Opcode = MessageOperation.Query,
            RD = true // Recursion Desired
        };

        request.Questions.Add(new Question
        {
            Name = hostname,
            Type = DnsType.A,
            Class = DnsClass.IN
        });

        byte[] requestData = request.ToByteArray();

        // Отправляем запрос
        await udpClient.SendAsync(requestData, requestData.Length, _dnsEndpoint);

        // Ждем ответ с таймаутом
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            UdpReceiveResult result = await udpClient.ReceiveAsync(linkedCts.Token);

            // Парсим ответ
            Message response = new Message();
            response.Read(result.Buffer, 0, result.Buffer.Length);

            return new DnsResponse
            {
                Status = response.Status,
                Answers = response.Answers.ToList(),
                IsSuccess = response.Status == MessageStatus.NoError && response.Answers.Count > 0
            };
        }
        catch (OperationCanceledException)
        {
            return new DnsResponse
            {
                Status = MessageStatus.ServerFailure,
                Answers = new List<ResourceRecord>(),
                IsSuccess = false,
                IsTimeout = true
            };
        }
    }

    /// <summary>
    /// Извлечь IP адрес из A записи
    /// </summary>
    public static string? ExtractIpAddress(DnsResponse response)
    {
        if (!response.IsSuccess)
        {
            return null;
        }

        ARecord? aRecord = response.Answers.OfType<ARecord>().FirstOrDefault();
        return aRecord?.Address.ToString();
    }
}

/// <summary>
/// DNS ответ
/// </summary>
public sealed class DnsResponse
{
    public required MessageStatus Status { get; init; }
    public required List<ResourceRecord> Answers { get; init; }
    public required bool IsSuccess { get; init; }
    public bool IsTimeout { get; init; }
}
