using System.Text;
using System.Text.RegularExpressions;
using PayDoPay.Application.Features.Receipts;
using PayDoPay.Application.Features.Transactions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PayDoPay.Infrastructure.Services;

/// <summary>dHash (9x8 grayscale → 64 bit → 16 hex) + Hamming distance. ImageSharp ile.</summary>
public class PerceptualHasher : IPerceptualHasher
{
    private const int W = 9, H = 8;

    public string DHash(byte[] content, string? mimeType)
    {
        if (mimeType == "application/pdf") return "";
        try
        {
            using var img = Image.Load<Rgba32>(content);
            img.Mutate(x => x.Resize(W, H).Grayscale());
            var bits = new StringBuilder(64);
            img.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < H; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < W - 1; x++)
                        bits.Append(row[x].R < row[x + 1].R ? '1' : '0');
                }
            });
            return BitsToHex(bits.ToString());
        }
        catch { return ""; }
    }

    public int HammingDistance(string a, string b)
    {
        if (a.Length != b.Length || a.Length == 0) return int.MaxValue;
        byte[] ba, bb;
        try { ba = Convert.FromHexString(a); bb = Convert.FromHexString(b); } catch { return int.MaxValue; }
        var dist = 0;
        for (var i = 0; i < ba.Length; i++) dist += System.Numerics.BitOperations.PopCount((uint)(ba[i] ^ bb[i]));
        return dist;
    }

    private static string BitsToHex(string bits)
    {
        var sb = new StringBuilder(bits.Length / 4);
        for (var i = 0; i + 4 <= bits.Length; i += 4)
            sb.Append(Convert.ToInt32(bits.Substring(i, 4), 2).ToString("x"));
        return sb.ToString();
    }
}

/// <summary>Dosya metadata analizi — Photoshop/Word/ChatGPT vb. imzaları (FileMetadataService karşılığı).</summary>
public class FileMetadataService : IFileMetadataService
{
    private static readonly string[] SuspiciousTools =
    {
        "adobe photoshop", "photoshop", "adobe illustrator", "illustrator", "gimp",
        "microsoft word", "libreoffice", "openoffice", "canva", "figma", "inkscape",
        "paint.net", "mspaint", "chatgpt", "openai", "dall-e", "dall·e", "midjourney", "stable diffusion",
    };

    public MetadataResult Analyze(byte[] binary, string mimeType)
    {
        try
        {
            return mimeType switch
            {
                "application/pdf" => AnalyzePdf(binary),
                "image/jpeg" or "image/jpg" => AnalyzeScan(binary, "JPEG"),
                "image/png" => AnalyzePng(binary),
                _ => new MetadataResult(false, Array.Empty<string>(), null),
            };
        }
        catch { return new MetadataResult(false, Array.Empty<string>(), null); }
    }

    private static bool IsSuspicious(string v)
    {
        var lower = v.ToLowerInvariant();
        return SuspiciousTools.Any(t => lower.Contains(t));
    }

    private static MetadataResult AnalyzePdf(byte[] b)
    {
        var head = Latin1(b, 0, Math.Min(65536, b.Length));
        var tail = b.Length > 65536 ? Latin1(b, b.Length - 65536, 65536) : "";
        var scan = head + tail;
        var flags = new List<string>();
        string? creation = null, mod = null, summary = null;
        foreach (var key in new[] { "Producer", "Creator", "Author", "Title", "CreationDate", "ModDate" })
        {
            var m = Regex.Match(scan, "/" + key + @"\s*\(([^)]{1,200})\)");
            if (!m.Success) continue;
            var val = m.Groups[1].Value.Trim();
            if (val == "") continue;
            if (key == "CreationDate") creation = val;
            else if (key == "ModDate") mod = val;
            else summary ??= val;
            if (IsSuspicious(val)) flags.Add($"PDF {key}: {val}");
        }
        if (creation is not null && mod is not null && creation != mod) flags.Add("PDF tarihi düzenlenmiş (CreationDate ≠ ModDate)");
        return new MetadataResult(flags.Count > 0, flags, summary ?? creation);
    }

    private static MetadataResult AnalyzePng(byte[] b)
    {
        if (b.Length < 8 || !(b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)) return new(false, Array.Empty<string>(), null);
        var flags = new List<string>(); string? summary = null;
        var offset = 8;
        while (offset + 12 <= b.Length)
        {
            var size = (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
            if (size < 0 || offset + 12 + size > b.Length) break;
            var type = Latin1(b, offset + 4, 4);
            var data = Latin1(b, offset + 8, size);
            offset += 12 + size;
            if (type == "IEND") break;
            if (type is "tEXt" or "iTXt")
            {
                var nul = data.IndexOf('\0');
                var key = nul >= 0 ? data[..nul] : type;
                var val = nul >= 0 ? data[(nul + 1)..] : data;
                if (IsSuspicious(val) || IsSuspicious(key)) flags.Add($"PNG {key}: {(val.Length > 80 ? val[..80] : val)}");
                if (key is "Software" or "Source") summary ??= val;
            }
        }
        return new MetadataResult(flags.Count > 0, flags, summary);
    }

    private static MetadataResult AnalyzeScan(byte[] b, string label)
    {
        var scan = Latin1(b, 0, Math.Min(65536, b.Length));
        var flags = new List<string>();
        foreach (var t in SuspiciousTools)
        {
            var idx = scan.ToLowerInvariant().IndexOf(t, StringComparison.Ordinal);
            if (idx >= 0) { flags.Add($"{label} metadata: {scan.Substring(idx, Math.Min(40, scan.Length - idx)).Trim()}"); break; }
        }
        return new MetadataResult(flags.Count > 0, flags, null);
    }

    private static string Latin1(byte[] b, int start, int len) => Encoding.Latin1.GetString(b, start, Math.Min(len, b.Length - start));
}
