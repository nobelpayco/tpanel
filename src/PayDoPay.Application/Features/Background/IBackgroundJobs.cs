namespace PayDoPay.Application.Features.Background;

/// <summary>Gün sonu kasa snapshot işi (snapshot:daily).</summary>
public interface IDailyCaseSnapshotJob
{
    Task RunAsync(string date, CancellationToken ct = default);
}

/// <summary>Süre dolan pending invest'leri otomatik reddet + fail callback.</summary>
public interface IExpirePendingJob
{
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>Telegram bekleyen yatırım/dekont/kredi bildirimleri (Faz 6b).</summary>
public interface ICheckPendingNotificationsJob
{
    Task RunAsync(CancellationToken ct = default);
}

/// <summary>Düşük tutar şüpheli oyuncu tespiti → sistem chat bildirimi (Faz 6b).</summary>
public interface ICheckLowAmountRiskJob
{
    Task RunAsync(CancellationToken ct = default);
}
