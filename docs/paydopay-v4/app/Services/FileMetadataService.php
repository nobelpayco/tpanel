<?php

namespace App\Services;

/**
 * Dosya metadata analizi — AI'dan bağımsız sahte tespiti.
 *
 * PDF: Producer/Creator string parsing (kütüphane yok)
 * JPG: exif_read_data (ext-exif)
 * PNG: tEXt/iTXt chunk parsing (raw binary)
 *
 * Çıkış: ['suspicious' => bool, 'flags' => string[], 'summary' => string|null]
 */
class FileMetadataService
{
    /** Sahte üretim/düzenleme yazılımları — bu kelimeler metadata'da geçerse şüphe sinyali. */
    private const SUSPICIOUS_TOOLS = [
        'Adobe Photoshop', 'Photoshop',
        'Adobe Illustrator', 'Illustrator',
        'GIMP',
        'Microsoft Word', 'Word', 'LibreOffice', 'OpenOffice',
        'Canva',
        'Figma',
        'Inkscape',
        'Paint.NET', 'MSPaint',
        'ChatGPT', 'OpenAI', 'DALL-E', 'DALL·E', 'Midjourney', 'Stable Diffusion',
    ];

    public static function analyze(string $binary, string $mime): array
    {
        $flags = [];
        $summary = null;

        try {
            if ($mime === 'application/pdf') {
                [$flags, $summary] = self::analyzePdf($binary);
            } elseif (in_array($mime, ['image/jpeg', 'image/jpg'], true)) {
                [$flags, $summary] = self::analyzeJpeg($binary);
            } elseif ($mime === 'image/png') {
                [$flags, $summary] = self::analyzePng($binary);
            } elseif ($mime === 'image/webp') {
                [$flags, $summary] = self::analyzeWebp($binary);
            }
        } catch (\Throwable $e) {
            // Sessizce geç; metadata yokluğu suç değil
        }

        return [
            'suspicious' => count($flags) > 0,
            'flags'      => $flags,
            'summary'    => $summary,
        ];
    }

    private static function analyzePdf(string $binary): array
    {
        $flags = [];
        $head = substr($binary, 0, 65536);
        $tail = substr($binary, -65536);
        $scan = $head . $tail;

        $fields = [
            'Producer'     => null,
            'Creator'      => null,
            'Author'       => null,
            'Title'        => null,
            'CreationDate' => null,
            'ModDate'      => null,
        ];

        foreach ($fields as $key => $_) {
            if (preg_match('/\/' . $key . '\s*\(([^)]{1,200})\)/u', $scan, $m)) {
                $val = self::decodePdfString($m[1]);
                if ($val !== '') {
                    $fields[$key] = $val;
                }
            }
        }

        foreach ($fields as $key => $val) {
            if ($val && self::isSuspiciousTool($val)) {
                $flags[] = "PDF {$key}: {$val}";
            }
        }

        // CreationDate ile ModDate arasında belirgin fark varsa → düzenlenmiş PDF
        if ($fields['CreationDate'] && $fields['ModDate'] && $fields['CreationDate'] !== $fields['ModDate']) {
            $flags[] = "PDF tarihi düzenlenmiş (CreationDate ≠ ModDate)";
        }

        $summary = $fields['Producer'] ?? $fields['Creator'];

        return [$flags, $summary];
    }

    private static function analyzeJpeg(string $binary): array
    {
        $flags = [];
        $summary = null;

        if (! function_exists('exif_read_data')) {
            return [$flags, $summary];
        }

        // exif_read_data resource veya data:// stream gerektirir
        $exif = @exif_read_data('data://image/jpeg;base64,' . base64_encode($binary), 'IFD0,EXIF', true, false);
        if (! is_array($exif) || empty($exif)) {
            return [$flags, $summary];
        }

        $ifd0 = $exif['IFD0'] ?? [];
        $software = $ifd0['Software'] ?? null;
        $make     = $ifd0['Make'] ?? null;
        $model    = $ifd0['Model'] ?? null;

        if ($software && self::isSuspiciousTool($software)) {
            $flags[] = "JPEG Software: {$software}";
        }

        // Make/Model bir telefon imzasıysa real gibi davran (Apple, samsung, Xiaomi, HUAWEI)
        // Make YOKSA ve Software de YOKSA → screenshot olabilir, suç değil
        // Make YOKSA ama Software=Photoshop → düzenlenmiş

        $summary = $software ?: ($make && $model ? "$make $model" : null);

        return [$flags, $summary];
    }

    private static function analyzePng(string $binary): array
    {
        $flags = [];
        $summary = null;

        if (substr($binary, 0, 8) !== "\x89PNG\r\n\x1a\n") {
            return [$flags, $summary];
        }

        $offset = 8;
        $len = strlen($binary);
        $textChunks = [];

        while ($offset + 12 <= $len) {
            $size = unpack('N', substr($binary, $offset, 4))[1];
            $type = substr($binary, $offset + 4, 4);
            $data = substr($binary, $offset + 8, $size);
            $offset += 12 + $size;

            if ($type === 'IEND') break;

            if ($type === 'tEXt') {
                $parts = explode("\0", $data, 2);
                if (count($parts) === 2) {
                    $textChunks[$parts[0]] = $parts[1];
                }
            } elseif ($type === 'iTXt') {
                $nullPos = strpos($data, "\0");
                if ($nullPos !== false) {
                    $key = substr($data, 0, $nullPos);
                    $rest = substr($data, $nullPos + 5);
                    $lastNull = strrpos($rest, "\0");
                    $textChunks[$key] = $lastNull !== false ? substr($rest, $lastNull + 1) : $rest;
                }
            }
        }

        foreach ($textChunks as $key => $val) {
            $combined = "{$key}: {$val}";
            if (self::isSuspiciousTool($val) || self::isSuspiciousTool($key)) {
                $flags[] = "PNG {$key}: " . substr($val, 0, 80);
            }
        }

        if (isset($textChunks['Software'])) {
            $summary = $textChunks['Software'];
        } elseif (isset($textChunks['Source'])) {
            $summary = $textChunks['Source'];
        }

        return [$flags, $summary];
    }

    private static function analyzeWebp(string $binary): array
    {
        // WebP EXIF chunk içerebilir; karmaşık. Şimdilik atlıyoruz.
        return [[], null];
    }

    private static function isSuspiciousTool(string $value): bool
    {
        $value = strtolower($value);
        foreach (self::SUSPICIOUS_TOOLS as $tool) {
            if (str_contains($value, strtolower($tool))) {
                return true;
            }
        }
        return false;
    }

    /** PDF literal string'lerini decode et (basit) */
    private static function decodePdfString(string $raw): string
    {
        // \xxx octal, \( \) \\ escape'leri için basit normalize
        $decoded = preg_replace_callback('/\\\\([0-7]{1,3})/', function ($m) {
            return chr(octdec($m[1]));
        }, $raw);
        $decoded = str_replace(['\\(', '\\)', '\\\\'], ['(', ')', '\\'], $decoded);
        return trim($decoded);
    }
}
