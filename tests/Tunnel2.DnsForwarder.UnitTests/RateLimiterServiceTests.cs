using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tunnel2.DnsForwarder.Configuration;
using Tunnel2.DnsForwarder.Services;

namespace Tunnel2.DnsForwarder.UnitTests;

public class RateLimiterServiceTests : IDisposable
{
    private readonly Mock<ILogger<RateLimiterService>> _loggerMock;
    private readonly Mock<IOptionsMonitor<RateLimitOptions>> _optionsMonitorMock;
    private readonly RateLimiterService _rateLimiter;

    public RateLimiterServiceTests()
    {
        _loggerMock = new Mock<ILogger<RateLimiterService>>();
        _optionsMonitorMock = new Mock<IOptionsMonitor<RateLimitOptions>>();

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(new RateLimitOptions
        {
            IsEnabled = true,
            MaxRequestsPerIp = 10,
            TimeWindowSeconds = 60,
            MaxTrackedIps = 100
        });

        _rateLimiter = new RateLimiterService(_loggerMock.Object, _optionsMonitorMock.Object);
    }

    [Fact]
    public void IsAllowed_WhenDisabled_ShouldAlwaysReturnTrue()
    {
        // Arrange
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(new RateLimitOptions { IsEnabled = false });
        IPAddress testIp = IPAddress.Parse("192.168.1.1");

        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            _rateLimiter.IsAllowed(testIp).Should().BeTrue();
        }
    }

    [Fact]
    public void IsAllowed_WhenUnderLimit_ShouldReturnTrue()
    {
        // Arrange
        IPAddress testIp = IPAddress.Parse("192.168.1.1");

        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            bool result = _rateLimiter.IsAllowed(testIp);
            result.Should().BeTrue($"request {i + 1} should be allowed");
        }
    }

    [Fact]
    public void IsAllowed_WhenOverLimit_ShouldReturnFalse()
    {
        // Arrange
        IPAddress testIp = IPAddress.Parse("192.168.1.1");

        // Act - send 10 allowed requests
        for (int i = 0; i < 10; i++)
        {
            _rateLimiter.IsAllowed(testIp);
        }

        // Assert - 11th request should be blocked
        bool result = _rateLimiter.IsAllowed(testIp);
        result.Should().BeFalse("rate limit should be exceeded");
    }

    [Fact]
    public void IsAllowed_DifferentIps_ShouldTrackSeparately()
    {
        // Arrange
        IPAddress ip1 = IPAddress.Parse("192.168.1.1");
        IPAddress ip2 = IPAddress.Parse("192.168.1.2");

        // Act - send 10 requests from each IP
        for (int i = 0; i < 10; i++)
        {
            _rateLimiter.IsAllowed(ip1);
            _rateLimiter.IsAllowed(ip2);
        }

        // Assert - both should be at limit, next request blocked
        _rateLimiter.IsAllowed(ip1).Should().BeFalse();
        _rateLimiter.IsAllowed(ip2).Should().BeFalse();
    }

    [Fact]
    public void GetStatistics_ShouldTrackAllowedAndBlocked()
    {
        // Arrange
        IPAddress testIp = IPAddress.Parse("192.168.1.1");

        // Act - 10 allowed + 5 blocked
        for (int i = 0; i < 10; i++)
        {
            _rateLimiter.IsAllowed(testIp);
        }

        for (int i = 0; i < 5; i++)
        {
            _rateLimiter.IsAllowed(testIp);
        }

        RateLimitStatistics stats = _rateLimiter.GetStatistics();

        // Assert
        stats.TotalAllowed.Should().Be(10);
        stats.TotalBlocked.Should().Be(5);
        stats.CurrentTrackedIps.Should().Be(1);
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
