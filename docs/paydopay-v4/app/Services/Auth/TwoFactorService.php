<?php

namespace App\Services\Auth;

use App\Models\User;
use PragmaRX\Google2FA\Google2FA;
use BaconQrCode\Renderer\ImageRenderer;
use BaconQrCode\Renderer\Image\SvgImageBackEnd;
use BaconQrCode\Renderer\RendererStyle\RendererStyle;
use BaconQrCode\Writer;

class TwoFactorService
{
    public function __construct(private Google2FA $google2fa) {}

    public function generateSecret(): string
    {
        return $this->google2fa->generateSecretKey();
    }

    public function getQrCodeSvg(User $user, string $secret): string
    {
        $url = $this->google2fa->getQRCodeUrl(
            config('app.name'),
            $user->username,
            $secret,
        );

        $renderer = new ImageRenderer(
            new RendererStyle(200),
            new SvgImageBackEnd(),
        );

        return (new Writer($renderer))->writeString($url);
    }

    public function verify(User $user, string $code): bool
    {
        $secret = $user->otp_code;
        if (! $secret) {
            return false;
        }

        // window=4 → ±2 dk drift tolerance. Telefon/sunucu saat farkına dayanıklı.
        return $this->google2fa->verifyKey($secret, $code, 4);
    }
}
