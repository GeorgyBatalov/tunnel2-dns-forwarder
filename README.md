# Tunnel2 DNS Forwarder

Lightweight DNS forwarder/secondary DNS server with caching and DDoS protection for the Tunnel2 infrastructure.

## Features

### Core Functionality
- **DNS Forwarding**: Receives DNS queries and forwards them to upstream primary DNS server
- **Dual Cache System**:
  - Positive cache for successful responses (configurable TTL up to DNS response TTL)
  - Negative cache for NXDOMAIN responses (shorter sliding window)
- **DDoS Protection**: Per-IP rate limiting with sliding window
- **Windows Service Support**: Can run as Windows Service or Linux systemd daemon
- **High Performance**: Async UDP handling with concurrent request processing

### Technical Features
- Built on .NET 8.0
- Uses Makaretu.Dns for DNS protocol handling
- Microsoft.Extensions.Hosting for service lifecycle
- In-memory caching with Microsoft.Extensions.Caching.Memory
- Comprehensive logging with structured logging support

## Architecture

```
┌─────────────┐      UDP Query      ┌──────────────────┐
│  DNS Client │ ─────────────────>  │  DNS Forwarder   │
│   (User)    │                     │   (This Service) │
└─────────────┘                     └──────────────────┘
                                            │
                                            │ Check Cache
                                            ▼
                                    ┌──────────────┐
                                    │  Dual Cache  │
                                    │  + / -       │
                                    └──────────────┘
                                            │
                                            │ Cache Miss
                                            ▼
                                    ┌──────────────────┐
                                    │  Rate Limiter    │
                                    │  (DDoS Guard)    │
                                    └──────────────────┘
                                            │
                                            │ Forward
                                            ▼
                                    ┌──────────────────┐
                                    │  Upstream DNS    │
                                    │ (Primary/Master) │
                                    └──────────────────┘
```

## Configuration

### appsettings.json

```json
{
  "DnsForwarder": {
    "ListenIpv4": "0.0.0.0",
    "UdpPort": 53,
    "UpstreamDnsAddress": "10.0.0.10",
    "UpstreamDnsPort": 53,
    "UpstreamTimeoutSeconds": 5,
    "UpstreamRetryCount": 2
  },
  "Cache": {
    "IsEnabled": true,
    "MaxPositiveCacheTtlSeconds": 300,
    "NegativeCacheTtlSeconds": 60,
    "MaxCacheSize": 10000,
    "AbsoluteCacheExpirationSeconds": 3600
  },
  "RateLimit": {
    "IsEnabled": true,
    "MaxRequestsPerIp": 100,
    "TimeWindowSeconds": 60,
    "MaxTrackedIps": 10000
  }
}
```

### Configuration Options

#### DnsForwarder
- `ListenIpv4`: IPv4 address to listen on (default: `0.0.0.0`)
- `UdpPort`: UDP port for DNS server (default: `53`)
- `UpstreamDnsAddress`: Address of upstream DNS server (required)
- `UpstreamDnsPort`: Port of upstream DNS server (default: `53`)
- `UpstreamTimeoutSeconds`: Timeout for upstream queries (default: `5`)
- `UpstreamRetryCount`: Number of retry attempts (default: `2`)

#### Cache
- `IsEnabled`: Enable/disable caching (default: `true`)
- `MaxPositiveCacheTtlSeconds`: Max TTL for successful responses (default: `300`)
- `NegativeCacheTtlSeconds`: TTL for NXDOMAIN responses (default: `60`)
- `MaxCacheSize`: Maximum number of cached entries (default: `10000`)
- `AbsoluteCacheExpirationSeconds`: Absolute expiration time (default: `3600`)

#### RateLimit
- `IsEnabled`: Enable/disable rate limiting (default: `true`)
- `MaxRequestsPerIp`: Max requests per IP in time window (default: `100`)
- `TimeWindowSeconds`: Time window for rate limiting (default: `60`)
- `MaxTrackedIps`: Max number of IPs to track (default: `10000`)

## Building

### Prerequisites
- .NET 8.0 SDK or later

### Build Commands

```bash
# Build in Release mode
dotnet build -c Release

# Run tests
dotnet test -c Release

# Publish for deployment
dotnet publish src/Tunnel2.DnsForwarder -c Release -o publish
```

## Running

### Console Application (Development)

```bash
dotnet run --project src/Tunnel2.DnsForwarder
```

### Windows Service

```bash
# Install as Windows Service
sc create Tunnel2DnsForwarder binPath="C:\path\to\Tunnel2.DnsForwarder.exe"

# Start service
sc start Tunnel2DnsForwarder

# Stop service
sc stop Tunnel2DnsForwarder

# Delete service
sc delete Tunnel2DnsForwarder
```

### Linux Systemd

Create `/etc/systemd/system/tunnel2-dns-forwarder.service`:

```ini
[Unit]
Description=Tunnel2 DNS Forwarder
After=network.target

[Service]
Type=notify
ExecStart=/usr/local/bin/Tunnel2.DnsForwarder
Restart=always
User=tunnel2
WorkingDirectory=/opt/tunnel2-dns-forwarder

[Install]
WantedBy=multi-user.target
```

```bash
# Enable and start
sudo systemctl enable tunnel2-dns-forwarder
sudo systemctl start tunnel2-dns-forwarder

# Check status
sudo systemctl status tunnel2-dns-forwarder
```

## Testing

The project includes comprehensive test coverage:

- **Unit Tests** (10 tests): Core component logic
  - DnsCacheService tests
  - RateLimiterService tests

- **Integration Tests** (17 tests): End-to-end scenarios
  - DNS query forwarding (6 tests)
  - Cache hit/miss scenarios
  - Negative response caching
  - Concurrent request handling
  - Rate limiting enforcement (3 tests)
  - Health check functionality (8 tests)

### Run Tests

```bash
# All tests
dotnet test

# Unit tests only
dotnet test tests/Tunnel2.DnsForwarder.UnitTests

# Integration tests only
dotnet test tests/Tunnel2.DnsForwarder.IntegrationTests
```

**Current Status**: ✅ 27/27 tests passing (100%)

## Performance Characteristics

- **Cache Hit Latency**: < 1ms (in-memory lookup)
- **Cache Miss Latency**: Depends on upstream DNS (typically 5-50ms)
- **Concurrent Requests**: Handled asynchronously with fire-and-forget pattern
- **Memory Usage**: ~50MB base + cache (configurable max size)
- **Rate Limiting Overhead**: < 0.1ms per request (concurrent dictionary lookup)

## Monitoring

The service logs key metrics at INFO level:
- Cache hits/misses
- Upstream query success/failures
- Rate limit violations
- Configuration on startup

Statistics are available via service interfaces:
- `IDnsCacheService.GetStatistics()` - cache metrics
- `IRateLimiter.GetStatistics()` - rate limit metrics
- `IUpstreamDnsClient.GetStatistics()` - upstream metrics

## Security Considerations

1. **Rate Limiting**: Protects against DNS amplification attacks
2. **No Recursion**: Only forwards to configured upstream (not a recursive resolver)
3. **Input Validation**: All DNS queries validated via Makaretu.Dns library
4. **Timeout Handling**: Prevents resource exhaustion from slow upstream servers
5. **Memory Limits**: Cache size limits prevent memory exhaustion

## Production Deployment

### Recommended Configuration

```json
{
  "DnsForwarder": {
    "ListenIpv4": "0.0.0.0",
    "UdpPort": 53,
    "UpstreamDnsAddress": "<primary-dns-ip>",
    "UpstreamTimeoutSeconds": 5,
    "UpstreamRetryCount": 2
  },
  "Cache": {
    "IsEnabled": true,
    "MaxPositiveCacheTtlSeconds": 300,
    "NegativeCacheTtlSeconds": 60,
    "MaxCacheSize": 50000,
    "AbsoluteCacheExpirationSeconds": 3600
  },
  "RateLimit": {
    "IsEnabled": true,
    "MaxRequestsPerIp": 1000,
    "TimeWindowSeconds": 60,
    "MaxTrackedIps": 100000
  }
}
```

### Firewall Rules

```bash
# Allow DNS queries (UDP)
sudo ufw allow 53/udp

# Or with iptables
sudo iptables -A INPUT -p udp --dport 53 -j ACCEPT
```

### Health Checks

DNS Forwarder поддерживает health check через специальные DNS запросы. **Не требуется отдельный HTTP порт!**

#### Стандартные health check домены:
- `health.check`
- `_health`
- `health`
- `healthcheck`

#### Примеры использования:

```bash
# Using dig
dig @localhost health.check A

# Using nslookup
nslookup health.check localhost

# С конкретным портом
dig @localhost -p 53 health.check A
```

**Ожидаемый ответ:**
- Status: `NOERROR`
- A record: `127.0.0.1` (по умолчанию, настраивается через `HealthCheckIpAddress`)
- TTL: 1 секунда

#### Docker Compose health check:

```yaml
services:
  dns-forwarder:
    image: tunnel2-dns-forwarder:latest
    healthcheck:
      test: ["CMD", "dig", "@127.0.0.1", "health.check", "A", "+short"]
      interval: 30s
      timeout: 3s
      retries: 3
      start_period: 5s
```

#### Kubernetes liveness probe:

```yaml
livenessProbe:
  exec:
    command:
    - /bin/sh
    - -c
    - 'dig @127.0.0.1 health.check A +short | grep -q "127.0.0.1"'
  initialDelaySeconds: 5
  periodSeconds: 10
  timeoutSeconds: 3
  failureThreshold: 3
```

#### Особенности health check:
- ✅ Работает на том же DNS порту (не нужен отдельный HTTP порт)
- ✅ Не форвардится к upstream DNS
- ✅ Не кэшируется (всегда fresh response)
- ✅ Короткий TTL (1 секунда)
- ✅ Не учитывается в rate limiting
- ✅ Поддерживает любые поддомены (например, `test.health.check`)

#### Настройка в appsettings.json:

```json
{
  "DnsForwarder": {
    "HealthCheckIpAddress": "127.0.0.1"
  }
}
```

Вы можете изменить IP адрес, который возвращается в health check ответах, установив `HealthCheckIpAddress`. Это полезно для мониторинга в сложных сетевых конфигурациях.

## Troubleshooting

### Service won't start
- Check if port 53 is already in use: `netstat -an | grep :53`
- Verify upstream DNS address is reachable
- Check logs for configuration errors

### High memory usage
- Reduce `MaxCacheSize` in configuration
- Reduce `MaxTrackedIps` for rate limiter
- Enable cache expiration monitoring

### Slow responses
- Check upstream DNS latency
- Increase `UpstreamTimeoutSeconds` if needed
- Verify cache is enabled and working

## License

Part of the Tunnel2 infrastructure project.

## Related Components

- **tunnel2-dns-server**: Primary authoritative DNS server
- **tunnel2-server**: Control plane (TunnelServer)
- **tunnel-proxy-entry**: Data plane (ProxyEntry)
- **tunnel2-client**: Client application
