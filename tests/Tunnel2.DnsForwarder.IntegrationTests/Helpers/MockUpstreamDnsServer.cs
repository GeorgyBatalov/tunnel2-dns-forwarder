using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;

namespace Tunnel2.DnsForwarder.IntegrationTests.Helpers;

/// <summary>
/// Mock upstream DNS сервер для integration тестов
/// </summary>
public sealed class MockUpstreamDnsServer : IDisposable
{
    private readonly UdpClient _udpListener;
    private readonly IPEndPoint _listenEndpoint;
    private readonly CancellationTokenSource _cts;
    private readonly Task _listenerTask;
    private readonly Dictionary<string, IPAddress> _records;
    private int _requestCount;

    public int Port => _listenEndpoint.Port;
    public int RequestCount => _requestCount;

    public MockUpstreamDnsServer(int port = 0)
    {
        _records = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
        _listenEndpoint = new IPEndPoint(IPAddress.Loopback, port);
        _udpListener = new UdpClient(_listenEndpoint);
        _listenEndpoint = (IPEndPoint)_udpListener.Client.LocalEndPoint!;
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(ListenAsync);
    }

    /// <summary>
    /// Добавить A запись в mock DNS сервер
    /// </summary>
    public void AddARecord(string hostname, string ipAddress)
    {
        _records[hostname.TrimEnd('.')] = IPAddress.Parse(ipAddress);
    }

    /// <summary>
    /// Очистить все записи
    /// </summary>
    public void ClearRecords()
    {
        _records.Clear();
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await _udpListener.ReceiveAsync(_cts.Token);
                Interlocked.Increment(ref _requestCount);

                // Обрабатываем DNS запрос
                byte[] responseData = ProcessDnsRequest(result.Buffer);

                // Отправляем ответ
                await _udpListener.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Ignore errors during tests
        }
    }

    private byte[] ProcessDnsRequest(byte[] requestData)
    {
        Message request = new Message();
        request.Read(requestData, 0, requestData.Length);

        Message response = request.CreateResponse();

        if (request.Questions.Count > 0)
        {
            Question question = request.Questions[0];
            string hostname = question.Name.ToString().TrimEnd('.');

            if (question.Type == DnsType.A && _records.TryGetValue(hostname, out IPAddress? ipAddress))
            {
                // Successful response
                ARecord aRecord = new ARecord
                {
                    Name = question.Name,
                    Address = ipAddress,
                    TTL = TimeSpan.FromSeconds(60)
                };

                response.Answers.Add(aRecord);
                response.Status = MessageStatus.NoError;
            }
            else
            {
                // NXDOMAIN
                response.Status = MessageStatus.NameError;
            }
        }

        return response.ToByteArray();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listenerTask.Wait(TimeSpan.FromSeconds(5));
        _cts.Dispose();
        _udpListener.Dispose();
    }
}
