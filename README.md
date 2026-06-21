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
├── TPanel.slnx                 # .NET çözüm dosyası
├── src/
│   ├── backend/                # .NET projeleri (Onion)
│   │   ├── TPanel.Domain/
│   │   ├── TPanel.Application/
│   │   ├── TPanel.Infrastructure/
│   │   └── TPanel.Api/         # giriş noktası — hem /api hem SPA'yı servis eder
│   └── frontend/               # Vue 3 SPA (Vuexy) — kaynak + derlenmiş build
│       ├── resources/          # Vue kaynak kodu
│       └── public/build/       # Vite çıktısı (.NET bunu servis eder)
├── storage/                    # Runtime depolama (dekontlar, export'lar) — .NET buraya yazar
└── docs/
    ├── paydopay-v4/            # Orijinal Laravel projesi (yalnızca referans)
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
dotnet build TPanel.slnx -c Release
```

---

## Canlı Ortama Kurulum (Production)

Uygulama **tek bir ASP.NET Core süreci**dir — `/api` ile Vue SPA'yı aynı porttan sunar. Önüne TLS için bir reverse proxy (nginx/Caddy), arkasına MySQL/MariaDB konur.

### Yöntem A — Docker (önerilen)

Repoda hazır `Dockerfile` + `docker-compose.yml` var (app + MySQL 8.0; frontend imaja derlenmiş haliyle gömülür, storage kalıcı volume).

```bash
git clone <repo> tpanel && cd tpanel
cp .env.example .env          # güçlü şifreler + APP_URL=https://panel.alanadiniz.com
docker compose up -d --build
```

- Panel: `http://SUNUCU:8080` (ilk açılışta şema `docs/database/tpanel_crm.sql`'den otomatik yüklenir).
- **HTTPS:** önüne reverse proxy koyun (`proxy_pass http://127.0.0.1:8080`, `client_max_body_size 12M`) + Let's Encrypt. Caddy ile otomatik TLS de tercih edilebilir.
- **MariaDB** isterseniz `db` imajını `mariadb:11` yapıp `Database__ServerVersion: "11.4.0-mariadb"` ayarlayın.
- Şemayı sonradan değiştirdiyseniz: `docker compose down -v` (⚠️ veriyi siler) → yeniden `up`.

Komutlar: `docker compose logs -f app` · `docker compose restart app` · `docker compose down`.

#### Yöntem A — detaylı adımlar (sıfırdan Debian sunucu)

**0) Docker'ı kur (bir kez)** — resmi depodan:
```bash
sudo apt update && sudo apt install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update && sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
docker compose version          # doğrula
```

**1) Projeyi al:**
```bash
cd /opt && sudo git clone <repo-url> tpanel && cd tpanel
```

**2) `.env` ayarla:**
```bash
cp .env.example .env && nano .env
# DB_ROOT_PASSWORD / DB_USER / DB_PASSWORD → güçlü şifreler
# APP_URL → https://panel.alanadiniz.com
# TELEGRAM_* → opsiyonel (boş bırakılabilir)
```

**3) Build + başlat** — repo kökündeki `deploy.sh` ile (önkoşul + .env kontrolü + sağlık beklemesi dahil):
```bash
./deploy.sh
# veya elle: docker compose up -d --build
```
İlk build birkaç dakika sürer (.NET restore + publish). `up` sırasıyla: `app` imajını derler (publish + derlenmiş SPA gömülür) → MySQL'i başlatır ve **ilk açılışta** `docs/database/tpanel_crm.sql`'i otomatik yükler → healthcheck geçince `app` başlar.

**4) Doğrula:**
```bash
docker compose ps                          # running/healthy
docker compose logs -f app                 # "Now listening on: http://[::]:8080"
curl http://localhost:8080/api/v1/health   # API sağlık
```

**5) HTTPS — önüne reverse proxy** (compose yalnızca HTTP 8080 açar):
```bash
sudo apt install -y nginx certbot python3-certbot-nginx
```
`/etc/nginx/sites-available/tpanel`:
```nginx
server {
    listen 80;
    server_name panel.alanadiniz.com;
    client_max_body_size 12M;                 # dekont yüklemeleri için ŞART
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```
```bash
sudo ln -s /etc/nginx/sites-available/tpanel /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d panel.alanadiniz.com   # otomatik TLS
```
→ Panel: **https://panel.alanadiniz.com** · giriş `velmort / 123456`.

**6) Günlük operasyonlar:**

| İşlem | Komut |
|---|---|
| Kodu güncelle | `git pull && ./deploy.sh` |
| Logları izle | `docker compose logs -f app` |
| Yeniden başlat | `docker compose restart app` |
| Durdur (veri kalır) | `docker compose down` |
| DB yedeği | `docker compose exec db mysqldump -u root -p"$DB_ROOT_PASSWORD" paydopay_crm > yedek.sql` |
| Storage yedeği | `docker run --rm -v tpanel_storage:/s -v $PWD:/b alpine tar czf /b/storage.tgz -C /s .` |

> **Şema değişikliği:** init script yalnızca ilk açılışta çalışır. Baştan yüklemek için `docker compose down -v` (⚠️ DB verisini siler) → `./deploy.sh`.

### Yöntem B — Debian (bare-metal + systemd)

```bash
# 1) Bağımlılıklar (libicu ŞART — uygulama tr-TR kültürü kullanır)
sudo apt update && sudo apt install -y nginx libicu-dev mariadb-server
sudo apt install -y aspnetcore-runtime-10.0   # veya self-contained publish ile bu adımı atla

# 2) Veritabanı
sudo mysql -e "CREATE DATABASE paydopay_crm CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
sudo mysql -e "CREATE USER 'tpanel'@'localhost' IDENTIFIED BY 'GUCLU_SIFRE'; GRANT ALL ON paydopay_crm.* TO 'tpanel'@'localhost';"
mysql -u tpanel -p paydopay_crm < docs/database/tpanel_crm.sql

# 3) Publish (yerelde) + sunucuya kopyala
dotnet publish src/backend/TPanel.Api -c Release -o publish
rsync -avz publish/             user@sunucu:/opt/tpanel/app/
rsync -avz src/frontend/public/ user@sunucu:/opt/tpanel/frontend/
ssh user@sunucu 'mkdir -p /opt/tpanel/storage/app/public/receipts'
```

Sunucuda `/opt/tpanel/app/appsettings.Production.json` — **mutlak yollar** + gerçek sırlar:
```json
{
  "ConnectionStrings": { "MySql": "Server=127.0.0.1;Port=3306;Database=paydopay_crm;User Id=tpanel;Password=GUCLU_SIFRE;CharSet=utf8mb4;SslMode=None;AllowPublicKeyRetrieval=true" },
  "Database": { "ServerVersion": "11.4.0-mariadb" },
  "Frontend": { "PublicPath": "/opt/tpanel/frontend" },
  "Storage": { "LocalDiskPath": "/opt/tpanel/storage/app", "PublicDiskPath": "/opt/tpanel/storage/app/public" },
  "App": { "Url": "https://panel.alanadiniz.com", "Name": "TPanel" }
}
```

systemd birimi (`/etc/systemd/system/tpanel.service`):
```ini
[Unit]
Description=TPanel
After=network.target mariadb.service
[Service]
WorkingDirectory=/opt/tpanel/app
ExecStart=/usr/bin/dotnet /opt/tpanel/app/TPanel.Api.dll
Restart=always
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
[Install]
WantedBy=multi-user.target
```
```bash
sudo chown -R www-data:www-data /opt/tpanel/storage
sudo systemctl enable --now tpanel
```

nginx + TLS:
```nginx
server {
    listen 80;
    server_name panel.alanadiniz.com;
    client_max_body_size 12M;          # dekont yüklemeleri (≤10MB) için
    location / { proxy_pass http://127.0.0.1:5000; proxy_set_header Host $host; proxy_set_header X-Forwarded-Proto $scheme; proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for; }
}
```
```bash
sudo certbot --nginx -d panel.alanadiniz.com
```

### Production'da dikkat edilecekler

- **libicu zorunlu** — uygulama Türkçe (`tr-TR`) biçimlendirme kullanır; eksikse çöker.
- **`client_max_body_size ≥ 12M`** — dekont yüklemeleri (≤10 MB).
- **`Database:ServerVersion`** kurulu motora göre olmalı (MySQL ≠ MariaDB).
- **Tek instance çalıştırın** — günlük snapshot/cron uygulama içinde `HostedService` olarak koşar; 2 instance çift snapshot üretir.
- **Sırlar** ortam değişkeni / `appsettings.Production.json` ile verilir, repoya commit edilmez. Anthropic API key panel → Sistem Ayarları'ndan (`system_settings`) okunur.
- Frontend değiştiyse `cd src/frontend && pnpm build` → `public/` çıktısını sunucuya kopyalayın (Docker'da imajı yeniden build edin).

---

## Yapılandırma

`src/backend/TPanel.Api/appsettings.json` anahtarları:

| Anahtar | Açıklama |
|---------|----------|
| `ConnectionStrings:MySql` | MySQL bağlantı dizesi |
| `Frontend:PublicPath` | SPA static dosya kökü (`../../frontend/public`) |
| `Storage:LocalDiskPath` / `PublicDiskPath` | Dekont/dosya depolama (repo kökü `storage/`) |
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
