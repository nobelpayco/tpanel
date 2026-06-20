using System.Text.Json;

namespace TPanel.Application.Features.Receipts;

public record ReceiptExpected(double Amount, string Iban, string RecipientName);

public record ClaudeReceiptResult(JsonElement Json, int InputTokens, int OutputTokens, string Model, string RawText);

/// <summary>Anthropic Claude Vision ile banka dekontu analizi.</summary>
public interface IClaudeVisionService
{
    string Model();
    Task<ClaudeReceiptResult?> AnalyzeReceiptAsync(byte[] binary, string mimeType, ReceiptExpected expected, CancellationToken ct = default);
    Task<(bool Ok, string Message)> PingAsync(CancellationToken ct = default);
    double EstimateCost(int inputTokens, int outputTokens, string? model = null);
}

public record MetadataResult(bool Suspicious, IReadOnlyList<string> Flags, string? Summary);

/// <summary>Dosya metadata analizi — AI'dan bağımsız sahte tespiti (Photoshop/Word/ChatGPT imzaları).</summary>
public interface IFileMetadataService
{
    MetadataResult Analyze(byte[] binary, string mimeType);
}

/// <summary>Dekont doğrulama orkestrasyonu (VerifyReceiptJob karşılığı).</summary>
public interface IReceiptVerifier
{
    Task RunAsync(int receiptId, CancellationToken ct = default);
}
