<?php

return [
    'failed'   => 'Bu kimlik bilgileri kayıtlarımızla eşleşmiyor.',
    'password' => 'Şifre hatalı.',
    'throttle' => 'Çok fazla giriş denemesi yaptınız. Lütfen :seconds saniye sonra tekrar deneyin.',

    // Custom — AuthController + EnforceIdleTimeout
    'invalid_credentials'      => 'Kullanıcı adı veya şifre hatalı.',
    'account_blocked'          => 'Hesabınız engellenmiştir.',
    'two_factor_required'      => 'İki faktörlü doğrulama gerekiyor.',
    'session_expired'          => 'Oturum süresi doldu.',
    'invalid_token'            => 'Geçersiz token.',
    'invalid_code'             => 'Geçersiz doğrulama kodu.',
    'logged_out'               => 'Çıkış yapıldı.',
    'current_password_wrong'   => 'Mevcut şifre hatalı.',
    'password_updated_reauth'  => 'Şifre güncellendi. Lütfen yeniden giriş yapın.',
    'pw_min'                   => 'Yeni şifre en az 6 karakter olmalıdır.',
    'pw_regex'                 => 'Yeni şifre en az bir harf ve bir rakam içermelidir.',
    'pw_different'             => 'Yeni şifre eski şifre ile aynı olamaz.',
    'idle_timeout'             => 'Oturumunuz hareketsizlik nedeniyle sonlandırıldı.',
    'no_permission'            => 'Bu işlem için yetkiniz yok.',
];
