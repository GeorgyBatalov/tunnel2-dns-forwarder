using FluentAssertions;
using Makaretu.Dns;
using Tunnel2.DnsForwarder.IntegrationTests.Helpers;

namespace Tunnel2.DnsForwarder.IntegrationTests;

/// <summary>
/// Integration тесты для health check через DNS запросы
/// </summary>
public class HealthCheckIntegrationTests : DnsForwarderTestBase
{
    [Theory]
    [InlineData("health.check")]
    [InlineData("_health")]
    [InlineData("health")]
    [InlineData("healthcheck")]
    public async Task HealthCheck_StandardDomains_ShouldReturnSuccess(string healthDomain)
    {
        // Act
        DnsResponse response = await DnsClient.QueryARecordAsync(healthDomain);

        // Assert
        response.IsSuccess.Should().BeTrue($"{healthDomain} should return successful health check");
        response.Status.Should().Be(MessageStatus.NoError);
        response.Answers.Should().HaveCount(1);

        string? ipAddress = RawDnsClient.ExtractIpAddress(response);
        ipAddress.Should().Be("127.0.0.1", "default health check IP");
    }

    [Fact]
    public async Task HealthCheck_WithSubdomain_ShouldReturnSuccess()
    {
        // Act
        DnsResponse response = await DnsClient.QueryARecordAsync("test.health.check");

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Status.Should().Be(MessageStatus.NoError);

        string? ipAddress = RawDnsClient.ExtractIpAddress(response);
        ipAddress.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task HealthCheck_ShouldNotForwardToUpstream()
    {
        // Arrange
        int initialUpstreamRequests = MockUpstreamServer.RequestCount;

        // Act
        await DnsClient.QueryARecordAsync("health.check");

        // Assert
        MockUpstreamServer.RequestCount.Should().Be(initialUpstreamRequests,
            "health check should not forward to upstream DNS");
    }

    [Fact]
    public async Task HealthCheck_ConcurrentRequests_ShouldHandleAll()
    {
        // Act - 10 параллельных health check запросов
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => DnsClient.QueryARecordAsync("health.check"))
            .ToArray();

        DnsResponse[] responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            RawDnsClient.ExtractIpAddress(r).Should().Be("127.0.0.1");
        });
    }

    [Fact]
    public async Task HealthCheck_ShouldNotBeCached()
    {
        // Act - два запроса подряд
        DnsResponse response1 = await DnsClient.QueryARecordAsync("health.check");
        DnsResponse response2 = await DnsClient.QueryARecordAsync("health.check");

        // Assert
        response1.IsSuccess.Should().BeTrue();
        response2.IsSuccess.Should().BeTrue();

        // TTL должен быть 1 секунда (короткий для health check)
        ARecord? aRecord1 = response1.Answers.OfType<ARecord>().FirstOrDefault();
        ARecord? aRecord2 = response2.Answers.OfType<ARecord>().FirstOrDefault();

        aRecord1.Should().NotBeNull();
        aRecord2.Should().NotBeNull();

        aRecord1!.TTL.TotalSeconds.Should().Be(1);
        aRecord2!.TTL.TotalSeconds.Should().Be(1);
    }
}
