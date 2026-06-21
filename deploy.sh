#!/usr/bin/env bash
# ============================================================
#  TPanel — Docker deploy script
#  Kullanım: proje kökünde  ./deploy.sh
#  Güncelleme: git pull && ./deploy.sh
# ============================================================
set -euo pipefail

cd "$(dirname "$0")"

PORT="${APP_PORT:-8080}"     # docker-compose app portu (8080:8080)
HEALTH_URL="http://localhost:${PORT}/api/v1/health"

info()  { printf '\033[1;36m>> %s\033[0m\n' "$*"; }
warn()  { printf '\033[1;33m!! %s\033[0m\n' "$*"; }
err()   { printf '\033[1;31mHATA: %s\033[0m\n' "$*" >&2; }

# --- 1) Önkoşullar ---
command -v docker >/dev/null 2>&1 || { err "docker kurulu değil. README 'Yöntem A — detaylı adımlar / 0) Docker'ı kur'."; exit 1; }
docker compose version >/dev/null 2>&1 || { err "'docker compose' (Compose v2) bulunamadı. docker-compose-plugin kurun."; exit 1; }

# --- 2) .env kontrolü ---
if [ ! -f .env ]; then
  warn ".env yok — .env.example'dan oluşturuldu. Şifreleri düzenleyip tekrar çalıştırın:"
  cp .env.example .env
  warn "  nano .env   →  DB_*_PASSWORD ve APP_URL'i ayarla"
  exit 1
fi
if grep -qE 'degistir_|=rootpass|=tpanelpass' .env; then
  warn ".env hâlâ örnek/varsayılan şifreler içeriyor — production için güçlü şifrelerle değiştirin."
fi

# --- 3) Build + başlat ---
info "docker compose up -d --build"
docker compose up -d --build

# --- 4) Sağlık beklemesi (max ~90sn) ---
info "Uygulamanın hazır olması bekleniyor (${HEALTH_URL})..."
ok=0
for i in $(seq 1 30); do
  if curl -fsS "$HEALTH_URL" >/dev/null 2>&1; then ok=1; break; fi
  sleep 3
done
if [ "$ok" != 1 ]; then
  err "Uygulama 90sn içinde yanıt vermedi. Logları inceleyin:"
  echo "    docker compose logs --tail=50 app"
  exit 1
fi

# --- 5) Özet ---
info "Sağlık kontrolü OK."
docker compose ps
echo ""
printf '\033[1;32m✅ TPanel çalışıyor: http://localhost:%s\033[0m\n' "$PORT"
echo "   Giriş: velmort / 123456"
echo "   HTTPS için önüne reverse proxy koyun (README: Yöntem A / adım 5)."
