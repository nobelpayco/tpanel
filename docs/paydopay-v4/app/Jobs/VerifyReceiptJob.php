<?php

namespace App\Jobs;

use App\Services\ClaudeVisionService;
use Illuminate\Bus\Queueable;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Bus\Dispatchable;
use Illuminate\Queue\InteractsWithQueue;
use Illuminate\Queue\SerializesModels;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;
use Illuminate\Support\Facades\Storage;

class VerifyReceiptJob implements ShouldQueue
{
    use Dispatchable, InteractsWithQueue, Queueable, SerializesModels;

    public int $tries = 3;
    public array $backoff = [60, 300];

    public function __construct(public int $receiptId) {}

    public function handle(): void
    {
        $receipt = DB::table('invest_receipts')->where('id', $this->receiptId)->first();
        if (! $receipt) {
            Log::info('VerifyReceiptJob: receipt not found', ['id' => $this->receiptId]);
            return;
        }

        $invest = DB::table('invest')->where('id', $receipt->invest_id)->first();
        if (! $invest) {
            $this->saveResult('rejected', 0, null, 'İlgili çekim bulunamadı.');
            return;
        }

        // 1) Hash duplicate kontrolü — aynı dosya başka bir invest'te var mı?
        if ($receipt->file_hash) {
            $dup = DB::table('invest_receipts')
                ->where('file_hash', $receipt->file_hash)
                ->where('id', '!=', $receipt->id)
                ->where('invest_id', '!=', $receipt->invest_id)
                ->orderBy('id')
                ->first();
            if ($dup) {
                $this->saveResult('rejected', 0, [
                    'duplicate_of' => ['receipt_id' => $dup->id, 'invest_id' => $dup->invest_id],
                ], 'Aynı dosya daha önce başka bir çekime (#' . $dup->invest_id . ') yüklenmiş.');
                return;
            }
        }

        // 1B) Bilinen sahte şablon kontrolü — file_hash veya perceptual_hash eşleşmesi
        $templates = DB::table('fake_receipt_templates')->get();
        foreach ($templates as $tpl) {
            // Exact file hash match
            if ($receipt->file_hash && $tpl->file_hash && $receipt->file_hash === $tpl->file_hash) {
                $this->saveResult('rejected', 0, [
                    'matched_template_id' => $tpl->id,
                    'match_type'          => 'file_hash',
                ], "🚫 Bilinen sahte şablon (#{$tpl->id}) ile bire bir eşleşti." . ($tpl->reason ? " (Şablon sebebi: {$tpl->reason})" : ''));
                Log::info('VerifyReceiptJob: known fake template match (file_hash)', [
                    'receipt_id' => $this->receiptId, 'template_id' => $tpl->id,
                ]);
                return;
            }

            // Perceptual hash distance (sadece her ikisi de varsa)
            if ($receipt->perceptual_hash && $tpl->perceptual_hash) {
                $dist = \App\Services\PerceptualHashService::hammingDistance($receipt->perceptual_hash, $tpl->perceptual_hash);
                if ($dist <= 8) {
                    $this->saveResult('rejected', 0, [
                        'matched_template_id' => $tpl->id,
                        'match_type'          => 'perceptual_hash',
                        'hamming_distance'    => $dist,
                    ], "🚫 Bilinen sahte şablon (#{$tpl->id}) ile görsel olarak eşleşti (Hamming: {$dist})." . ($tpl->reason ? " (Şablon sebebi: {$tpl->reason})" : ''));
                    Log::info('VerifyReceiptJob: known fake template match (dhash)', [
                        'receipt_id' => $this->receiptId, 'template_id' => $tpl->id, 'distance' => $dist,
                    ]);
                    return;
                }
            }
        }

        // 2) Dosyayı oku
        if (! Storage::disk('public')->exists($receipt->file_path)) {
            $this->saveResult('rejected', 0, null, 'Dosya storage\'da bulunamadı.');
            return;
        }
        $binary = Storage::disk('public')->get($receipt->file_path);
        if (! $binary) {
            Log::warning('VerifyReceiptJob: dosya okunamadı', ['receipt_id' => $this->receiptId]);
            return; // retry
        }

        // 2B) Server-side metadata analizi (AI'dan önce, AI'dan bağımsız)
        $metaAnalysis = \App\Services\FileMetadataService::analyze($binary, $receipt->mime_type ?: 'application/octet-stream');
        $metadataFlags = $metaAnalysis['flags'] ?? [];
        $metadataSuspicious = $metaAnalysis['suspicious'] ?? false;

        // 3) Claude Vision çağır
        $expected = [
            'amount'         => (float) $invest->amount,
            'iban'           => (string) $invest->iban,
            'recipient_name' => (string) $invest->name,
        ];
        $result = ClaudeVisionService::analyzeReceipt($binary, $receipt->mime_type ?: 'image/jpeg', $expected);
        if ($result === null) {
            Log::warning('VerifyReceiptJob: Claude vision null döndü, retry', ['receipt_id' => $this->receiptId]);
            $this->release(60);
            return;
        }

        // 4) Skor hesapla
        $score = 0;
        $notes = [];

        // Receipt format kontrolü
        if (! empty($result['is_receipt'])) {
            $score += 10;
            $notes[] = 'Banka makbuzu formatı tespit edildi.';
        } else {
            $notes[] = 'Banka makbuzu formatı tespit edilemedi.';
        }

        // Tutar eşleşmesi (±%1 tolerans)
        if (isset($result['amount']) && is_numeric($result['amount'])) {
            $rA = (float) $result['amount'];
            $eA = (float) $invest->amount;
            $diff = $eA > 0 ? abs($rA - $eA) / $eA : 0;
            if ($diff <= 0.01) {
                $score += 30;
                $notes[] = 'Tutar tam eşleşiyor (' . number_format($rA, 2) . ' TL).';
            } else {
                $notes[] = 'Tutar uyumsuz (dekont: ' . number_format($rA, 2) . ', beklenen: ' . number_format($eA, 2) . ').';
            }
        }

        // IBAN son 4 hane eşleşmesi — önce iban_full'dan çıkar (daha güvenilir), yoksa iban_last4
        $expIbanDigits = preg_replace('/[^0-9]/', '', (string) $invest->iban);
        $expLast4 = strlen($expIbanDigits) >= 4 ? substr($expIbanDigits, -4) : '';
        $ibanFullRaw = isset($result['iban_full']) ? (string) $result['iban_full'] : '';
        $rLast4 = '';
        if ($ibanFullRaw) {
            $ibanFullDigits = preg_replace('/[^0-9]/', '', $ibanFullRaw);
            if (strlen($ibanFullDigits) >= 4) $rLast4 = substr($ibanFullDigits, -4);
        }
        if (! $rLast4 && isset($result['iban_last4'])) {
            $rLast4 = substr(preg_replace('/[^0-9]/', '', (string) $result['iban_last4']), -4);
        }
        if ($expLast4 && $rLast4 && $expLast4 === $rLast4) {
            $score += 25;
            $notes[] = 'IBAN son 4 hane eşleşiyor (****' . $expLast4 . ').';
        } elseif ($expLast4 && $rLast4) {
            $notes[] = 'IBAN son 4 hane uyumsuz (dekont: ' . $rLast4 . ', beklenen: ' . $expLast4 . ').';
        } elseif ($expLast4 && ! $rLast4) {
            $notes[] = 'IBAN okunamadı (görsel net değil — manuel kontrol önerilir).';
        }

        // Alıcı adı: kelime bazında karşılaştır — her isim/soyisim ayrı ayrı en az %80 eşleşmeli.
        // (similar_text bütün metni karşılaştırınca soyisim eşleşmesi farklı bir ismi de geçirebiliyor.)
        $expName = self::normalizeName((string) $invest->name);
        $rName = self::normalizeName((string) ($result['recipient_name'] ?? ''));
        if ($expName && $rName) {
            $expTokens = array_values(array_filter(explode(' ', $expName), fn($t) => strlen($t) >= 2));
            $rTokens   = array_values(array_filter(explode(' ', $rName), fn($t) => strlen($t) >= 2));

            $allMatch = count($expTokens) > 0 && count($rTokens) > 0;
            $sims = [];
            foreach ($expTokens as $et) {
                $best = 0;
                foreach ($rTokens as $rt) {
                    similar_text($et, $rt, $pct);
                    if ($pct > $best) $best = $pct;
                }
                $sims[] = $best;
                if ($best < 80) $allMatch = false;
            }
            $avgSim = $sims ? array_sum($sims) / count($sims) : 0;

            if ($allMatch) {
                $score += 25;
                $notes[] = 'Alıcı adı eşleşiyor (' . round($avgSim) . '% benzerlik).';
            } else {
                $notes[] = 'Alıcı adı uyumsuz (dekont: "' . ($result['recipient_name'] ?? '?') . '", beklenen: "' . $invest->name . '").';
            }
        }

        // Banka adı tanınır mı
        $knownBanks = ['garanti', 'akbank', 'is bankasi', 'iş bankası', 'yapi kredi', 'yapı kredi',
            'ziraat', 'finansbank', 'qnb', 'halkbank', 'vakif', 'vakıf', 'denizbank',
            'enpara', 'odeabank', 'şekerbank', 'sekerbank', 'ing', 'hsbc', 'fibabanka',
            'turkiye finans', 'türkiye finans', 'albaraka', 'kuveyt türk', 'kuveyt turk', 'tom', 'papara', 'param'];
        $bankNameLower = strtolower((string) ($result['bank_name'] ?? ''));
        $bankRecognized = false;
        foreach ($knownBanks as $b) {
            if ($bankNameLower && str_contains($bankNameLower, $b)) {
                $bankRecognized = true;
                break;
            }
        }
        if ($bankRecognized) {
            $score += 10;
            $notes[] = 'Banka tanındı: ' . $result['bank_name'] . '.';
        }

        // Tampering kontrolü — verilerin eşleşmesi tampering'i geçersiz KILMAZ.
        // (Sahte dekontlarda fraudster zaten doğru verileri photoshoplar — tam eşleşme şüpheyi azaltmaz, artırır.)
        if (! empty($result['signs_of_tampering'])) {
            $score = max(0, $score - 40);
            $reason = $result['tampering_reasons'] ?? 'görsel/PDF düzenleme izleri tespit edildi';
            $notes[] = '⚠️ Manipülasyon belirtisi: ' . $reason;
        }

        // AI-generated (sıfırdan AI ile üretilmiş) tespiti — SAHTE DEKONT, asla geçmemeli.
        // False-positive koruması YOK; veriler eşleşse bile reddedilir.
        if (! empty($result['appears_ai_generated'])) {
            $score = max(0, $score - 80);
            $reason = $result['ai_generation_reasons'] ?? 'AI/yapay görsel üretimi belirtileri tespit edildi';
            $notes[] = '⛔ Sahte dekont (AI ile üretilmiş gibi görünüyor): ' . $reason;
        }

        // Server-side metadata flags (Photoshop, Word, ChatGPT vs.)
        if ($metadataSuspicious) {
            $score = max(0, $score - 30);
            $notes[] = '📄 Dosya metadata şüpheli: ' . implode(' | ', $metadataFlags);
        }

        // AI notu varsa ekle
        if (! empty($result['notes'])) {
            $notes[] = 'AI: ' . $result['notes'];
        }

        // 5) Karar
        $status = $score >= 80 ? 'verified' : ($score >= 50 ? 'suspicious' : 'rejected');

        $this->saveResult($status, $score, $result, implode("\n", $notes), $metadataFlags);
        Log::info('VerifyReceiptJob done', [
            'receipt_id' => $this->receiptId,
            'status' => $status,
            'score' => $score,
        ]);
    }

    private function saveResult(string $status, int $score, ?array $data, string $notes, array $metadataFlags = []): void
    {
        DB::table('invest_receipts')->where('id', $this->receiptId)->update([
            'verification_status' => $status,
            'verification_score'  => $score,
            'verification_data'   => $data ? json_encode($data, JSON_UNESCAPED_UNICODE) : null,
            'verification_notes'  => $notes,
            'metadata_flags'      => ! empty($metadataFlags) ? json_encode($metadataFlags, JSON_UNESCAPED_UNICODE) : null,
            'verified_at'         => now(),
        ]);
    }

    private static function normalizeName(string $name): string
    {
        $map = [
            'ç' => 'c', 'Ç' => 'C', 'ğ' => 'g', 'Ğ' => 'G',
            'ı' => 'i', 'I' => 'I', 'İ' => 'I', 'i' => 'i',
            'ö' => 'o', 'Ö' => 'O', 'ş' => 's', 'Ş' => 'S',
            'ü' => 'u', 'Ü' => 'U',
        ];
        $name = strtr($name, $map);
        $name = preg_replace('/[^A-Za-z0-9\s]/', '', $name);
        $name = preg_replace('/\s+/', ' ', trim($name));
        return strtolower($name);
    }
}
