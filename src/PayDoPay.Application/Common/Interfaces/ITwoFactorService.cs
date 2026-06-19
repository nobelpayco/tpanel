namespace PayDoPay.Application.Common.Interfaces;

/// <summary>TOTP 2FA (google2fa uyumlu).</summary>
public interface ITwoFactorService
{
    /// <summary>Yeni base32 secret üretir.</summary>
    string GenerateSecret();

    /// <summary>otpauth URI'sini içeren QR kodunu SVG (string) olarak döner.</summary>
    string GetQrCodeSvg(string username, string secret);

    /// <summary>±2 dk drift toleransı (window=4) ile kodu doğrular.</summary>
    bool Verify(string secret, string code);
}
