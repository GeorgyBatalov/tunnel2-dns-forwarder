# Tunnel2 DNS Forwarder - Roadmap

## Обзор

**Tunnel2 DNS Forwarder** - легковесный Secondary DNS сервер с кэшированием для репликации запросов к Primary DNS (`tunnel2-dns-server`).

### Архитектура
```
┌─────────────┐         ┌──────────────────┐         ┌─────────────────┐
│   Client    │────────>│  DNS Forwarder   │────────>│  Primary DNS    │
│  (browser)  │<────────│  (Secondary)     │<────────│  (tunnel2-dns)  │
└─────────────┘         └──────────────────┘         └─────────────────┘
                              │
                              ▼
                        ┌──────────────┐
                        │ Dual Cache   │
                        │ Positive +   │
                        │ Negative     │
                        └──────────────┘
```

### Технологический стек

- **DNS библиотека**: Makaretu.Dns (та же, что в tunnel2-dns-server)
- **Hosting**: Microsoft.Extensions.Hosting (Windows Service + Console)
- **Cache**: Microsoft.Extensions.Caching.Memory (двойной кэш)
- **Anti-DDoS**: Rate limiting per IP (sliding window)
- **Тестирование**: xUnit + FluentAssertions + Moq

### Зависимости (минимальные)

```xml
<PackageReference Include="Makaretu.Dns" Version="4.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting.Systemd" Version="8.0.0" />
```

## Структура проекта

```
tunnel2-dns-forwarder/
├── src/
│   └── Tunnel2.DnsForwarder/
│       ├── Program.cs                          # Entry point (console/service)
│       ├── Services/
│       │   ├── DnsForwarderService.cs          # Main DNS UDP listener
│       │   ├── DnsCacheService.cs              # Dual cache (positive/negative)
│       │   ├── DnsUpstreamClient.cs            # Client to primary DNS
│       │   └── RateLimiterService.cs           # DDoS protection
│       ├── Configuration/
│       │   └── DnsForwarderOptions.cs          # Settings
│       └── Tunnel2.DnsForwarder.csproj
├── tests/
│   ├── Tunnel2.DnsForwarder.UnitTests/
│   │   ├── DnsCacheServiceTests.cs
│   │   ├── RateLimiterServiceTests.cs
│   │   └── DnsUpstreamClientTests.cs
│   └── Tunnel2.DnsForwarder.IntegrationTests/
│       ├── DnsForwarderIntegrationTests.cs
│       └── MockPrimaryDnsServer.cs
├── Tunnel2.DnsForwarder.sln
├── ROADMAP.md
└── README.md
```

## Конфигурация

```json
{
  "DnsForwarder": {
    "ListenAddress": "0.0.0.0",
    "ListenPort": 53,

    "Upstream": {
      "Host": "127.0.0.1",
      "Port": 12053,
      "TimeoutSeconds": 5,
      "RetryCount": 3
    },

    "Cache": {
      "Positive": {
        "Enabled": true,
        "MaxTtlSeconds": 300,
        "SlidingExpirationSeconds": 60
      },
      "Negative": {
        "Enabled": true,
        "TtlSeconds": 30,
        "SlidingExpirationSeconds": 10
      }
    },

    "RateLimiting": {
      "Enabled": true,
      "MaxRequestsPerIp": 100,
      "WindowSeconds": 10
    }
  }
}
```

## Roadmap

### ✅ Фаза 1: Базовая функциональность (2-3 дня)

#### День 1: Инфраструктура
- [ ] Создать GitHub репозиторий `tunnel2-dns-forwarder`
- [ ] Создать solution и проекты
- [ ] Настроить Program.cs с поддержкой Windows Service/Console
- [ ] Конфигурация через appsettings.json
- [ ] **Тесты**: Проверка загрузки конфигурации

#### День 2: DNS Forwarder Core
- [ ] UDP DNS listener (DnsForwarderService)
- [ ] DNS upstream client (DnsUpstreamClient)
- [ ] Парсинг запросов/ответов через Makaretu.Dns
- [ ] **Unit тесты**: Парсинг DNS пакетов
- [ ] **Integration тесты**: Mock primary DNS server

#### День 3: Кэширование
- [ ] Реализовать DnsCacheService с двойным кэшем
- [ ] Логика: min(dns_ttl, max_configured_ttl)
- [ ] Негативный кэш для NXDOMAIN
- [ ] **Unit тесты**: Cache hit/miss, TTL expiration
- [ ] **Unit тесты**: Negative cache отдельно

### ✅ Фаза 2: Production-ready (2-3 дня)

#### День 4: DDoS Protection
- [ ] Реализовать RateLimiterService
- [ ] Sliding window per IP
- [ ] Возврат SERVFAIL при превышении лимита
- [ ] **Unit тесты**: Rate limiting logic
- [ ] **Integration тесты**: Проверка блокировки при превышении

#### День 5: Observability & Reliability
- [ ] Structured logging
- [ ] Graceful shutdown
- [ ] Health check endpoint (HTTP на порту 8080)
- [ ] Error handling и retry logic
- [ ] **Integration тесты**: Недоступность primary DNS

#### День 6: Финальная интеграция
- [ ] End-to-end тестирование с реальным tunnel2-dns-server
- [ ] Нагрузочное тестирование
- [ ] Документация (README, deployment guide)
- [ ] Примеры конфигурации для Windows Service

### 📊 Покрытие тестами (цель: >80%)

#### Unit Tests
- ✅ DnsCacheService (позитивный кэш)
- ✅ DnsCacheService (негативный кэш)
- ✅ RateLimiterService (rate limiting)
- ✅ DnsUpstreamClient (retry logic)
- ✅ Configuration loading

#### Integration Tests
- ✅ Full DNS query flow (cache miss → upstream → cache hit)
- ✅ NXDOMAIN handling и negative cache
- ✅ Rate limiting блокировка
- ✅ Primary DNS unavailable fallback
- ✅ Concurrent requests под нагрузкой

## Deployment

### Консольное приложение
```bash
dotnet run --project src/Tunnel2.DnsForwarder
```

### Windows Service
```powershell
# Опубликовать
dotnet publish -c Release -r win-x64 --self-contained

# Создать службу
sc.exe create Tunnel2DnsForwarder binPath="C:\path\to\Tunnel2.DnsForwarder.exe"
sc.exe start Tunnel2DnsForwarder
```

### Linux systemd
```bash
# Опубликовать
dotnet publish -c Release -r linux-x64 --self-contained

# Создать systemd unit
sudo systemctl enable tunnel2-dns-forwarder.service
sudo systemctl start tunnel2-dns-forwarder.service
```

## Ключевые функции

### Двойной кэш
- **Позитивный кэш**: Успешные DNS ответы с TTL из ответа, но не более MaxTtlSeconds
- **Негативный кэш**: NXDOMAIN/SERVFAIL с фиксированным коротким TTL (30 сек)

### Anti-DDoS
- Rate limiting per source IP
- Sliding window (100 запросов за 10 секунд)
- Автоматическая блокировка при превышении

### Надёжность
- Retry механизм для upstream запросов
- Graceful shutdown
- Health checks
- Circuit breaker pattern (опционально)

## Метрики и мониторинг

- Cache hit rate (positive/negative)
- Query latency
- Upstream availability
- Rate limiting blocks
- Errors count

## Будущие улучшения (Фаза 3)

- [ ] Docker support
- [ ] Redis backend для кэша (опционально)
- [ ] TCP DNS support
- [ ] DNS-over-TLS (DoT)
- [ ] DNS-over-HTTPS (DoH)
- [ ] Prometheus metrics endpoint
- [ ] Multiple upstream servers (failover)
