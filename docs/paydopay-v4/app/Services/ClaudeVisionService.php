<?php

namespace App\Services;

use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Log;

class ClaudeVisionService
{
    /** API key — önce system_settings, yoksa .env */
    public static function apiKey(): ?string
    {
        $key = DB::table('system_settings')->where('key', 'anthropic_api_key')->value('value');
        return $key ?: config('services.anthropic.api_key');
    }

    /** Model — önce system_settings, yoksa .env, yoksa default */
    public static function model(): string
    {
        $m = DB::table('system_settings')->where('key', 'anthropic_vision_model')->value('value');
        return $m ?: (config('services.anthropic.model') ?: 'claude-haiku-4-5');
    }

    /**
     * Anthropic Claude Vision API ile banka dekontu analiz et.
     *
     * @param string $binary Dosya binary (image veya PDF)
     * @param string $mimeType image/jpeg, image/png, image/webp, application/pdf
     * @param array  $expected ['amount' => float, 'iban' => string, 'recipient_name' => string]
     * @return array|null { is_receipt, amount, iban_last4, recipient_name, bank_name, date, confidence, raw_text, _usage }
     */
    public static function analyzeReceipt(string $binary, string $mimeType, array $expected): ?array
    {
        $apiKey = self::apiKey();
        if (! $apiKey) {
            Log::warning('ClaudeVisionService: ANTHROPIC_API_KEY tanımlı değil');
            return null;
        }

        $model = self::model();
        $base64 = base64_encode($binary);

        $expectedAmount = number_format((float) ($expected['amount'] ?? 0), 2, '.', '');
        $expectedIban = (string) ($expected['iban'] ?? '');
        $expectedName = (string) ($expected['recipient_name'] ?? '');

        $today = now()->format('Y-m-d');
        $systemPrompt = "Bugünün tarihi: {$today}. Dekonttaki işlem tarihi bugün veya öncesi ise NORMAL kabul edilir; 'gelecek tarih' diye işaretleme.\n\n" .
            "Sen bir banka dekontu/makbuzu analiz uzmanısın. Sana gönderilen görseli inceleyip aşağıdaki JSON formatında cevap ver. Sadece JSON döndür, başka metin yok:\n\n" .
            "{\n" .
            "  \"is_receipt\": boolean (banka transfer makbuzu/dekontu mu?),\n" .
            "  \"amount\": number|null (TL cinsinden tutar, sadece sayı),\n" .
            "  \"iban_full\": string|null (SADECE **ALICI** (havale edilen kişi / KARŞI TARAF / HEDEF) IBAN'ının TAM metni. GÖNDEREN/KAYNAK IBAN DEĞİL. TR ile başlayan, boşluklar dahil, görseldeki gibi aynen yaz. Hesap numarasıyla KARIŞTIRMA — IBAN her zaman TR ile başlar, 26 karakterdir),\n" .
            "  \"iban_last4\": string|null (ALICI IBAN'ın gerçek SON 4 RAKAMI, boşluksuz — gönderen IBAN'ın değil),\n" .
            "  \"recipient_name\": string|null (alıcı/havale edilen kişinin adı),\n" .
            "  \"sender_name\": string|null (gönderen kişinin adı, varsa),\n" .
            "  \"bank_name\": string|null (bankanın adı: Garanti, Akbank, İş Bankası, Yapı Kredi, Ziraat, vb.),\n" .
            "  \"transaction_date\": string|null (ISO 8601: YYYY-MM-DD HH:MM:SS),\n" .
            "  \"transaction_id\": string|null (referans/işlem no varsa),\n" .
            "  \"confidence\": number (0-100, görüntü kalitesi ve okunabilirlik),\n" .
            "  \"signs_of_tampering\": boolean (görsel/PDF üzerinde Photoshop/GIMP/PDF editor ile DÜZENLEME (orijinal dekont alıp belirli alanları değiştirme) izi varsa true. Detaylı kontrol kuralları aşağıda),\n" .
            "  \"tampering_reasons\": string|null (eğer signs_of_tampering=true ise kısa Türkçe gerekçe — hangi alanda hangi sinyali yakaladın, max 200 karakter),\n" .
            "  \"appears_ai_generated\": boolean (görüntü ChatGPT/DALL-E/Midjourney/Stable Diffusion gibi AI ile sıfırdan ÜRETİLMİŞ mi görünüyor? Detaylı kontrol kuralları aşağıda),\n" .
            "  \"ai_generation_reasons\": string|null (eğer appears_ai_generated=true ise kısa Türkçe gerekçe — hangi sinyali yakaladın, max 200 karakter),\n" .
            "  \"notes\": string (kısa Türkçe analiz notu, max 200 karakter — sadece görsel kalite ve banka format gözlemi yaz)\n" .
            "}\n\n" .
            "ÖNEMLI KURALLAR:\n" .
            "1) IBAN'ı okurken HESAP NUMARASI ile karıştırma. IBAN 'TR' ile başlar, 26 karakterdir. Son 4 rakam = IBAN'ın TR'den sonraki tüm rakamların en son 4 tanesi.\n" .
            "1B) DEKONTTA GENELLİKLE 2 IBAN OLUR. ZORUNLU AKIŞ:\n" .
            "    Adım-1: Görseldeki TÜM IBAN'ları (TR ile başlayan, 26 karakter) listele.\n" .
            "    Adım-2: Her IBAN'ın YANINDAKİ etikete bak: 'Alıcı', 'Karşı Taraf', 'Hedef Hesap', 'Havale Yapılan', 'To/Recipient', 'Hesap Adı: <alıcının adı>' = ALICI. 'Gönderen', 'Kaynak Hesap', 'From/Sender', 'Hesabımdan' = GÖNDEREN.\n" .
            "    Adım-3: Alıcı adı (recipient_name) hangi IBAN'ın yanında yazıyor? O IBAN ALICI'nındır. Gönderen adı hangisinin yanında? O GÖNDEREN'inkidir.\n" .
            "    Adım-4: Sadece ALICI IBAN'ını iban_full alanına yaz. GÖNDEREN IBAN'ını YAZMA.\n" .
            "    Adım-5: Hangisinin ALICI olduğunu kesin ayıramazsan iban_full=null ve iban_last4=null bırak. YANLIŞ IBAN vermektense null vermek MUTLAKA tercih edilir.\n" .
            "    Adım-6: notes alanında 'Alıcı IBAN ...' veya 'Görsel kalitesi düşük' gibi gözlemi yaz — eğer iban_full null bıraktıysan 'Alıcı IBAN net ayırt edilemedi' yaz.\n" .
            "2) signs_of_tampering SADECE görsel manipülasyon/photoshop için. Veri ile beklenen değer arasındaki farklılık tampering DEĞİLDİR — sadece görselde fiziksel düzenleme izi varsa true.\n" .
            "3) notes alanında 'beklenen ile uyuşmuyor' gibi yorum YAPMA. O karşılaştırma backend'de yapılır. Sen sadece görselde ne gördüğünü yaz.\n" .
            "4) NET OKUYAMADIĞIN HİÇBİR DEĞERİ TAHMİN ETME — null bırak. Görüntü bulanık, ters/yan açılı, çözünürlük düşük veya rakam okunamıyorsa ilgili alanı null ver. YANLIŞ tahmin, null'dan çok daha kötü; null verirsen backend daha sonra bu alanı atlar.\n" .
            "5) confidence < 60 ise IBAN ve tutar gibi kritik alanlarda EMİN OLMADIĞIN değerleri null bırakmaya öncelik ver. 'Sanırım', 'galiba' tarzı tahmin yapma — kesin okuyamıyorsan null.\n" .
            "6) Görüntü ters/yan döndürülmüşse zihinsel olarak döndür ve oku; ama netlik yoksa yine null tercih et.\n\n" .
            "AI-GENERATED (YAPAY ÜRETİM) TESPİTİ — appears_ai_generated alanı için kontrol listesi:\n" .
            "ÖNEMLİ: Gerçek dekontlar şu formatlardan birinde olabilir:\n" .
            "   • Mobil bankacılık ekran görüntüsü (status bar, saat, pil olabilir veya kırpılmış olabilir)\n" .
            "   • Mobil uygulamadan kaydedilen PDF/JPG (logo + temiz format olabilir)\n" .
            "   • İnternet şubesi yazdırma çıktısı (logo + standart kurumsal format)\n" .
            "   • ATM dekont fotoğrafı (kağıt dokulu, fotoğraf gren'i)\n" .
            "   • Banka şubesinden alınan basılı dekont\n" .
            "Format çeşitli olabilir — sadece 'düz arkaplan' tek başına AI sinyali DEĞİLDİR. Aşağıdaki SOMUT sinyallerden EN AZ 2'sini ara:\n" .
            "A) **Yazıyla yazılan tutar SEMANTİK uyumsuzluğu (EN GÜÇLÜ SİNYAL)**: Bankalarda tutar genelde rakam + 'Yalnız [Türkçe kelimeler] yazmıştır/Çekilmiştir' formatında yazıyla yazılır.\n" .
            "   ÖNEMLİ: Kompakt/bitişik yazım NORMALDİR — bankalar genelde aralıksız yazar. Örnek GEÇERLİ formatlar (sahte DEĞİL):\n" .
            "     • 'YALNIZ İKİBİNTL.' (2000 TL için — kısa ve doğru)\n" .
            "     • 'Yalnız İkibin Türk Lirası' (klasik)\n" .
            "     • 'BEŞBİNTÜRKLİRASIONLİRAOTUZKURUŞ' (bitişik ama her parça anlamlı)\n" .
            "     • 'BEŞBİNSEKİZTLOTUZYEDİKURUŞ' veya 'BEŞBİNSEKİZLİRAOTUZYEDİKURUŞ'\n" .
            "   SADECE şu durumlarda appears_ai_generated için sinyal say:\n" .
            "     (a) Yazıdaki rakamsal değer dekonttaki tutarla MANTIKEN UYUŞMUYORSA (örn. 5008,37 yazıyor ama yazıyla '... iki bin yedi yüz ...' diyor)\n" .
            "     (b) Yazıda UYDURMA harf dizisi / anlamsız parçalar varsa. Test: yazıyı Türkçe sayı kelimelerine (BİR/İKİ/ÜÇ/.../ON/YİRMİ/.../YÜZ/BİN/MİLYON, LİRA/TÜRKLİRASI/TL, KURUŞ/KR) VEYA '%XX' kuruş formatına ayrıştırmaya çalış. Hiçbir kelimeye/desene uymayan, anlamsız harf bloku varsa (örn. 'ELLİBİNONALTITL**İYETMİŞ**ALTIKR' → 'TLİYETMİŞ' anlamlı bir parça değil) → flag.\n" .
            "     (c) Tutar yüzler/binler/kuruş ayrımı tamamen kopmuşsa (örn. 50016,76 için yazıda 'YETMİŞ' yerine 'YETMİSAL' gibi yarım kelime).\n" .
            "   GEÇERLİ KABUL EDİLECEK ÖRNEKLER (sahte SANMA):\n" .
            "     • 'SEKİZBİNSEKİZ %37' (8008,37 TL — Halkbank/QNB tarzı, '%37' kuruşu temsil eder)\n" .
            "     • 'YALNIZ İKİBİNTL.' (kompakt)\n" .
            "     • 'BEŞBİNTÜRKLİRASIONLİRAOTUZKURUŞ' (bitişik ama parçalar anlamlı)\n" .
            "     • 'Beşbin sekiz Türk lirası otuz yedi kuruş' (klasik açık yazım)\n" .
            "   Bitişik yazım VEYA '%' sembolü TEK BAŞINA sahte demek DEĞİLDİR. Sadece anlamsal bozulma + tutar uyumsuzluğu varsa sahtedir.\n" .
            "B) **Banka logosu eksikliği veya yanlışlığı**: Eğer dekontta bankanın adı geçiyor ama logosu hiç yoksa, veya logo yanlış renk/şekil/oranda ise → şüpheli. NOT: Bazı mobil app screenshot'ları sadece metin gösterebilir, bu tek başına yetersiz.\n" .
            "C) **Mantıksal tutarsızlıklar**: FAST/Hızlı transfer 'anlık' yapılır — işlem tarihi ile valör arasında 1+ gün fark varsa şüpheli. Tutarlar matematiksel olarak tutmuyorsa (toplam ≠ ana tutar + masraf) şüpheli.\n" .
            "D) **Referans/işlem numarası formatı**: Gerçek bankalarda referans no spesifik formatlardadır (Ziraat genelde F<6+ rakam>, Garanti 10-13 hane, İş Bankası 12 hane vs.). Çok düz veya generic numaralar (örn. 12345678, 11111111) → şüpheli.\n" .
            "E) **Font, kerning ve metin işleme**: AI-generated görsellerde tüm metin tek bir text-rendering pipeline'dan çıkar — fontlar fazla mükemmel, harf araları fazla düzenli, JPEG/raster artefaktı yok. Gerçek banka dekontunda hafif sıkıştırma izi, font ailesi karışımı, hafif anti-alias düzensizliği görülür.\n" .
            "F) **Türkçe karakter sorunları**: Bazı AI'lar 'ı/İ', 'ğ/Ğ', 'ş/Ş' gibi Türkçe karakterleri tutarsız işliyor — aynı belgede hem 'İŞLEM' hem 'ISLEM' karışıyor olabilir.\n" .
            "G) **Standart belge unsurları eksikliği**: KVKK metni, ticari sicil no, banka adresi, dipnot, QR kod gibi unsurların TÜMÜ birden eksikse şüpheli. NOT: Mobil app screenshot'larda zaten bu kadarı olmaz, tek başına yetersiz.\n" .
            "H) **KARAR**: A maddesi (yazıyla yazılan tutar uyumsuzluğu) TEK BAŞINA appears_ai_generated=true için yeterlidir. Diğer maddeler için EN AZ 2 sinyal olmalı. ai_generation_reasons'a hangileri yakaladığını yaz.\n\n" .
            "BANKA-ÖZEL / GENEL FORMAT NOTLARI (sahte sanma — bu özellikler gerçek dekontlardadır):\n" .
            "   • **'%XX' formatında kuruş gösterimi**: Bazı Türk bankalarında (Halkbank, QNB Finansbank, Enpara dahil) yazıyla yazılan tutarda kuruş kısmı '%37' veya '%76' gibi yüzde sembolüyle gösterilir. Örnek: 8008,37 TL için yazıyla 'SEKİZBİNSEKİZ %37' yazılabilir; 5008,37 için 'BEŞBİNSEKİZ %37'. Bu GERÇEK BANKA FORMATIDIR, sahtelik DEĞİLDİR. '%' sembolü kuruş ondalık kısmını temsil eder, anlamsız harf dizisi DEĞİLDİR.\n" .
            "   • Ziraat Bankası: 'Yalnız İKİBİNTL.', 'YALNIZ BEŞBİNTL.' gibi kompakt 'YALNIZ <tutar>TL.' yazımı normaldir.\n" .
            "   • Enpara/QNB: Mobil uygulama screenshot'larında banka logosu üstte küçük, alıcı IBAN ayrı bir satırda, gönderen IBAN ayrı satırda yer alır — 2 IBAN olması normal.\n" .
            "   • Garanti BBVA: FAST dekontlarında 'Karşı Taraf' etiketi alıcı için kullanılır.\n" .
            "   • İş Bankası: Dekont üst bölümünde mavi banner ile şube bilgisi olabilir.\n\n" .
            "DEKONT FORMAT BİLGİ TABANI:\n" .
            "Eğitim verinde Türkiye'deki tüm büyük bankaların (Ziraat, Garanti BBVA, İş Bankası, Yapı Kredi, Akbank, Halkbank, VakıfBank, QNB Finansbank, Enpara, Denizbank, ING, TEB, HSBC, Şekerbank, Albaraka, Kuveyt Türk, Türkiye Finans, Fibabanka, Odeabank, Anadolubank, TOM, Param, Papara) tipik dekont formatları, logo yerleşimi, font ailesi, renk paleti, sayfa düzeni, KVKK metni, dipnot konvansiyonları VARDIR. Bu bilgiyi mutlaka aktif olarak kullan:\n" .
            "   • Görseldeki bankanın gerçek dekont formatına UYGUN mu? (Logo pozisyonu, renk, alan dizilimi)\n" .
            "   • Banka'nın gerçek yazıtipi ile dekonttaki yazıtipi tutarlı mı? (Ziraat genelde Helvetica/Arial türevi; Garanti BBVA sans-serif kurumsal; İş Bankası Frutiger benzeri)\n" .
            "   • Banka'nın 'Yalnız <yazıyla tutar> Çekilmiştir' satırı GERÇEKTEN o bankada böyle yazılır mı?\n" .
            "   • Referans no, FAST kodu, EFT format konvansiyonları bilinen formatla uyuşuyor mu?\n" .
            "   • Logo metinsel mi (sahte) yoksa gerçek vektör/raster logo mu?\n" .
            "Bu kontrolden ŞÜPHE çıkıyorsa appears_ai_generated veya signs_of_tampering true ver.\n\n" .
            "GÖRSEL/PDF DÜZENLEMESİ (TAMPERING) TESPİTİ — signs_of_tampering alanı için kontrol listesi:\n" .
            "Bu alan AI-generation'dan FARKLI. Burada orijinal bir dekont alınıp Photoshop / GIMP / Paint / PDF editor (Foxit, Acrobat Pro, vb.) ile belirli alanların DEĞİŞTİRİLMESİ kastedilir. Aramanız gereken sinyaller:\n" .
            "T0a) **TÜM DEKONTTA GENEL FONT TUTARLILIĞI**: Gerçek bir banka dekontu BAŞTAN SONA tek bir font ailesi ve tutarlı font ağırlıkları kullanır (etiketler/değerler arası bold farkı normal — ama font ailesinin kendisi değişmez). Şunlara dikkat et:\n" .
            "    - Şube kodu, IBAN, hesap no, vergi no, tarih, valör, alıcı adı, gönderen adı, tutar satırı, dipnot — TÜM bu alanlarda font ailesi (örn. Arial-vari, Helvetica-vari, serif vs.) aynı mı?\n" .
            "    - Etiket fontu (\"İŞLEM TUTARI\", \"IBAN\" vs.) ile değer fontu (\"5000,00 TRY\", \"TR67 0001 ...\") aynı aileden mi?\n" .
            "    - Alt bölümle üst bölüm arasında font karakteristiği (x-yüksekliği, ascender/descender oranı, terminal stili) değişmiş mi?\n" .
            "    - Bir alanın içinde bile birden fazla font karışmış olabilir (örn. metin alanı bir font, içine sonradan eklenen rakamlar başka font).\n" .
            "    Tek bir satır/alan DAHİ diğerlerinden farklı font ailesinde görünüyorsa o alan eklenmiş/düzenlenmiş demektir → signs_of_tampering=true.\n" .
            "T0b) **KARAKTER-BAZINDA İNCELEME (kritik alanlarda)**: TUTAR rakamlarına ve IBAN rakamlarına KARAKTER KARAKTER bak. Her bir basamağın:\n" .
            "    - Yüksekliği komşu basamaklarla aynı mı? (örn. 5000'deki '5' diğer '0'lardan birkaç piksel uzun/kısa mı?)\n" .
            "    - Genişliği ve kalınlığı (stroke width) aynı font ailesinde mi?\n" .
            "    - Baseline (alt çizgi) tam hizada mı?\n" .
            "    - Anti-aliasing/kenar yumuşatma deseni aynı mı?\n" .
            "    Tek bir basamak DAHİ farklı görünüyorsa o rakam düzenlenmiş demektir → signs_of_tampering=true. Beklenen tutar tamamen eşleşse bile ATLAMA — rakam değiştirilmiş olabilir.\n" .
            "T1) **Farklı font / font weight / kerning**: Aynı alanda (örn. tutar satırı, IBAN satırı) bazı karakterler diğerlerinden hafif farklı font veya kalınlıkta. Genelde tutar/IBAN rakamlarında olur.\n" .
            "T2) **Anti-aliasing tutarsızlığı**: Aynı yazı tipindeki bazı karakterlerin kenarları daha keskin/yumuşak. Üst üste binmiş katmanlardan kaynaklanır.\n" .
            "T3) **JPEG sıkıştırma artefaktları**: Düzenlenen bölgenin etrafında halo veya farklı sıkıştırma deseni. Düzenlenmemiş alan ile düzenlenen alan farklı JPEG kalitesi gösterir.\n" .
            "T4) **Hizalama bozulması**: Tutar/rakam metinlerinin baseline (satır altı çizgisi) komşu metinle aynı hizada değil; piksel olarak kayık.\n" .
            "T5) **Arkaplan/renk uyumsuzluğu**: Düzenlenen alanda arkaplan rengi (genelde beyaz) hafif farklı tonda — minik renk lekesi.\n" .
            "T6) **Kopya-yapıştırma izi**: Aynı karakterin tıpatıp aynı pikseli; ya da çevredeki dokunun (kağıt grenli, ekran piksel deseni) ani kesilmesi.\n" .
            "T7) **Mantıksal tutarsızlık**: Toplam ile alt kalemler uyuşmuyor (örn. 50.000 + 16,76 masraf = 50.016,76 olmalı ama 50.000,76 yazıyor); valör/işlem tarihi gerçekçi değil; bakiye düşüşü tutarla eşleşmiyor.\n" .
            "T8) **Çift değer izi**: Aynı tutar veya IBAN birden fazla yerde yazıyor ve aralarında küçük fark var (yarım düzenleme yapıldığında olur).\n" .
            "T9) **PDF özelinde**: PDF'te metin sıralaması, font subset (alt küme) tutarsızlığı; aynı sayfada birden fazla font ailesi varken normalde tek olmalı; metni seçilebilir alan ile bitmap (raster) alan karışıklığı.\n" .
            "T10) **Yukarıdaki maddelerden EN AZ 2 TANESİ pozitifse signs_of_tampering=true ver ve tampering_reasons'a hangi sinyalleri yakaladığını yaz**.\n" .
            "ÖNEMLİ AYRIM: Eğer hiç orijinal yok, görsel sıfırdan üretilmiş gibi görünüyorsa → appears_ai_generated=true. Orijinal dekont var ama içeriği değiştirilmişse → signs_of_tampering=true. İkisi aynı anda true olabilir.";

        $userText = "Bu görüntüyü banka dekontu olarak analiz et. Beklenen değerler:\n" .
            "- Tutar: {$expectedAmount} TL\n" .
            "- Alıcı IBAN son 4 hane: " . substr(preg_replace('/[^0-9]/', '', $expectedIban), -4) . "\n" .
            "- Alıcı adı: {$expectedName}\n\n" .
            "Bunlarla karşılaştırarak yukarıdaki JSON şemasına uygun cevap ver.";

        $payload = [
            'model' => $model,
            'max_tokens' => 800,
            'messages' => [[
                'role' => 'user',
                'content' => [
                    [
                        'type' => 'image',
                        'source' => [
                            'type' => 'base64',
                            'media_type' => $mimeType,
                            'data' => $base64,
                        ],
                    ],
                    [
                        'type' => 'text',
                        'text' => $userText,
                    ],
                ],
            ]],
            'system' => $systemPrompt,
        ];

        try {
            $res = Http::timeout(45)
                ->withHeaders([
                    'x-api-key' => $apiKey,
                    'anthropic-version' => '2023-06-01',
                    'content-type' => 'application/json',
                ])
                ->post('https://api.anthropic.com/v1/messages', $payload);

            if (! $res->successful()) {
                Log::warning('ClaudeVision API error', ['status' => $res->status(), 'body' => substr($res->body(), 0, 500)]);
                return null;
            }

            $body = $res->json();
            $text = $body['content'][0]['text'] ?? '';
            $rawText = $text;

            // JSON çıkar (model bazen kod bloğuna sarar)
            if (preg_match('/\{[\s\S]+\}/', $text, $m)) {
                $text = $m[0];
            }
            $parsed = json_decode($text, true);
            if (! is_array($parsed)) {
                Log::warning('ClaudeVision JSON parse failed', ['raw' => substr($rawText, 0, 500)]);
                return null;
            }

            $parsed['raw_text'] = $rawText;
            $parsed['_usage'] = [
                'model' => $model,
                'input_tokens'  => (int) ($body['usage']['input_tokens'] ?? 0),
                'output_tokens' => (int) ($body['usage']['output_tokens'] ?? 0),
            ];
            return $parsed;
        } catch (\Throwable $e) {
            Log::warning('ClaudeVision exception: ' . $e->getMessage());
            return null;
        }
    }

    /**
     * API key geçerli mi diye 1 token'lık ping at.
     * @return array { ok: bool, message: string }
     */
    public static function ping(): array
    {
        $apiKey = self::apiKey();
        if (! $apiKey) {
            return ['ok' => false, 'message' => 'API key tanımlı değil.'];
        }

        try {
            $res = Http::timeout(15)
                ->withHeaders([
                    'x-api-key' => $apiKey,
                    'anthropic-version' => '2023-06-01',
                    'content-type' => 'application/json',
                ])
                ->post('https://api.anthropic.com/v1/messages', [
                    'model'      => self::model(),
                    'max_tokens' => 5,
                    'messages'   => [[ 'role' => 'user', 'content' => 'ping' ]],
                ]);

            if ($res->successful()) {
                return ['ok' => true, 'message' => 'Bağlantı başarılı. Model: ' . self::model()];
            }
            $err = $res->json('error.message') ?? $res->body();
            return ['ok' => false, 'message' => 'Hata: ' . substr((string) $err, 0, 200)];
        } catch (\Throwable $e) {
            return ['ok' => false, 'message' => 'İstisna: ' . $e->getMessage()];
        }
    }

    /**
     * Verilen token kullanımına göre USD maliyet tahmini.
     * Claude Haiku 4.5 fiyatlandırması: input $1/Mtok, output $5/Mtok (Mayıs 2026).
     */
    public static function estimateCost(int $inputTokens, int $outputTokens, ?string $model = null): float
    {
        $model = $model ?: self::model();
        $prices = [
            'claude-haiku-4-5'  => ['in' => 1.0,  'out' => 5.0],
            'claude-sonnet-4-6' => ['in' => 3.0,  'out' => 15.0],
            'claude-opus-4-7'   => ['in' => 15.0, 'out' => 75.0],
        ];
        $p = $prices[$model] ?? $prices['claude-haiku-4-5'];
        return ($inputTokens / 1_000_000) * $p['in'] + ($outputTokens / 1_000_000) * $p['out'];
    }
}
