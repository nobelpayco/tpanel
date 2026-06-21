using Microsoft.Extensions.Configuration;
using TPanel.Application.Features.PublicApi;

namespace TPanel.Infrastructure.Services;

/// <summary>
/// Dekontları private klasöre yazar (Laravel storage/app/receipts ile aynı konum — admin paneli aynı dosyaları okur).
/// DB'ye 'receipts/{filename}' göreli yolu yazılır.
/// </summary>
public class FileReceiptStorage : IReceiptStorage
{
    private readonly string _localDiskBase;

    public FileReceiptStorage(IConfiguration config, IWebHostEnvironmentMarker env)
    {
        // Laravel local disk kökü (storage/app). DB'de 'receipts/...' göreli yolu saklanır.
        var configured = config["Storage:LocalDiskPath"] ?? "../../../docs/paydopay-v4/storage/app";
        _localDiskBase = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
    }

    public async Task<string> StoreReceiptAsync(string fileName, byte[] content, CancellationToken ct = default)
    {
        var dir = Path.Combine(_localDiskBase, "receipts");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), content, ct);
        return "receipts/" + fileName;
    }

    public Task<(bool, string)> ResolveAsync(string relativePath, CancellationToken ct = default)
    {
        var full = Path.Combine(_localDiskBase, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult((File.Exists(full), full));
    }
}

/// <summary>Infrastructure'ın ASP.NET'e bağımlı olmaması için content root sağlayıcı.</summary>
public interface IWebHostEnvironmentMarker
{
    string ContentRootPath { get; }
}
