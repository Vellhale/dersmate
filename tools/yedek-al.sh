#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# dersmate — üretim yedeği (Linux sunucu).
#
# tools/yedek-al.ps1'in sunucu karşılığı: o betik PowerShell ve geliştirme
# makinesi için yazıldı; üretim sunucusunda pwsh kurulu olmayabilir.
#
#   ./tools/yedek-al.sh                    → /var/backups/dersmate altına
#   ./tools/yedek-al.sh /mnt/yedek         → başka bir dizine
#   YEDEK_DIZINI=/mnt/yedek ./tools/yedek-al.sh   → ortam değişkeniyle (cron için)
#
# CRON (her gece 03:15) — `cd` GEREKMİYOR, betik kendi kökünü buluyor:
#   15 3 * * * /opt/dersmate/tools/yedek-al.sh >> /var/log/dersmate-yedek.log 2>&1
#
# ⛔ İKİ ŞEY YEDEKLENİYOR, biri unutulmaya çok müsait:
#     1. Veritabanı  — pg_dump
#     2. KANIT DOSYALARI — ders kanıtı görselleri (proof-storage hacmi)
#
#    Kanıt İKİ PARÇALI bir kayıt: satır veritabanında, dosya diskte. Yalnızca
#    birini geri yüklemek, "kanıt var" diyen bir satırla var olmayan bir dosya
#    bırakır — itiraz hakemliğinin dayanağı yedeksiz kalır.
#
# ⚠️ DENENMEMİŞ YEDEK, YEDEK DEĞİLDİR. En az bir kez BOŞ bir veritabanında geri
#    yüklemeyi dene (aşağıdaki "GERİ YÜKLEME" bölümü). Bunu yapmadan yedeğinin
#    olduğunu bilmiyorsun.
#
# ─── BU BETİK İKİ AYRI YAZIMIN BİRLEŞİMİ (2026-09-04) ────────────────────────
# Aynı iş iki kez, birbirinden habersiz yazıldı (main ve ozellik/hesap-silme).
# Ortak ata yoktu. Karşılaştırıldı; her iki taraftan da ölçülebilir şekilde daha
# iyi olan alındı. Alınmayanların gerekçesi ilgili satırın yanında duruyor —
# "neden böyle değil" sorusu bir daha araştırılmasın diye.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# Betiğin kendi konumundan kök: cron satırında `cd` gerekmiyor ve yanlış dizinde
# çalıştırıldığında sessizce boş yedek üretmiyor.
KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# ⛔ VARSAYILAN HEDEF DEPO KÖKÜNÜN DIŞINDA (2026-09-05'te taşındı).
#
# Eskiden `$KOK/yedekler` idi ve tools/dagit.sh bu betiği argümansız çağırıyor —
# yani sunucudaki git çalışma ağacına, HER DAĞITIMDA, veritabanı dökümü (e-posta
# adresleri, parola özetleri, HWID'ler) ve öğrenci belgelerini taşıyan tar dosyası
# düşüyordu. `git add -A` ya da oto-commit hook'u bunları depoya itebilirdi.
#
# ⚠️ Yalnızca .gitignore'a eklemek YETMEZDİ, hatta yeni bir risk açardı:
# `git clean -xdf` yoksayılan dosyaları da siler, yani yedek tek komutla yok
# olabilirdi. Doğru çözüm yedeği git ağacından TAMAMEN çıkarmak.
#
# /var/backups Ubuntu'da zaten var ve sistem yedekleri için ayrılmış standart yer
# (FHS). Farklı bir yol isteyen ya argüman verir ya YEDEK_DIZINI'ni ayarlar.
#
# ⚠️ GÖÇ: eski kurulumlarda yedekler hâlâ /opt/dersmate/yedekler altındadır. Yeni
# hedefe TAŞI, yoksa saklama temizliği eskileri budamaz ve kişisel veri git ağacında
# kalmaya devam eder:  mv /opt/dersmate/yedekler/* /var/backups/dersmate/
HEDEF="${1:-${YEDEK_DIZINI:-/var/backups/dersmate}}"
COMPOSE="docker compose -f $KOK/docker-compose.prod.yml --env-file $KOK/.env.production"
DAMGA="$(date +%F-%H%M)"

# Saklama süresi (gün). Diski dolduran bir yedek klasörü, yedeksiz kalmanın
# başka bir yolu: PostgreSQL yazamaz hâle gelir.
SAKLAMA_GUN="${SAKLAMA_GUN:-14}"

if [ ! -f "$KOK/.env.production" ]; then
    echo "HATA: $KOK/.env.production yok. Ayarlar üretilmemiş (bkz. docs/SUNUCUYA-KURULUM.md §2)." >&2
    exit 1
fi

# ── Kimlik bilgileri compose ile AYNI kaynaktan ──────────────────────────────
# Elle gömülen isim ("-U dersmate dersmate"), sessizce yanlış veritabanını
# yedeklemenin en kolay yolu: compose bu değerleri .env.production'dan alıyor.
#
# `source` yerine `grep` BİLEREK: source, dosyanın içindeki her satırı ÇALIŞTIRIR.
# Yedek betiğinin bir yapılandırma dosyasını yürütmesi gerekmiyor; iki değer
# okumak için kabuk çalıştırmak gereksiz bir yetki.
# ⛔ `|| true` OLMADAN AŞAĞIDAKİ İKİ KORUMA HİÇ ÇALIŞMIYORDU (2026-09-05).
#
# grep aradığını bulamazsa 1 ile çıkar; `set -euo pipefail` altında atama BAŞARISIZ
# sayılır ve betik TAM BU SATIRDA, hiçbir şey yazmadan ölür. Yani hemen altındaki
# `:?` mesajlarına asla ulaşılmıyordu — koruma yazılmıştı ama erişilemezdi.
#
# Ölçüldü:
#   set -euo pipefail; X="$(grep -E '^YOK=' /dev/null)"; : "${X:?YOK bulunamadi}"
#     → çıkış 1, EKRANDA HİÇBİR ŞEY YOK
#   aynısı `|| true` ile
#     → "X: YOK bulunamadi", yani koruma devreye giriyor
#
# Aynı kusur tools/dagit.sh'te bir dağıtımı kaybettirdi: betik sessizce öldü, "sorunsuz
# geçti" sanıldı ve sunucu günlerce eski commit'te kaldı.
POSTGRES_USER="$(grep -E '^POSTGRES_USER=' "$KOK/.env.production" | cut -d= -f2- || true)"
POSTGRES_DB="$(grep -E '^POSTGRES_DB=' "$KOK/.env.production" | cut -d= -f2- || true)"

: "${POSTGRES_USER:?POSTGRES_USER .env.production içinde bulunamadı}"
: "${POSTGRES_DB:?POSTGRES_DB .env.production içinde bulunamadı}"

mkdir -p "$HEDEF"

# ── 1. Veritabanı ────────────────────────────────────────────────────────────
echo "[1/3] Veritabanı yedekleniyor (db: $POSTGRES_DB, kullanıcı: $POSTGRES_USER)…"

# -T: TTY ayırma. Cron'da TTY yok ve onsuz "the input device is not a TTY" ile düşer.
#
# BORU HATTI BİLEREK KORUNDU. Alternatif (konteynerde dosyaya yaz → `compose cp`
# → sıkıştır) denendi ve bırakıldı: dökümün SIKIŞTIRILMAMIŞ hâli konteynerin
# /tmp'inde birikiyor, yani sunucuda geçici olarak iki kat yer istiyor. Disk
# 40 GB ve zaten asgari sınırda.
#
# Borunun klasik tuzağı — pg_dump patlasa bile gzip boş girdiden geçerli bir .gz
# üretip 0 ile çıkar — `set -o pipefail` ile kapalı; boyut kontrolü ikinci hat.
$COMPOSE exec -T db pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" \
    | gzip -9 > "$HEDEF/db-$DAMGA.sql.gz"

BOYUT=$(stat -c%s "$HEDEF/db-$DAMGA.sql.gz")
if [ "$BOYUT" -lt 1000 ]; then
    echo "HATA: veritabanı yedeği şüpheli derecede küçük ($BOYUT bayt). İçeriği kontrol et." >&2
    exit 1
fi

# BÜTÜNLÜK SINAMASI. Boyut kontrolü "dosya var mı" der, "açılıyor mu" demez.
# Bozuk bir yedeğin bozuk olduğunu geri yüklerken öğrenmek, yedek almamaktan
# farksızdır.
gzip -t "$HEDEF/db-$DAMGA.sql.gz"
echo "      db-$DAMGA.sql.gz ($(numfmt --to=iec "$BOYUT")) — sıkıştırma bütün"

# ── 2. Kanıt dosyaları ───────────────────────────────────────────────────────
echo "[2/3] Kanıt dosyaları yedekleniyor…"

# HACİM ADI SABİT ve bu doğru: docker-compose.prod.yml'de `name: dersmate` var,
# yani proje adı klasör adından TÜRETİLMİYOR. Depo başka bir adla klonlansa bile
# hacim `dersmate_proof-storage` kalır.
#
# Alternatif (adı `compose config --format json` ile okumak) denendi ve bırakıldı:
# python3 bağımlılığı getiriyor ve devreye giren yedek yolu
# `$(basename $KOK)_proof-storage` — klasör adı farklıysa YANLIŞ ad üretiyor.
# Yanlış ad hata vermez: `docker run -v <olmayan-hacim>` boş bir hacim yaratır,
# tar başarıyla çalışır ve geriye sıfır dosyalık bir "yedek" kalır.
HACIM="dersmate_proof-storage"

docker run --rm \
    -v "$HACIM":/veri:ro \
    -v "$(realpath "$HEDEF")":/cikti \
    alpine tar czf "/cikti/kanitlar-$DAMGA.tar.gz" -C /veri .

tar tzf "$HEDEF/kanitlar-$DAMGA.tar.gz" >/dev/null   # arşiv bütün mü
GIRDI=$(tar tzf "$HEDEF/kanitlar-$DAMGA.tar.gz" | grep -c . || true)

# ⛔ SIFIR GİRDİ = ÖLÜMCÜL, UYARI DEĞİL (2026-09-05'te uyarıdan hataya yükseltildi).
#
# Hacim adı bir gün değişirse tar yine başarılı olur, arşiv yine geçerlidir —
# yalnızca boştur. Eskiden burada stderr'e bir UYARI basılıp devam ediliyordu ve
# çıkış kodu 0 kalıyordu. Zincir ölçüldü ve şöyle işliyordu:
#
#   1. gece   → 0 girdilik, "geçerli" bir .tar.gz üretilir, betik 0 ile çıkar
#   14. gece  → aşağıdaki saklama temizliği son GERÇEK kanıt yedeğini de siler
#
# Yani sessiz arıza, 14 günde kalıcı veri kaybına dönüşüyordu. Uyarının kimseye
# ulaşmaması da bunun bir parçasıydı: önerilen cron satırı `2>&1` ile stderr'i de
# log dosyasına yönlendiriyor, dolayısıyla cron uyarı postası HİÇ gitmiyor.
#
# `exit 1` iki şeyi birden yapıyor: temizlik hiç çalışmıyor (aşağıdaki adım bu
# satırdan sonra) ve cron gerçekten haber veriyor — sıfırdan farklı çıkış kodu,
# stderr yönlendirilmiş olsa bile cron'un ilgisini çeker.
#
# Veritabanı tarafında bunun karşılığı zaten vardı (1000 baytlık eşik); kanıt
# tarafında yoktu.
if [ "$GIRDI" -eq 0 ]; then
    echo "HATA: kanıt arşivi BOŞ (0 girdi) — yedek GEÇERSİZ." >&2
    echo "      '$HACIM' hacmi var mı? 'docker volume ls' ile bak." >&2
    echo "      Saklama temizliği ÇALIŞTIRILMADI: eldeki geçerli yedekler korundu." >&2
    exit 1
fi
echo "      kanitlar-$DAMGA.tar.gz ($(numfmt --to=iec "$(stat -c%s "$HEDEF/kanitlar-$DAMGA.tar.gz")")) — $GIRDI girdi, arşiv bütün"

# ── 3. Eski yedekleri temizle ────────────────────────────────────────────────
#
# BURAYA ANCAK İKİ ARŞİV DE DOĞRULANMIŞSA GELİNİR. Yol boyunca üç kapı var ve
# üçü de silmeden ÖNCE: veritabanı boyut eşiği (1000 bayt), `gzip -t`/`tar tzf`
# bütünlük sınamaları (`set -e` ile ölümcül) ve yukarıdaki sıfır-girdi kontrolü.
# Sıra bilinçli: bu turun yedeği geçerli değilse eskisini silmek, elde hiç yedek
# bırakmamanın en kısa yoludur.
echo "[3/3] $SAKLAMA_GUN günden eski yedekler siliniyor…"

# AD DESENİNE göre siliniyor, klasöre göre değil. Alternatif (tarihli klasör açıp
# `find -type d -delete`) denendi ve bırakıldı: $HEDEF başka bir diske
# yönlendirilebiliyor (`./tools/yedek-al.sh /mnt/yedek`) ve orası paylaşılan bir
# klasörse ALAKASIZ dizinler silinir. Desen, silme yarıçapını bu betiğin kendi
# ürettiklerine kilitliyor.
find "$HEDEF" -maxdepth 1 -name 'db-*.sql.gz'       -mtime "+$SAKLAMA_GUN" -print -delete
find "$HEDEF" -maxdepth 1 -name 'kanitlar-*.tar.gz' -mtime "+$SAKLAMA_GUN" -print -delete

echo ""
echo "Yedek tamam: $HEDEF"
echo ""
echo "⚠️ Yedek SUNUCUNUN KENDİSİNDE duruyor. Sunucu kaybolursa yedek de kaybolur —"
echo "   düzenli olarak başka bir yere kopyala (rclone, scp, S3…)."
echo "   Geri yükleme adımları bu betiğin sonundaki yorumda."

# ─────────────────────────────────────────────────────────────────────────────
# GERİ YÜKLEME (elle, dikkatle)
#
#   # 1. Uygulamayı durdur — açık bağlantılar restore'u engeller
#   docker compose -f docker-compose.prod.yml --env-file .env.production stop api web
#
#   # 2. Şemayı düşür ve yeniden yarat (aşağıdaki NOT'a bak)
#   docker compose -f docker-compose.prod.yml --env-file .env.production \
#     exec -T db psql -U dersmate -d postgres \
#     -c 'DROP DATABASE dersmate;' -c 'CREATE DATABASE dersmate OWNER dersmate;'
#
#   # 3. Veritabanı
#   #    Kullanıcı adı ve db adı ELLE YAZILMAZ — betiğin kendi kuralı bu
#   #    (bkz. yukarısı: "elle gömülen isim, sessizce yanlış veritabanını
#   #    yedeklemenin en kolay yolu"). Aynısı geri yüklemede de geçerli:
#   YEDEK=/var/backups/dersmate
#   PGUSER="$(grep -E '^POSTGRES_USER=' .env.production | cut -d= -f2-)"
#   PGDB="$(grep -E '^POSTGRES_DB=' .env.production | cut -d= -f2-)"
#
#   gunzip -c "$YEDEK/db-2026-08-29-0315.sql.gz" | \
#     docker compose -f docker-compose.prod.yml --env-file .env.production \
#     exec -T db psql -U "$PGUSER" -d "$PGDB"
#
#   # 4. Kanıt dosyaları
#   #
#   # ⛔ SIRA BİLİNÇLİ: ÖNCE DOĞRULA, SONRA TAŞI, EN SON AÇ. Hiçbir adımda `rm` yok.
#   #
#   # Eskiden burada tek satırda `rm -rf /veri/* && tar xzf …` vardı: yıkım önce,
#   # doğrulama hiç. Boş bir arşivle çalıştırıldığında canlı kanıt deposunu siliyor,
#   # yerine hiçbir şey koymuyor ve `tar` hatasız 0 ile çıkıyordu — tek komutta
#   # kalıcı kanıt kaybı. (`/veri/*` glob'u ayrıca gizli dosyaları atlıyordu, yani
#   # "temiz" geri yükleme aslında temiz değildi.)
#   #
#   # Kanıt iki parçalı bir kayıt ve itiraz hakemliğinin tek dayanağı; geri dönüşü
#   # olmayan bir adımı doğrulamadan atmak burada özellikle pahalı.
#
#   ARSIV=kanitlar-2026-08-29-0315.tar.gz
#
#   # 4a. Arşiv gerçekten dolu mu? (0 ise DUR — geri yükleyecek bir şey yok)
#   tar tzf "$YEDEK/$ARSIV" | grep -c .
#
#   # 4b. Eskisini SİLME, kenara al — geri dönüş noktası kalsın.
#   #
#   # ⚠️ HEDEF HOST'TA OLMAK ZORUNDA. Konteyner içinde bir yola (/veri.eski gibi)
#   # taşımak dosyaları YOK EDER: orası hacim değil konteynerin kendi katmanı ve
#   # `--rm` ile birlikte siliniyor. Yani "kenara alma" adımı sessizce silme adımına
#   # dönüşürdü — kaçınmaya çalıştığımız şeyin ta kendisi.
#   mkdir -p "$YEDEK/kanit-eski"
#   docker run --rm -v dersmate_proof-storage:/veri -v "$YEDEK/kanit-eski":/eski \
#     alpine sh -c 'mv /veri/* /veri/.[!.]* /eski/ 2>/dev/null; true'
#
#   # 4c. Şimdi aç
#   docker run --rm -v dersmate_proof-storage:/veri -v "$YEDEK":/yedek:ro \
#     alpine tar xzf "/yedek/$ARSIV" -C /veri
#
#   # 4d. Sayılar tutuyor mu? 4a'daki girdi sayısıyla karşılaştır
#   docker run --rm -v dersmate_proof-storage:/veri alpine \
#     sh -c 'find /veri -type f | wc -l'
#
#   # 4e. ANCAK tuttuysa eskiyi at (kişisel veri — açıkta bırakma):
#   #   rm -rf "$YEDEK/kanit-eski"
#
#   # 5. Başlat
#   docker compose -f docker-compose.prod.yml --env-file .env.production start api web
#
# NOT — DÖKÜM NEDEN `--clean` İÇERMİYOR:
# pg_dump çıktısı CREATE TABLE içeriyor ama DROP içermiyor, yani dolu bir
# veritabanına geri yüklemek "already exists" hatalarıyla yarım kalır. Bu
# BİLEREK böyle. `--clean --if-exists` eklemek dökümü tek başına çalışan bir
# silme aracına çevirirdi: yedek klasöründeki her .sql.gz, yanlışlıkla
# çalıştırıldığında üretimi silen bir dosya olurdu. Yıkım, 2. adımdaki gibi
# AÇIKÇA yazılmalı — yedeğin içine gizlenmemeli.
# ─────────────────────────────────────────────────────────────────────────────
