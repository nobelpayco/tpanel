# TPanel PSP CRM Paneli (.NET)

Ödeme aracılık/PSP CRM paneli. Banka IBAN'ları üzerinden yatırım/çekim işlemleri, takım bazlı kasa muhasebesi, komisyon ve partner kâr paylaşımı yönetimi.

Bu depo, mevcut **PHP/Laravel 12 + Vue 3** projesinin (`docs/paydopay-v4`) **.NET 10 + Soğan (Onion) mimarisi**ne taşınmış halidir. Frontend (Vue SPA) birebir korunmuş, `/api` sözleşmesi değiştirilmemiştir.

---

## Mimari

**Soğan Mimarisi** — içe doğru bağımlılık: `Api → Infrastructure → Application → Domain`

| Katman | Sorumluluk |
|--------|-----------|
| **TPanel.Domain** | Entity'ler, enum'lar, iş kuralları (bağımlılıksız çekirdek) |
| **TPanel.Application** | Arayüzler (`I*Store`, `I*Service`), DTO/feature mantığı |
| **TPanel.Infrastructure** | Dapper/EF Core veri erişimi, dış servisler (Telegram, Claude Vision), arka plan işleri |
| **TPanel.Api** | İnce controller'lar, auth, middleware, SPA servisi |

**Teknolojiler:** .NET 10, ASP.NET Core, EF Core 9 (Pomelo MySQL) + Dapper (hibrit), MySQL, Vue 3 (Vuexy) + Vite.

---

## Dizin Yapısı

```
tpanel/
├── PayDoPay.slnx               # .NET çözüm dosyası
├── src/
│   ├── backend/                # .NET projeleri (Onion)
│   │   ├── TPanel.Domain/
│   │   ├── TPanel.Application/
│   │   ├── TPanel.Infrastructure/
│   │   └── TPanel.Api/         # giriş noktası — hem /api hem SPA'yı servis eder
│   └── frontend/               # Vue 3 SPA (Vuexy) — kaynak + derlenmiş build
│       ├── resources/          # Vue kaynak kodu
│       └── public/build/       # Vite çıktısı (.NET bunu servis eder)
└── docs/
    ├── paydopay-v4/            # Orijinal Laravel projesi (referans) + runtime storage
    └── database/tpanel_crm.sql
```

> **Not:** Backend ve frontend tek origin'den sunulur — .NET hem `/api/*`'i hem Vue SPA'yı (`/`) aynı port üzerinden verir. Frontend `/api` göreli base URL kullanır.

---

## Gereksinimler

- **.NET 10 SDK** (bu makinede `C:\Program Files\dotnet\dotnet.exe`)
- **MySQL** — XAMPP ile `localhost:3306`, `paydopay_crm` veritabanı yüklü (`docs/database/tpanel_crm.sql`)
- **Node.js + pnpm** — *yalnızca frontend'i yeniden derlemek için gerekir.* Sadece çalıştırmak için gerekmez (derlenmiş build depoda mevcut).

---

## Kurulum & Çalıştırma

### 1. Veritabanı
XAMPP Control Panel'den **MySQL**'i başlatın. `paydopay_crm` şeması yüklü olmalı. Bağlantı dizesi (`src/backend/TPanel.Api/appsettings.json`):

```
Server=127.0.0.1;Port=3306;Database=paydopay_crm;User Id=root;Password=
```

### 2. Uygulamayı çalıştırma

**Geliştirme (önerilen):**
```powershell
dotnet run --project src\backend\TPanel.Api
```
> `launchSettings.json`'a göre `http://localhost:5212` (Development). Durdurmak: `Ctrl+C`.

**Port/ortam override ile:**
```powershell
$env:ASPNETCORE_URLS="http://localhost:5080"; $env:ASPNETCORE_ENVIRONMENT="Production"
dotnet run --project src\backend\TPanel.Api
```

**Derlenmiş DLL'i doğrudan (script/CI için):**
```powershell
cd src\backend\TPanel.Api
$env:ASPNETCORE_URLS="http://localhost:5080"
& "C:\Program Files\dotnet\dotnet.exe" ".\bin\Debug\net10.0\TPanel.Api.dll"
```
> Çalışma dizini `TPanel.Api` klasörü olmalı (appsettings + göreli frontend/storage yolları için).

### 3. Erişim
- Panel: **http://localhost:5212** (veya seçtiğiniz port)
- Test kullanıcısı: **`velmort` / `123456`** (super admin, 2FA kapalı)
- API sağlık kontrolü: `/api/v1/health`

---

## Frontend (Vue SPA)

Derlenmiş build depoda olduğundan **çalıştırmak için ek işlem gerekmez** — .NET servis eder.

**Yeniden derlemek için** (Node.js gerekir):
```powershell
cd src\frontend
pnpm install
pnpm build        # -> public/build güncellenir
```

> `vite.config.js` hâlâ `laravel-vite-plugin` kullanır; Laravel kökü dışında build sırasında uyarı verebilir ama çıktıyı yine `public/build`'e üretir. Hot-reload'lu geliştirme (`pnpm dev`) için ek bağlama gerekir.

---

## Derleme

```powershell
dotnet build PayDoPay.slnx -c Release
```

---

## Yapılandırma

`src/backend/TPanel.Api/appsettings.json` anahtarları:

| Anahtar | Açıklama |
|---------|----------|
| `ConnectionStrings:MySql` | MySQL bağlantı dizesi |
| `Frontend:PublicPath` | SPA static dosya kökü (`../../frontend/public`) |
| `Storage:LocalDiskPath` / `PublicDiskPath` | Dekont/dosya depolama (`docs/paydopay-v4/storage`) |
| `Telegram:BotToken` / `PayrouteChatId` | Telegram bildirimleri (boşsa no-op) |

> **Sırlar:** Prodüksiyon değerlerini (DB şifresi, Telegram/Anthropic token) `appsettings.json`'a yazıp commit etmeyin; ortam değişkeni veya `appsettings.*.local.json` kullanın (`.gitignore` ile dışlanır). Anthropic API key runtime'da `system_settings` tablosundan okunur.

---

## Özellikler (taşınan)

- **Kimlik doğrulama** — Sanctum uyumlu opaque token, MD5 legacy şifre uyumu, 2FA (TOTP)
- **Dış Merchant API (v1)** — HMAC korumalı deposit/withdraw/transaction uçları
- **İşlem yönetimi** — yatırım/çekim onay-red akışı, dekont yükleme
- **Kasa muhasebesi** — takım/merchant/aracı/partner/fon deposu kasaları, devirli günlük snapshot
- **Yönetim CRUD + Dashboard + Export** — async CSV/XLSX dışa aktarım
- **6 analitik rapor** — merchant/team/operations/conversion/player-risk/bank-account
- **Arka plan işleri** — günlük kasa snapshot cron, süre dolan yatırım reddi, Telegram bildirimleri
- **AI dekont doğrulama** — Claude Vision OCR + perceptual hash (dHash) + metadata analizi

### Kapsam dışı (manuel/ileride)
Telegram inbound webhook (`TelegramWebhookController`), `telegram:set-webhook`, `ProvisionApiSecrets` ve test-data üretimi henüz port edilmedi (canlı bot / public URL gerektirir).

---

## Lisans

Özel/dahili proje.
