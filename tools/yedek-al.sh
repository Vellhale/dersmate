#!/usr/bin/env bash
#
# dersmate yedegi (uretim sunucusu): PostgreSQL dokumu + kanit dosyalari.
#
# NEDEN AYRI BIR BETIK: tools/yedek-al.ps1 Windows icindir; uretim Ubuntu + Docker.
# Belgede duran tek satirlik komut ise iki sekilde sessizce yaniltiyordu:
#
#   1. Kullanici ve veritabani adi elle gomuluydu ("-U dersmate dersmate"), oysa
#      docker-compose.prod.yml bunlari .env.production'daki POSTGRES_USER /
#      POSTGRES_DB degiskenlerinden aliyor. Isimler tutmazsa pg_dump hata verir.
#   2. "pg_dump ... | gzip > dosya" kaliplarinda kabuk, BORUNUN SON komutunun cikis
#      kodunu dondurur. pg_dump patlasa bile gzip bos girdiden gecerli bir .gz uretir
#      ve 0 ile ciker: geriye yedek sanilan ~20 baytlik bir dosya kalir. Bu betik
#      "set -o pipefail" kullanir ve ayrica boyutu dogrular.
#
# KULLANIM
#   ./tools/yedek-al.sh [hedef-klasor]        (varsayilan: ./yedekler)
#
# CRON (her gece 03:15, cikti gunluge)
#   15 3 * * * cd /opt/dersmate && ./tools/yedek-al.sh >> /var/log/dersmate-yedek.log 2>&1

set -euo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HEDEF="${1:-$KOK/yedekler}"
COMPOSE="docker compose -f $KOK/docker-compose.prod.yml"
SAKLANACAK_GUN="${SAKLANACAK_GUN:-14}"

# Kimlik bilgileri compose ile AYNI kaynaktan: elle gomulen isim, sessizce yanlis
# veritabanini yedeklemenin en kolay yoludur.
if [[ -f "$KOK/.env.production" ]]; then
  # shellcheck disable=SC1091
  set -a; source "$KOK/.env.production"; set +a
fi
: "${POSTGRES_USER:?POSTGRES_USER tanimsiz — .env.production okunamadi}"
: "${POSTGRES_DB:?POSTGRES_DB tanimsiz — .env.production okunamadi}"

DAMGA="$(date +%Y-%m-%d_%H%M%S)"
KLASOR="$HEDEF/$DAMGA"
mkdir -p "$KLASOR"

echo "dersmate yedegi -> $KLASOR"

# --- 1. Veritabani -----------------------------------------------------------
echo "[1/3] Veritabani dokumu (db: $POSTGRES_DB, kullanici: $POSTGRES_USER)..."

# Dokum konteyner ICINDE dosyaya yaziliyor; boru yok, kabuk yeniden kodlamiyor.
$COMPOSE exec -T db \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists -f /tmp/dersmate-dokum.sql
$COMPOSE cp db:/tmp/dersmate-dokum.sql "$KLASOR/veritabani.sql"
$COMPOSE exec -T db rm -f /tmp/dersmate-dokum.sql

DOKUM_BOYUT=$(stat -c%s "$KLASOR/veritabani.sql")
if (( DOKUM_BOYUT < 1024 )); then
  echo "HATA: dokum yalnizca $DOKUM_BOYUT bayt. Baglanti ya da yetki sorunu; yedek gecersiz." >&2
  exit 1
fi

gzip -9 "$KLASOR/veritabani.sql"
gzip -t "$KLASOR/veritabani.sql.gz"   # sikistirma butun mu
echo "      tamam - $(stat -c%s "$KLASOR/veritabani.sql.gz") bayt (sikistirilmis)"

# --- 2. Kanit dosyalari ------------------------------------------------------
# AYRI ALINMALARI ANLAMSIZ: kanit iki parcali bir kayit — satir veritabaninda,
# dosya diskte. Yalnizca birini geri yuklemek "kanit var" diyen bir satirla var
# olmayan bir dosya birakir.
echo "[2/3] Kanit dosyalari (docker volume)..."

HACIM="$($COMPOSE config --format json 2>/dev/null \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["volumes"]["proof-storage"]["name"])' 2>/dev/null || true)"
if [[ -z "$HACIM" ]]; then
  # Yedek yol: compose proje adi + hacim adi.
  HACIM="$(basename "$KOK")_proof-storage"
  echo "      (hacim adi compose'dan okunamadi, tahmin: $HACIM)"
fi

docker run --rm -v "$HACIM":/veri:ro -v "$KLASOR":/cikti alpine \
  tar czf /cikti/kanitlar.tar.gz -C /veri .
tar tzf "$KLASOR/kanitlar.tar.gz" >/dev/null   # arsiv butun mu
echo "      tamam - $(tar tzf "$KLASOR/kanitlar.tar.gz" | grep -c . ) girdi"

# --- 3. Eski yedekleri temizle ----------------------------------------------
echo "[3/3] $SAKLANACAK_GUN gunden eski yedekler siliniyor..."
find "$HEDEF" -mindepth 1 -maxdepth 1 -type d -mtime +"$SAKLANACAK_GUN" -exec rm -rf {} + 2>/dev/null || true

echo
echo "Yedek hazir: $KLASOR"
cat <<NOT

Geri yukleme (dokum DROP iceriyor, mevcut semayi siler):
  gunzip -c "$KLASOR/veritabani.sql.gz" \
    | $COMPOSE exec -T db psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"

  docker run --rm -v "$HACIM":/veri -v "$KLASOR":/girdi alpine \
    sh -c 'rm -rf /veri/* && tar xzf /girdi/kanitlar.tar.gz -C /veri'

Denenmemis yedek, yedek degildir: geri yuklemeyi en az bir kez BOS bir
veritabaninda deneyin. Bunu yapmadan yedeginizin oldugunu bilmiyorsunuz.
NOT
