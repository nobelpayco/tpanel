using Microsoft.Extensions.Configuration;
using TPanel.Application.Features.Transactions;

namespace TPanel.Infrastructure.Services;

/// <summary>Çekim dekontları — Laravel public diski (storage/app/public) ile aynı konum.</summary>
public class WithdrawReceiptStorage : IWithdrawReceiptStorage
{
    private readonly string _publicDiskBase;

    public WithdrawReceiptStorage(IConfiguration config, IWebHostEnvironmentMarker env)
    {
        var configured = config["Storage:PublicDiskPath"] ?? "../../../docs/paydopay-v4/storage/app/public";
        _publicDiskBase = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
    }

    public async Task<string> StoreAsync(int investId, string fileName, byte[] content, CancellationToken ct = default)
    {
        var relative = $"receipts/withdrawals/{investId}/{fileName}";
        var full = Path.Combine(_publicDiskBase, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content, ct);
        return relative;
    }

    public Task<(bool, string)> ResolveAsync(string relativePath, CancellationToken ct = default)
    {
        var full = Path.Combine(_publicDiskBase, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult((File.Exists(full), full));
    }
}

