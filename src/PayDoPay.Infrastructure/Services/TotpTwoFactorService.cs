using Microsoft.Extensions.Configuration;
using OtpNet;
using PayDoPay.Application.Common.Interfaces;
using QRCoder;

namespace PayDoPay.Infrastructure.Services;

/// <summary>google2fa uyumlu TOTP (base32 secret, ±2 dk pencere) + SVG QR.</summary>
public class TotpTwoFactorService : ITwoFactorService
{
    private readonly string _issuer;

    public TotpTwoFactorService(IConfiguration config)
    {
        _issuer = config["App:Name"] ?? "PayDoPay";
    }

    public string GenerateSecret()
    {
        // 10 bayt => 16 karakter base32 (google2fa varsayılanı ile uyumlu).
        var key = KeyGeneration.GenerateRandomKey(10);
        return Base32Encoding.ToString(key);
    }

    public string GetQrCodeSvg(string username, string secret)
    {
        var issuer = Uri.EscapeDataString(_issuer);
        var holder = Uri.EscapeDataString(username);
        var otpauth = $"otpauth://totp/{issuer}:{holder}?secret={secret}&issuer={issuer}";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpauth, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(4);
    }

    public bool Verify(string secret, string code)
    {
        if (string.IsNullOrEmpty(secret))
            return false;

        try
        {
            var keyBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(keyBytes);
            // window=4 (önce/sonra) => ±2 dk drift toleransı (google2fa verifyKey window=4).
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 4, future: 4));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
