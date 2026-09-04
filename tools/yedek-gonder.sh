#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# dersmate — yedekleri ŞİFRELİ olarak sunucu dışına gönder.
#
#   ./tools/yedek-gonder.sh                → varsayılan uzak: dersmate-sifreli:
#   YEDEK_UZAK=baska: ./tools/yedek-gonder.sh
#
# CRON — yedek-al.sh'in ARDINDAN, `&&` ile:
#   15 3 * * * /root/dersmate/tools/yedek-al.sh && /root/dersmate/tools/yedek-gonder.sh >> /var/log/dersmate-yedek.log 2>&1
#
# `&&` BİLİNÇLİ: yedek alma başarısızsa (0 girdili kanıt arşivi, küçük döküm…)
# gönderim HİÇ çalışmamalı. Bozuk bir yedeği buluta taşımak, buluttaki sağlam
# kopyanın yerini almaz ama saklama penceresini boşa harcar.
#
# ⛔ NEDEN VAR: yedek SUNUCUNUN KENDİSİNDE duruyordu. Sunucu kaybolursa (disk arızası,
#    sağlayıcı hesabının kapanması, fidye yazılımı) yedek de kaybolur — yani aslında
#    yedek yoktu, kopyası vardı. Bu adım bir dönem sunucuda ELLE kurulmuştu ve depoda
#    hiçbir izi yoktu; sunucu yeniden kurulduğunda ya da işi başkası devraldığında
#    sessizce yok olacaktı. Artık kod.
#
# ─── ŞİFRELEME: PAROLAYI KAYBEDERSEN YEDEK DE GİDER ──────────────────────────
#
# Yedeklerde KİŞİSEL VERİ var: e-posta adresleri, parola özetleri, HWID'ler ve
# öğrencilerin yüklediği kimlik/öğrenci belgeleri. Bunları şifresiz bir bulut
# hesabında tutmak KVKK açısından savunulamaz.
#
# Şifreleme rclone'un `crypt` uzağıyla yapılıyor: dosya adları ve içerik yerelde
# şifrelenip öyle yükleniyor, bulut sağlayıcısı okunur hiçbir şey görmüyor.
#
# ⚠️ BEDELİ ŞU: crypt parolası KAYBOLURSA yedekler KURTARILAMAZ. Kimse
#    çözemez — bu şifrelemenin amacı zaten bu. Bu yüzden:
#
#      • Parolayı SUNUCUDA DEĞİL, bir parola yöneticisinde sakla. Sunucu
#        kaybolduğunda yedeğe ihtiyacın olacak ve parola sunucudaysa o da gitmiş olur.
#      • En az İKİ kişide bulunsun. Tek kişideyse, o kişi ulaşılamadığında
#        şirketin bütün yedekleri ölü veridir.
#      • Kurulumdan sonra GERİ YÜKLEMEYİ DENE (docs/SUNUCUYA-KURULUM.md §10).
#        Denenmemiş bir şifreli yedek, iki kat denenmemiş yedektir.
#
# Kurulum adımları ve `rclone config` yordamı: docs/SUNUCUYA-KURULUM.md §10.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

KAYNAK="${1:-${YEDEK_DIZINI:-/var/backups/dersmate}}"
UZAK="${YEDEK_UZAK:-dersmate-sifreli:}"

# Buluttaki saklama süresi (gün). Sunucudakinden (14) UZUN olması bilinçli: bulut
# kopyası felaket kopyasıdır ve sunucunun günlerce fark edilmeyen bir arızasında
# geriye daha çok gidebilmek gerekir.
#
# ⚠️ AMA SINIRSIZ DEĞİL. Sınırsız saklama, silinmiş hesapların kimlik belgelerinin
# bulutta süresiz durması demekti — gizlilik metnindeki silme vaadiyle çelişir.
UZAK_SAKLAMA_GUN="${UZAK_SAKLAMA_GUN:-30}"

# ── Ön kontroller ────────────────────────────────────────────────────────────
if ! command -v rclone >/dev/null 2>&1; then
    echo "HATA: rclone kurulu değil. Kurulum: docs/SUNUCUYA-KURULUM.md §10" >&2
    exit 1
fi

if [ ! -d "$KAYNAK" ]; then
    echo "HATA: yedek dizini yok: $KAYNAK" >&2
    echo "      Önce ./tools/yedek-al.sh çalıştır." >&2
    exit 1
fi

UZAK_AD="${UZAK%%:*}"

# ⛔ HEDEFİN GERÇEKTEN ŞİFRELİ OLDUĞUNU DOĞRULA.
#
# Bu kontrol olmadan, uzak adı yanlış yazmak ya da crypt yerine düz bir Drive uzağı
# vermek KİŞİSEL VERİYİ ŞİFRESİZ YÜKLERDİ — ve hiçbir hata vermezdi, çünkü rclone
# açısından ikisi de geçerli bir hedef. Sessizce yanlış çalışan bir güvenlik önlemi,
# hiç olmayandan kötüdür: var sanılır.
TIP="$(rclone config show "$UZAK_AD" 2>/dev/null | grep -E '^type *=' | head -1 | sed 's/.*= *//' || true)"

if [ -z "$TIP" ]; then
    echo "HATA: '$UZAK_AD' diye bir rclone uzağı yok." >&2
    echo "      Tanımlı uzaklar: $(rclone listremotes 2>/dev/null | tr '\n' ' ')" >&2
    echo "      Kurulum: docs/SUNUCUYA-KURULUM.md §10" >&2
    exit 1
fi

if [ "$TIP" != "crypt" ]; then
    echo "HATA: '$UZAK_AD' uzağının tipi '$TIP' — 'crypt' olmalı." >&2
    echo "      Bu hedefe yükleme, kimlik belgelerini ŞİFRESİZ buluta koyardı." >&2
    echo "      Gönderim yapılmadı." >&2
    exit 1
fi

echo "[1/3] Yükleniyor: $KAYNAK → $UZAK (şifreli)"

# ── 1. Yükle ─────────────────────────────────────────────────────────────────
#
# `copy`, `sync` DEĞİL — ve bu farkı bir kez daha yazmaya değer:
#
#   sync  uzağı yerele BİREBİR yansıtır. Yereldeki bir silme (fidye yazılımı, yanlış
#         komut, yedek betiğinin 0 girdiyle çalışması) ilk gece uzağa da yayılır ve
#         TEK yedeğini kaybedersin. Yedeğin varlık sebebi tam olarak bu senaryodur.
#   copy  yalnızca ekler. Uzaktaki eski dosyalar yerelde silinse bile durur.
#
# Saklama, aşağıda AYRI ve YAŞA GÖRE yapılıyor: silme yarıçapı "yerelde yok" değil,
# "belirlenmiş süreden eski".
rclone copy "$KAYNAK" "$UZAK" \
    --include 'db-*.sql.gz' \
    --include 'kanitlar-*.tar.gz' \
    --transfers 2 \
    --stats-one-line \
    --stats 30s

# ── 2. Gerçekten gitti mi ────────────────────────────────────────────────────
#
# ⚠️ `rclone copy` HİÇBİR ŞEY YÜKLEMESE DE 0 İLE ÇIKAR. Süzgeç bir gün tutmazsa
# ya da kaynak boşalırsa, cron her gece "başarılı" bir gönderim raporlar ve buluttaki
# yedek sessizce eskir. Bu betiğin kardeşi yedek-al.sh aynı sınıftan bir arızayı
# (0 girdili arşiv) yaşadı; oradan alınan ders burada da uygulanıyor.
UZAKTAKI="$(rclone lsf "$UZAK" 2>/dev/null | grep -cE '^(db-.*\.sql\.gz|kanitlar-.*\.tar\.gz)$' || true)"

if [ "$UZAKTAKI" -eq 0 ]; then
    echo "HATA: uzakta hiç yedek dosyası yok — gönderim gerçekleşmemiş." >&2
    echo "      Saklama temizliği ÇALIŞTIRILMADI." >&2
    exit 1
fi
echo "      uzakta $UZAKTAKI dosya"

# En taze dosya gerçekten BUGÜNden mi? Dosya sayısı doğru olsa bile hepsi eski
# olabilir; o durumda gönderim aylardır kırıktır ve kimse fark etmemiştir.
EN_TAZE="$(rclone lsl "$UZAK" 2>/dev/null | sort -k2,3 | tail -1 | awk '{print $2}' || true)"
echo "      en taze dosya: ${EN_TAZE:-bilinmiyor}"

# ── 3. Uzaktaki eskileri buda ────────────────────────────────────────────────
echo "[2/3] $UZAK_SAKLAMA_GUN günden eski uzak yedekler siliniyor…"

# YAŞA GÖRE ve YALNIZCA KENDİ ÜRETTİĞİMİZ ADLARA. Süzgeçsiz bir `delete`, uzak
# klasör paylaşılıyorsa alakasız dosyaları da silerdi.
rclone delete "$UZAK" \
    --min-age "${UZAK_SAKLAMA_GUN}d" \
    --include 'db-*.sql.gz' \
    --include 'kanitlar-*.tar.gz'

KALAN="$(rclone lsf "$UZAK" 2>/dev/null | grep -cE '^(db-.*\.sql\.gz|kanitlar-.*\.tar\.gz)$' || true)"

echo "[3/3] Tamam. Uzakta $KALAN dosya kaldı."
echo ""
echo "⚠️ Şifreleme parolası olmadan bu dosyalar KURTARILAMAZ."
echo "   Parola sunucuda DEĞİL, parola yöneticisinde ve en az iki kişide durmalı."
echo "   Geri yükleme yordamı: docs/SUNUCUYA-KURULUM.md §10."
