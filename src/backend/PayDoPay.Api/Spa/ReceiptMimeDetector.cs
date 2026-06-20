namespace PayDoPay.Api.Spa;

/// <summary>
/// Dekont dosyasının gerçek türünü magic bytes ile tespit eder (uzantıya güvenmez).
/// İzinli: jpg, png, gif, webp, pdf. PHP getMimeType + finfo kontrolünün karşılığı.
/// </summary>
public static class ReceiptMimeDetector
{
    public static string? DetectExtension(byte[] b)
    {
        if (b.Length < 12) return null;

        // JPEG: FF D8 FF
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        // GIF: "GIF8"
        if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return "gif";
        // PDF: "%PDF"
        if (b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46) return "pdf";
        // WEBP: "RIFF"...."WEBP"
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "webp";

        return null;
    }
}
