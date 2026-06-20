namespace PayDoPay.Application.Common.Interfaces;

/// <summary>
/// Uygulama saati. Laravel APP_TIMEZONE=Europe/Istanbul ile uyumlu olması için
/// "Now" İstanbul yerel saatini döner (DB datetime kolonları bu zamana göre yazılmış).
/// </summary>
public interface IClock
{
    /// <summary>İstanbul yerel saati (saat dilimi bilgisi olmadan, DB ile aynı).</summary>
    DateTime Now { get; }

    /// <summary>İstanbul saatine göre günün başlangıcı (00:00).</summary>
    DateTime Today { get; }

    /// <summary>Unix saniye (UTC tabanlı).</summary>
    long UnixNow { get; }
}
