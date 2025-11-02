using FluentAssertions;
using Makaretu.Dns;
using Tunnel2.DnsForwarder.IntegrationTests.Helpers;

namespace Tunnel2.DnsForwarder.IntegrationTests;

/// <summary>
/// End-to-end integration тесты DNS форвардера
/// </summary>
public class DnsForwarderIntegrationTests : DnsForwarderTestBase
{
    [Fact]
    public async Task Query_ExistingRecord_ShouldReturnCorrectIp()
    {
        // Arrange
        MockUpstreamServer.AddARecord("example.com", "192.0.2.1");

        // Act
        DnsResponse response = await DnsClient.QueryARecordAsync("example.com");

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Status.Should().Be(MessageStatus.NoError);
        response.Answers.Should().HaveCount(1);

        string? ipAddress = RawDnsClient.ExtractIpAddress(response);
        ipAddress.Should().Be("192.0.2.1");

        // Проверяем что upstream сервер получил запрос
        MockUpstreamServer.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task Query_NonExistentRecord_ShouldReturnNxdomain()
    {
        // Act
        DnsResponse response = await DnsClient.QueryARecordAsync("nonexistent.test");

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Status.Should().Be(MessageStatus.NameError);
        response.Answers.Should().BeEmpty();
        MockUpstreamServer.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task Query_CachedRecord_ShouldNotForwardToUpstream()
    {
        // Arrange
        MockUpstreamServer.AddARecord("cached.com", "192.0.2.2");

        // Act - первый запрос (cache miss)
        DnsResponse response1 = await DnsClient.QueryARecordAsync("cached.com");
        int requestCountAfterFirst = MockUpstreamServer.RequestCount;

        // Act - второй запрос (cache hit)
        DnsResponse response2 = await DnsClient.QueryARecordAsync("cached.com");
        int requestCountAfterSecond = MockUpstreamServer.RequestCount;

        // Assert
        response1.IsSuccess.Should().BeTrue();
        response2.IsSuccess.Should().BeTrue();

        string? ip1 = RawDnsClient.ExtractIpAddress(response1);
        string? ip2 = RawDnsClient.ExtractIpAddress(response2);

        ip1.Should().Be("192.0.2.2");
        ip2.Should().Be("192.0.2.2");

        // Upstream должен получить только 1 запрос (второй из кэша)
        requestCountAfterFirst.Should().Be(1);
        requestCountAfterSecond.Should().Be(1, "second request should be served from cache");
    }

    [Fact]
    public async Task Query_MultipleRecords_ShouldCacheIndependently()
    {
        // Arrange
        MockUpstreamServer.AddARecord("example1.com", "192.0.2.10");
        MockUpstreamServer.AddARecord("example2.com", "192.0.2.11");
        MockUpstreamServer.AddARecord("example3.com", "192.0.2.12");

        // Act
        DnsResponse response1 = await DnsClient.QueryARecordAsync("example1.com");
        DnsResponse response2 = await DnsClient.QueryARecordAsync("example2.com");
        DnsResponse response3 = await DnsClient.QueryARecordAsync("example3.com");

        // Повторные запросы (должны быть из кэша)
        DnsResponse response1Cached = await DnsClient.QueryARecordAsync("example1.com");
        DnsResponse response2Cached = await DnsClient.QueryARecordAsync("example2.com");

        // Assert
        RawDnsClient.ExtractIpAddress(response1).Should().Be("192.0.2.10");
        RawDnsClient.ExtractIpAddress(response2).Should().Be("192.0.2.11");
        RawDnsClient.ExtractIpAddress(response3).Should().Be("192.0.2.12");

        RawDnsClient.ExtractIpAddress(response1Cached).Should().Be("192.0.2.10");
        RawDnsClient.ExtractIpAddress(response2Cached).Should().Be("192.0.2.11");

        // Должно быть ровно 3 upstream запроса (повторные из кэша)
        MockUpstreamServer.RequestCount.Should().Be(3);
    }

    [Fact]
    public async Task Query_NegativeResponse_ShouldBeCached()
    {
        // Act - первый запрос (cache miss)
        DnsResponse response1 = await DnsClient.QueryARecordAsync("notfound.test");
        int requestCountAfterFirst = MockUpstreamServer.RequestCount;

        // Act - второй запрос (должен быть из negative cache)
        DnsResponse response2 = await DnsClient.QueryARecordAsync("notfound.test");
        int requestCountAfterSecond = MockUpstreamServer.RequestCount;

        // Assert
        response1.IsSuccess.Should().BeFalse();
        response1.Status.Should().Be(MessageStatus.NameError);

        response2.IsSuccess.Should().BeFalse();
        response2.Status.Should().Be(MessageStatus.NameError);

        // Negative response тоже должен кэшироваться
        requestCountAfterFirst.Should().Be(1);
        requestCountAfterSecond.Should().Be(1, "negative response should be cached");
    }

    [Fact]
    public async Task Query_ConcurrentRequests_ShouldHandleCorrectly()
    {
        // Arrange
        MockUpstreamServer.AddARecord("concurrent.test", "192.0.2.50");

        // Act - 10 параллельных запросов
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => DnsClient.QueryARecordAsync("concurrent.test"))
            .ToArray();

        DnsResponse[] responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            RawDnsClient.ExtractIpAddress(r).Should().Be("192.0.2.50");
        });

        // Из-за кэша upstream может получить меньше запросов чем 10
        MockUpstreamServer.RequestCount.Should().BeLessThanOrEqualTo(10);
    }
}
