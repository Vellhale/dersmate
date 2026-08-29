#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# dersmate — yeni sürümü yayına alma (Linux sunucu).
#
#   ./tools/dagit.sh              → main dalını çeker ve yayına alır
#   ./tools/dagit.sh --atla-yedek → yedeği atlar (ÖNERİLMEZ)
#
# İLK KURULUM İÇİN DEĞİL. Sıfırdan kurulum: docs/SUNUCUYA-KURULUM.md
# Bu betik, ZATEN ÇALIŞAN bir kurulumu günceller.
#
# SIRA BİLİNÇLİ ve her adımın bir sebebi var:
#   1. Yedek        — göç geri alınamaz; öncesinde bir dönüş noktası olmalı
#   2. git pull     — kod
#   3. Arayüz derle — VITE_API_URL gömülü paket
#   4. İmaj kur     — API
#   5. GÖÇ          — uygulama AYAKTAYKEN değil, ayrı konteynerde
#   6. Yeniden başlat
#   7. Sağlık kontrolü
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

COMPOSE="docker compose -f docker-compose.prod.yml --env-file .env.production"
ATLA_YEDEK="${1:-}"

if [ ! -f .env.production ]; then
    echo "HATA: .env.production yok. Depo kökünde misin?" >&2
    exit 1
fi

API_URL="$(grep -E '^API_URL=' .env.production | cut -d= -f2-)"

# ─── 1. Yedek ───────────────────────────────────────────────────────────────
if [ "$ATLA_YEDEK" != "--atla-yedek" ]; then
    echo "▸ [1/7] Yedek alınıyor…"
    ./tools/yedek-al.sh
else
    echo "▸ [1/7] Yedek ATLANDI (--atla-yedek)."
    echo "        Göç geri alınamaz; bir sorun çıkarsa dönüş noktan yok."
fi

# ─── 2. Kod ─────────────────────────────────────────────────────────────────
echo "▸ [2/7] Kod çekiliyor…"
ONCEKI="$(git rev-parse --short HEAD)"
git pull --ff-only
YENI="$(git rev-parse --short HEAD)"

if [ "$ONCEKI" = "$YENI" ]; then
    echo "        Değişiklik yok ($YENI). Yine de yeniden kurulacak."
else
    echo "        $ONCEKI → $YENI"
    git log --oneline "$ONCEKI..$YENI" | sed 's/^/        /'
fi

# ─── 3. Arayüz ──────────────────────────────────────────────────────────────
# Sunucuda derleniyor çünkü dist/ depoda değil. Node kurulu değilse bu adımı
# kendi makinende yapıp dist/ klasörünü rsync ile gönder ve burayı atla.
echo "▸ [3/7] Arayüz derleniyor…"
( cd frontend && npm ci --silent && npm run build )

# ⚠️ SON SAVUNMA. vite.config.js zaten localhost'lu bir üretim derlemesini
# durduruyor; bu kontrol o kapının çalıştığını doğruluyor. Paketin içinde
# localhost varsa canlıda HİÇBİR istek çalışmaz ve site "açılıyor ama boş".
if grep -q "localhost" frontend/dist/assets/*.js 2>/dev/null; then
    echo "HATA: derlenen pakette 'localhost' geçiyor — dağıtım durduruldu." >&2
    echo "      frontend/.env.production içindeki VITE_API_URL'i kontrol et." >&2
    exit 1
fi

# ─── 4. İmaj ────────────────────────────────────────────────────────────────
echo "▸ [4/7] API imajı kuruluyor…"
$COMPOSE build api

# ─── 5. Göç ─────────────────────────────────────────────────────────────────
# AYRI KONTEYNERDE (`run --rm`), çalışan API'nin içinde DEĞİL. İki sebep:
#   • göç, uygulama ayaktayken koşarsa yeni şemaya eski kod istek karşılar
#   • yeni eklenen bir PostgreSQL eklentisini (ör. citext) çalışan bir API
#     göremez: bağlantı havuzu tip kataloğunu önbelleğe almıştır
#     (bkz. SUNUCUYA-KURULUM §6'daki uyarı)
echo "▸ [5/7] Göç uygulanıyor…"
$COMPOSE run --rm api dotnet PeerLearn.Api.dll --migrate

# ─── 6. Yeniden başlat ──────────────────────────────────────────────────────
echo "▸ [6/7] Servisler yeniden başlatılıyor…"
$COMPOSE up -d

# ─── 7. Doğrulama ───────────────────────────────────────────────────────────
echo "▸ [7/7] Sağlık kontrolü…"
for i in $(seq 1 30); do
    KOD="$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/health/ready" || echo 000)"
    if [ "$KOD" = "200" ]; then
        echo "        /health/ready → 200"
        echo ""
        echo "Dağıtım tamam: $YENI"
        exit 0
    fi
    sleep 2
done

echo "" >&2
echo "HATA: /health/ready 60 saniyede 200 dönmedi (son: $KOD)." >&2
echo "      Günlük: $COMPOSE logs --tail 50 api" >&2
exit 1
