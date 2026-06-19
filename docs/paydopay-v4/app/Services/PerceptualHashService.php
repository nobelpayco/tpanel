<?php

namespace App\Services;

/**
 * Perceptual hash (dHash) — sahte şablon eşleştirme.
 *
 * Algoritma:
 * 1. Görseli grayscale 9x8 = 72 piksele küçült
 * 2. Her satır için komşu piksel farklarını al → 8x8 = 64 bit
 * 3. 16 karakterlik hex döndür
 *
 * Hamming distance = iki hash arasındaki farklı bit sayısı. Eşik 8 = ~%87 benzerlik.
 */
class PerceptualHashService
{
    private const HASH_W = 9; // (W-1) x H = 8x8 bits
    private const HASH_H = 8;

    /**
     * @return string|null 16-karakter hex; hesaplanamazsa null
     */
    public static function dHash(string $binary, string $mime): ?string
    {
        if (! function_exists('imagecreatefromstring') || ! extension_loaded('gd')) {
            return null;
        }

        // PDF için GD desteklemiyor — atla
        if ($mime === 'application/pdf') {
            return null;
        }

        // imagecreatefromstring çoğu raster format için çalışır (jpg/png/webp/gif)
        $src = @imagecreatefromstring($binary);
        if (! $src) {
            return null;
        }

        try {
            $resized = imagecreatetruecolor(self::HASH_W, self::HASH_H);
            // EXIF orientation'a göre döndür (basit fallback yok; AI tarafı zaten halleder)
            imagecopyresampled(
                $resized, $src,
                0, 0, 0, 0,
                self::HASH_W, self::HASH_H,
                imagesx($src), imagesy($src)
            );

            $bits = '';
            for ($y = 0; $y < self::HASH_H; $y++) {
                for ($x = 0; $x < self::HASH_W - 1; $x++) {
                    $left  = self::luma(imagecolorat($resized, $x, $y));
                    $right = self::luma(imagecolorat($resized, $x + 1, $y));
                    $bits .= ($left < $right) ? '1' : '0';
                }
            }

            imagedestroy($resized);
            imagedestroy($src);

            // 64-bit → 16 hex char
            return self::bitsToHex($bits);
        } catch (\Throwable $e) {
            if (isset($resized) && $resized) imagedestroy($resized);
            if ($src) imagedestroy($src);
            return null;
        }
    }

    /**
     * İki dHash arasındaki Hamming distance (0-64).
     * 0 = aynı görsel, <8 = çok benzer, <16 = orta benzerlik
     */
    public static function hammingDistance(string $hashA, string $hashB): int
    {
        if (strlen($hashA) !== strlen($hashB) || strlen($hashA) === 0) {
            return PHP_INT_MAX;
        }

        $a = hex2bin($hashA);
        $b = hex2bin($hashB);
        if ($a === false || $b === false) {
            return PHP_INT_MAX;
        }

        $distance = 0;
        $len = strlen($a);
        for ($i = 0; $i < $len; $i++) {
            $xor = ord($a[$i]) ^ ord($b[$i]);
            // popcount byte
            $xor = $xor - (($xor >> 1) & 0x55);
            $xor = ($xor & 0x33) + (($xor >> 2) & 0x33);
            $xor = ($xor + ($xor >> 4)) & 0x0F;
            $distance += $xor;
        }
        return $distance;
    }

    private static function luma(int $rgb): int
    {
        $r = ($rgb >> 16) & 0xFF;
        $g = ($rgb >> 8) & 0xFF;
        $b = $rgb & 0xFF;
        return (int) (0.299 * $r + 0.587 * $g + 0.114 * $b);
    }

    private static function bitsToHex(string $bits): string
    {
        $hex = '';
        for ($i = 0; $i < strlen($bits); $i += 4) {
            $hex .= dechex(bindec(substr($bits, $i, 4)));
        }
        return $hex;
    }
}
