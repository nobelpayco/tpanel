namespace PayDoPay.Application.Features.Transactions;

/// <summary>Çekim dekontlarını public depoya yazar (receipts/withdrawals/{id}/...).</summary>
public interface IWithdrawReceiptStorage
{
    Task<string> StoreAsync(int investId, string fileName, byte[] content, CancellationToken ct = default);
    Task<(bool Exists, string FullPath)> ResolveAsync(string relativePath, CancellationToken ct = default);
}

/// <summary>Dekont AI doğrulama kuyruğu. Faz 6'da gerçek implementasyon; şimdilik no-op.</summary>
public interface IReceiptVerificationQueue
{
    Task EnqueueAsync(int receiptId, CancellationToken ct = default);
}

/// <summary>Algısal hash (dHash) — sahte şablon eşleştirme.</summary>
public interface IPerceptualHasher
{
    string DHash(byte[] content, string? mimeType);
    int HammingDistance(string hashA, string hashB);
}
