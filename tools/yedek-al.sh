#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# dersmate — üretim yedeği (Linux sunucu).
#
# tools/yedek-al.ps1'in sunucu karşılığı: o betik PowerShell ve geliştirme
# makinesi için yazıldı; üretim sunucusunda pwsh kurulu olmayabilir.
#
#   ./tools/yedek-al.sh                    → ./yedekler altına
#   ./tools/yedek-al.sh /mnt/yedek         → başka bir dizine
#
# CRON (her gece 03:15):
#   15 3 * * * cd /home/KULLANICI/dersmate && ./tools/yedek-al.sh >> yedek.log 2>&1
#
# ⛔ İKİ ŞEY YEDEKLENİYOR, biri unutulmaya çok müsait:
#     1. Veritabanı  — pg_dump
#     2. KANIT DOSYALARI — ders kanıtı görselleri (proof-storage hacmi)
#
#    Yalnızca veritabanını almak, itiraz hakemliğinin dayanağını yedeksiz
#    bırakır: defterde "kanıt yüklendi" yazar ama görsel yoktur.
#
# ⚠️ DENENMEMİŞ YEDEK, YEDEK DEĞİLDİR. En az bir kez geri yüklemeyi dene
#    (aşağıdaki "GERİ YÜKLEME" bölümü).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

HEDEF="${1:-./yedekler}"
COMPOSE="docker compose -f docker-compose.prod.yml --env-file .env.production"
DAMGA="$(date +%F-%H%M)"

# Saklama süresi (gün). Diski dolduran bir yedek klasörü, yedeksiz kalmanın
# başka bir yolu: PostgreSQL yazamaz hâle gelir.
SAKLAMA_GUN="${SAKLAMA_GUN:-14}"

if [ ! -f .env.production ]; then
    echo "HATA: .env.production bulunamadı. Depo kökünde misin?" >&2
    exit 1
fi

# shellcheck disable=SC1091
POSTGRES_USER="$(grep -E '^POSTGRES_USER=' .env.production | cut -d= -f2-)"
POSTGRES_DB="$(grep -E '^POSTGRES_DB=' .env.production | cut -d= -f2-)"

mkdir -p "$HEDEF"

echo "[1/3] Veritabanı yedekleniyor…"
# -T: TTY ayırma. Cron'da TTY yok ve onsuz "the input device is not a TTY" ile düşer.
$COMPOSE exec -T db pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" \
    | gzip > "$HEDEF/db-$DAMGA.sql.gz"

# BOŞ DOSYA KONTROLÜ: pg_dump hata verse bile gzip 20 baytlık geçerli bir dosya
# üretir ve yedek "alınmış" görünür. Sessiz başarısızlığın klasik yolu.
BOYUT=$(stat -c%s "$HEDEF/db-$DAMGA.sql.gz")
if [ "$BOYUT" -lt 1000 ]; then
    echo "HATA: veritabanı yedeği şüpheli derecede küçük ($BOYUT bayt). İçeriği kontrol et." >&2
    exit 1
fi
echo "      db-$DAMGA.sql.gz ($(numfmt --to=iec "$BOYUT"))"

echo "[2/3] Kanıt dosyaları yedekleniyor…"
docker run --rm \
    -v dersmate_proof-storage:/veri:ro \
    -v "$(realpath "$HEDEF")":/cikti \
    alpine tar czf "/cikti/kanitlar-$DAMGA.tar.gz" -C /veri .
echo "      kanitlar-$DAMGA.tar.gz ($(numfmt --to=iec "$(stat -c%s "$HEDEF/kanitlar-$DAMGA.tar.gz")"))"

echo "[3/3] $SAKLAMA_GUN günden eski yedekler siliniyor…"
find "$HEDEF" -name 'db-*.sql.gz'       -mtime "+$SAKLAMA_GUN" -print -delete
find "$HEDEF" -name 'kanitlar-*.tar.gz' -mtime "+$SAKLAMA_GUN" -print -delete

echo ""
echo "Yedek tamam: $HEDEF"
echo ""
echo "⚠️ Yedek SUNUCUNUN KENDİSİNDE duruyor. Sunucu kaybolursa yedek de kaybolur —"
echo "   düzenli olarak başka bir yere kopyala (rclone, scp, S3…)."

# ─────────────────────────────────────────────────────────────────────────────
# GERİ YÜKLEME (elle, dikkatle)
#
#   # 1. Uygulamayı durdur — açık bağlantılar restore'u engeller
#   docker compose -f docker-compose.prod.yml --env-file .env.production stop api web
#
#   # 2. Veritabanı
#   gunzip -c yedekler/db-2026-08-29-0315.sql.gz | \
#     docker compose -f docker-compose.prod.yml --env-file .env.production \
#     exec -T db psql -U dersmate -d dersmate
#
#   # 3. Kanıt dosyaları
#   docker run --rm -v dersmate_proof-storage:/veri -v "$PWD/yedekler":/yedek \
#     alpine tar xzf /yedek/kanitlar-2026-08-29-0315.tar.gz -C /veri
#
#   # 4. Başlat
#   docker compose -f docker-compose.prod.yml --env-file .env.production start api web
#
# NOT: pg_dump çıktısı CREATE TABLE içeriyor ama DROP içermiyor. Dolu bir
# veritabanına geri yüklemek "already exists" hatalarıyla yarım kalır. Temiz
# geri yükleme için önce veritabanını düşür/yeniden yarat.
# ─────────────────────────────────────────────────────────────────────────────
