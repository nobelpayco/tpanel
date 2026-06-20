namespace PayDoPay.Application.Features.Auth;

/// <summary>lang/tr/auth.php karşılıkları (varsayılan locale tr).</summary>
public static class AuthMessages
{
    public const string InvalidCredentials = "Kullanıcı adı veya şifre hatalı.";
    public const string AccountBlocked = "Hesabınız engellenmiştir.";
    public const string TwoFactorRequired = "İki faktörlü doğrulama gerekiyor.";
    public const string SessionExpired = "Oturum süresi doldu.";
    public const string InvalidToken = "Geçersiz token.";
    public const string InvalidCode = "Geçersiz doğrulama kodu.";
    public const string LoggedOut = "Çıkış yapıldı.";
    public const string CurrentPasswordWrong = "Mevcut şifre hatalı.";
    public const string PasswordUpdatedReauth = "Şifre güncellendi. Lütfen yeniden giriş yapın.";
    public const string PwMin = "Yeni şifre en az 6 karakter olmalıdır.";
    public const string PwRegex = "Yeni şifre en az bir harf ve bir rakam içermelidir.";
    public const string PwDifferent = "Yeni şifre eski şifre ile aynı olamaz.";
    public const string IdleTimeout = "Oturumunuz hareketsizlik nedeniyle sonlandırıldı.";
    public const string NoPermission = "Bu işlem için yetkiniz yok.";
}
