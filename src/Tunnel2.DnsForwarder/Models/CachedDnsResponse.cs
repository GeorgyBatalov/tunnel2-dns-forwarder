namespace Tunnel2.DnsForwarder.Models;

/// <summary>
/// Кэшированный DNS ответ
/// </summary>
public sealed class CachedDnsResponse
{
    /// <summary>
    /// Сырые байты DNS ответа
    /// </summary>
    public required byte[] ResponseData { get; init; }

    /// <summary>
    /// TTL из оригинального DNS ответа (в секундах)
    /// </summary>
    public required int OriginalTtl { get; init; }

    /// <summary>
    /// Время создания записи в кэше
    /// </summary>
    public required DateTime CachedAt { get; init; }

    /// <summary>
    /// Тип записи (positive или negative)
    /// </summary>
    public required CacheEntryType EntryType { get; init; }
}

/// <summary>
/// Тип записи в кэше
/// </summary>
public enum CacheEntryType
{
    /// <summary>
    /// Успешный ответ (NOERROR с записями)
    /// </summary>
    Positive,

    /// <summary>
    /// NXDOMAIN или другая ошибка
    /// </summary>
    Negative
}
