using PayDoPay.Application.Common.Interfaces;

namespace PayDoPay.Infrastructure.Services;

/// <summary>İstanbul saat dilimine sabitlenmiş uygulama saati (Laravel APP_TIMEZONE uyumu).</summary>
public class SystemClock : IClock
{
    private static readonly TimeZoneInfo Istanbul = ResolveIstanbul();

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Istanbul);
    public DateTime Today => Now.Date;
    public long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static TimeZoneInfo ResolveIstanbul()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Son çare: sabit +03:00 (İstanbul DST kullanmıyor).
        return TimeZoneInfo.CreateCustomTimeZone("Istanbul+3", TimeSpan.FromHours(3), "Istanbul", "Istanbul");
    }
}
