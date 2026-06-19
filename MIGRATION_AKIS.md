# PayDoPay v4 — .NET Core Migrasyon Akış Dokümanı

> Kaynak: `docs/paydopay-v4` (Laravel 12 + Vue 3 / Vuexy SPA)
> Hedef: .NET (Soğan / Onion Mimarisi) + MySQL (`docs/database/paydopay_crm.sql`)
> Frontend: Mevcut Vue SPA **birebir** korunacak (sadece `/api` sözleşmesi sabit kalmalı).

---

## 1. Proje Nedir? (İş Tanımı)

PayDoPay, bir **ödeme aracılık / PSP CRM paneli**dir. Bahis/oyun siteleri (merchant) ile son kullanıcılar (player) arasında **havale/EFT tabanlı yatırım (deposit) ve çekim (withdraw)** işlemlerini yönetir. Para fiziksel banka hesapları (IBAN havuzu) üzerinden akar; panel bu hesapları **takımlara (team)** dağıtır, **kasaları (case/kasa)** muhasebeleştirir, **komisyonları** hesaplar ve **ortaklara (partner)** kâr paylaşımı yapar.

### Ana Aktörler
| Aktör | Tablo | Rol |
|-------|-------|-----|
| Merchant | `merchantUser` | Dış müşteri (bahis sitesi). HMAC API ile işlem açar. Komisyon öder. |
| Team (Takım) | `teams` | İşlemleri işleyen operasyon grubu. IBAN havuzuna sahip, kasası (`overturn`) var. |
| User (Personel) | `users` | Panel kullanıcısı (SuperAdmin/SubAdmin/TeamAdmin/TeamAgent/Merchant/Blocked). |
| Intermediary (Aracı) | `new_intermediaries` | Merchant/Team üzerinden komisyon alan aracı. |
| Partner (Ortak) | `paylira_partners` | Paylira net kârından pay alan ortak. |
| Fund Storage (Fon Deposu) | `fund_storages` | TL nakit / kripto (USDT-TRX) / kredi hattı kasaları. |
| Player (Oyuncu) | (kimliksiz, `invest.player_id`) | Son kullanıcı. Ödeme sayfasını `u_id` ile görür. |

### Çekirdek Tablo: `invest`
Tüm deposit & withdraw işlemleri tek tabloda tutulur.
- `type`: 1=Deposit, 2=Withdraw
- `status`: 0=Bekliyor/Havuz, 1=Pending (IBAN verildi), 2=Processing (ödendi/işleniyor), 3=Approved, 4=Rejected
- Kilit alanlar: `order_id` (merchant sipariş no, global unique), `u_id` (128-bit ödeme token'ı), `firm_id` (merchant), `team_id`, `bank_id`, `agent_id`, `amount`, `panel_commissin_amount` (sic), `iban`, `receipt_path`, `callbackUrl/callbackOkUrl/callbackFailUrl`, `callbackSended`, `ibanSeen`, `rejectType`, tarih alanları (`created_at`, `form_at`, `process_date`, `finalize_date`).

---

## 2. Dış API Akışları (Merchant Entegrasyonu — v1)

> Bu sözleşme **dış müşteriler tarafından kullanıldığı için birebir korunmalı**. `/api/v1/*`

### 2.1 Kimlik Doğrulama — HMAC (Stripe tarzı)
`MerchantApiAuth` middleware:
- Header'lar: `X-Api-Key`, `X-Timestamp` (unix sn), `X-Signature`
- `signed_payload = METHOD + "\n" + PATH + "\n" + TIMESTAMP + "\n" + sha256(body)`
- `signature = hex(hmac_sha256(merchant.apiSecret, signed_payload))`
- Drift toleransı: ±300 sn. `merchantUser.status='1'` olmalı.
- Her istek `api_callback_logs` (direction=in) + legacy `apiRequestLog`'a yazılır.

### 2.2 Deposit Oluşturma — `POST /api/v1/deposit` (hosted ödeme sayfası)
1. Tutar kontrolü (`minDeposit`/`maxDeposit`).
2. Blacklist kontrolü (`type=1`, player_id).
3. Duplicate `order_id`/`u_id` kontrolü (sistem geneli) → 409.
4. Aynı oyuncunun 10 dk içinde bekleyen işlemi varsa kilit → 409.
5. Komisyon hesabı: `round(amount * commission / 100, 2)`.
6. `u_id = hex(random_bytes(16))` (32 hex).
7. `invest` kaydı **status=0, ibanSeen=0** ile açılır (IBAN henüz yok).
8. Yanıt: `pay_url = https://.../pay/{u_id}`.

### 2.3 Deposit Direct (H2H) — `POST /api/v1/deposit/direct`
Aynı validasyonlar; **IBAN anında seçilir** (`MerchantBankService::pickOne`), `status=1, ibanSeen=1` ile kaydedilir, IBAN yanıtta döner. IBAN yoksa 503.

### 2.4 Withdraw — `POST /api/v1/withdraw`
Blacklist + duplicate kontrolü; `iban` regex `^TR\d{24}$`; `type=2, status=0` (admin onayı bekler) kaydı açılır.

### 2.5 Durum Sorgu — `GET /api/v1/transaction/{order_id}`
`order_id` veya `u_id` ile arar; status→label map (`approved/rejected/waiting`), HTTP kodu duruma göre (200/201/202).

### 2.6 Public Ödeme Sayfası — `/api/v1/pay/{u_id}` (auth yok, u_id ile korunur)
- `GET /{u_id}`: işlemi getir; süre dolmuşsa (`pay_link_expiry_*`) status=4 + expire callback; gerekirse otomatik IBAN ata.
- `POST /{u_id}/select-bank`: oyuncu IBAN seçer (ibanSeen=1, status=1).
- `POST /{u_id}/paid`: oyuncu "ödedim" der (status 1→2).
- `POST /{u_id}/cancel`: iptal (status→4, rejectType=5).
- `POST /{u_id}/receipt`: dekont yükle (gerçek MIME kontrolü, max 10MB, private `storage/app/receipts/`).

### 2.7 Callback (Merchant Webhook) — `CallbackService`
- `hash = sha256(apiKey + "|" + order_id + "|" + (approved?'true':'false'))`
- Approved → `{code:200,status:true,uID,saleID,amount,senderName,hash,message}`
- Rejected → `{code:201,status:false,...,detail}`
- `merchant.new_api=1` → JSON gövde; aksi halde form-encoded. 10 sn timeout. 2xx → `callbackSended=1`.
- Expire callback ayrı (`sendExpire`).

### 2.8 Banka Seçim Motoru — `MerchantBankService` (kritik iş kuralı)
Uygun IBAN seçim filtreleri (hepsi geçmeli): hesap & takım `status=1`, tutar hesap/takım `min/max_invest` aralığında, takım `wait_limit ≥` bekleyen işlem sayısı, günlük adet (`daily_count_limit`) ve tutar (`max_amount`) limitleri, opsiyonel zorunlu takım, ve `block_when_full=1` ise `current_cash + amount ≤ maxCase`. Sıralama: `sort_order` → round-robin/random. `maxCase` aşımında takım otomatik pasife çekilir + Telegram uyarısı.

---

## 3. İç Panel Akışları (Admin/Operasyon)

> `/api/*` (Sanctum bearer token + idle timeout). Frontend bunları kullanır.

### 3.1 Kimlik & Yetki
- **Login** `POST /api/auth/login`: kullanıcı/şifre (**MD5 legacy hash**), `status` & `otp_ok` kontrolü → 2FA gerekiyorsa şifreli `temp_token` + QR döner, yoksa Sanctum token.
- **2FA** `POST /api/auth/two-factor`: TOTP (`google2fa`, window=4), `users.otp_code` sırrı.
- **me / logout / change-password** (change-password tüm token'ları siler).
- **Token**: `personal_access_tokens` tablosu (opaque, plaintext frontend localStorage). TTL 8 saat + 30 dk idle (`EnforceIdleTimeout`).
- **Roller** (`users.user_type`): 1=SuperAdmin, 2=TeamAgent, 3=Merchant, 4=SubAdmin, 5=TeamAdmin, 6=Blocked. Yetkiler User modelindeki `can*()` metotları + frontend scope (team/merchant) filtresi.

### 3.2 Deposit Yönetimi (`DepositController`)
`pending`/`all`/`detail`/`receipt`/`filter-meta` + `approve` (status→3, opsiyonel tutar değişimi, callback) / `reject` (status→4, rejectType, 10 dk kuralı admin hariç) / `resend-callback` (SuperAdmin). TrustScore hesabı oyuncu geçmişinden.

### 3.3 Withdraw Yönetimi (`WithdrawController`)
Havuz (status=0) → `take` (kullanıcıya ata, →2) → `approve` (dekont zorunlu, →3) / `reject` (→4) / `release` (havuza geri). Dekont yükleme (multipart/base64), SHA256 + perceptual hash, `VerifyReceiptJob` ile **ClaudeVisionService** AI doğrulama; `receipt-review`, manuel doğrulama, sahte işaretleme (`fake_receipt_templates`). `bulk-assign`, eksik dekont Telegram bildirimi.

### 3.4 Kasa (Case) Muhasebesi — Çekirdek Mantık
Her varlık için **kasa = son snapshot + bugünün net hareketi**. Snapshot'lar `daily_case_snapshots` (entity_type: merchant/merchant_group/team/intermediary/partner/paylira) — gün sonu cron.

- **Merchant kasası** (`caseNow`): `+ (deposit - deposit*komisyon) - (withdraw + withdraw*withdrawKomisyon) - ödemeler`.
- **Team kasası** (`overturn`): `+ deposit*teamKomisyon` (alacak) / withdraw, ödeme, transfer, sync, gider, aracı/partner ofset hareketleri.
- **Intermediary**: merchant/team başına `deposit * commission_rate` birikimi - ödemeler.
- **Paylira net**: tüm merchant komisyonları + delivery profit − team komisyonları − aracı komisyonları − giderler.
- **Partner**: `paylira_net * share_percent + capital - payments - expense_shares`.
- İlgili controller'lar: `MerchantCaseController`, `TeamCaseController`, `IntermediaryCaseController`, `PayliraPartnerController`, `CaseReportController`, `FundStorageController`. Ödeme/transfer/sync silme yalnız **aynı gün**; `team_action_log` denetim kaydı tutar. Her para hareketinde `enforceMaxCase`.

### 3.5 Diğer Yönetim Modülleri
- **Merchant** CRUD + grup + apiKey/apiSecret rotasyonu.
- **Team** CRUD (limitler, komisyon, maxCase, Telegram bayrakları).
- **Intermediary** CRUD + merchant/team attach (pivot + rate).
- **Bank Account** CRUD (IBAN'dan banka tanıma, sort_order, günlük kullanım).
- **Fund Storage** (TL/kripto/kredi) + transfer/sync + TRON TX lookup (TronGrid/TronScan).
- **Blacklist** (player/IBAN/IP/email) + Excel export.
- **User** yönetimi (rol bazlı scope).
- **Settings** (system_settings, Anthropic test, Telegram chat-id, loglar).
- **Dashboard** (stats, merchant-cases, yearly-volume, team-performance, player-stats) + public **widget** (token).
- **Reports**: merchant/team/operations/conversion/bank-account/player-risk raporları.
- **Export** (async kuyruk `export_jobs` + `ExportTransactionsJob`, XLSX, token'lı download).

### 3.6 Arka Plan İşleri (Jobs / Console)
- `ExportTransactionsJob`, `VerifyReceiptJob` (kuyruk).
- Cron: `DailyCaseSnapshot`, `CheckPendingNotifications`, `CheckLowAmountPlayerRisk`, `ExpirePendingInvests`, `SetTelegramWebhook`, `ProvisionApiSecrets`.

### 3.7 Harici Servisler
`CallbackService`, `MerchantBankService`, `TwoFactorService`, `ClaudeVisionService` (Claude API — dekont OCR/analiz), `PerceptualHashService`, `TelegramService`, `FileMetadataService`, `TrustScore`.

---

## 4. Frontend Sözleşmesi (Değişmeyecek)
- SPA build (`resources/js/main.js` → Vite) `public/build`, `application.blade.php` ile servis edilir.
- API client `ofetch`, base `/api`, `Authorization: Bearer {token}`.
- Token frontend'de localStorage; router guard `localStorage.user.user_type` ile rol kontrolü.
- **.NET tarafı bu sözleşmeyi (route'lar, JSON şekilleri, bearer token doğrulama) aynen sağlamalı.** SPA fallback: API dışı tüm GET → `index.html`.

---

## 5. Hedef Mimari (Soğan / Onion)

```
PayDoPay.sln
├── src/
│   ├── PayDoPay.Domain          # Entity'ler, enum'lar, domain servis arayüzleri, iş kuralları (bağımsız)
│   ├── PayDoPay.Application      # Use-case'ler (servisler/handler), DTO'lar, arayüzler (IRepository, ICallbackService...)
│   ├── PayDoPay.Infrastructure   # EF Core (MySQL/Pomelo) + Dapper, repository impl, harici servisler (Telegram/Claude/Tron), kuyruk, cron
│   └── PayDoPay.Api              # ASP.NET Core Web API — controller'lar, middleware (HMAC, idle timeout), auth, SPA servis
├── tests/
└── docs/ (mevcut kaynak)
```
- **Bağımlılık yönü içe doğru**: Api → Infrastructure → Application → Domain.
- **DB**: MySQL, mevcut `paydopay_crm.sql` şeması **olduğu gibi** (camelCase/snake_case karışık) — EF Core `[Column]`/Fluent map ile birebir eşlenir, yeni migration yok.
- **Auth**: `personal_access_tokens` tablosuna karşı opaque bearer doğrulama (custom AuthenticationHandler) → frontend değişmez. MD5 şifre doğrulaması korunur.
- **Raporlar/ağır sorgular**: Dapper ile raw SQL (Laravel'deki DB::raw mantığına en yakın, en az risk).

---

## 6. Önerilen Çalışma Sırası (Faz Planı)
1. **Faz 0 – İskelet**: Solution + 4 katman, MySQL bağlantısı, EF Core DbContext + tüm entity eşlemeleri, SPA servis + health.
2. **Faz 1 – Auth**: login/2FA/me/logout/change-password, opaque token handler, idle timeout, rol yetkileri. (Frontend login çalışır hale gelir.)
3. **Faz 2 – Dış v1 API**: HMAC middleware, deposit/direct/withdraw/transaction, public pay/*, CallbackService, MerchantBankService. (Dış entegrasyon korunur.)
4. **Faz 3 – İşlem Yönetimi**: Deposit & Withdraw controller'ları, dekont, TrustScore.
5. **Faz 4 – Kasa Muhasebesi**: Merchant/Team/Intermediary/Partner case + FundStorage + snapshot + enforceMaxCase.
6. **Faz 5 – Yönetim & Raporlar**: Merchant/Team/Bank/User/Blacklist/Settings + Dashboard + Reports + Export.
7. **Faz 6 – Arka plan**: Jobs (HostedService/Hangfire) + cron + harici servisler (Telegram/Claude/Tron).
8. **Faz 7 – Doğrulama**: Frontend'i .NET API'ye bağlayıp uçtan uca test.

---

> Sıradaki adım: aşağıdaki mimari kararlar netleştikten sonra **Faz 0** ile başlanacak.
