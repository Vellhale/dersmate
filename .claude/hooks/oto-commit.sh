#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Claude Code "Stop" hook — oturum değişikliklerini YERELDE commit'ler.
#
# Kurulum: .claude/settings.local.json → hooks.Stop bu betiği çağırır.
#
# NE YAPAR : her turun sonunda çalışma ağacındaki değişiklikleri tek commit'e alır.
# NE YAPMAZ: push. GitHub'a gönderme kararı sende — 'git push' ile elle yap.
#
# Bilerek konulmuş kapılar. Hiçbiri turu bloklamaz; koşul sağlanmazsa sessizce
# ya da tek satırlık bir uyarıyla atlar:
#   - depo dışındaysa, HEAD detached'sa, rebase/merge yarım kalmışsa: dokunmaz
#   - değişiklik yoksa: commit atmaz (sohbet turları tarihçeyi kirletmesin)
#   - 10 MB üstü bir dosya varsa: HİÇ commit atmaz, uyarır. 'git add -A'
#     yanlışlıkla veritabanı dökümü / derleme çıktısı içeri almasın diye.
#
# NOT: JSON kaçırma bilerek saf bash genişletmesiyle yapılıyor, sed ile değil.
# Betik metni kabuk katmanlarından geçerken ters bölüler yarıya iniyor ve
# sed ifadesi sessizce bozuluyordu (mesaj boş çıkıyordu). Burada ters bölü yok.
# ---------------------------------------------------------------------------
set -uo pipefail

AZAMI_BAYT=$((10 * 1024 * 1024))   # tek dosya üst sınırı: 10 MB
OZET_SINIRI=30                     # commit gövdesinde listelenecek azami dosya

# --- JSON çıktı yardımcıları (jq bu makinede yok) ---------------------------
json_kacir() {
  local m=$1
  m=${m//\\/\\\\}      # ters bölü
  m=${m//\"/\\\"}      # çift tırnak
  m=${m//$'\n'/\\n}    # satır sonu
  m=${m//$'\r'/}       # satır başı (CRLF artığı)
  m=${m//$'\t'/\\t}    # sekme
  printf '%s' "$m"
}
bildir() {   # kullanıcıya bilgi göster ve turu BLOKLAMADAN çık
  printf '{"systemMessage":"%s","suppressOutput":true}\n' "$(json_kacir "$1")"
  exit 0
}
sessiz_cik() { exit 0; }

# --- Depo kökü: betiğin kendi konumundan türet (<kök>/.claude/hooks/) -------
kendi_dizin=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd) || sessiz_cik
kok=$(cd "$kendi_dizin/../.." && pwd) || sessiz_cik
cd "$kok" || sessiz_cik

git rev-parse --is-inside-work-tree >/dev/null 2>&1 || sessiz_cik

# --- Yarım kalmış git işlemine karışma -------------------------------------
gitdizin=$(git rev-parse --git-dir 2>/dev/null) || sessiz_cik
for isaret in rebase-merge rebase-apply MERGE_HEAD CHERRY_PICK_HEAD REVERT_HEAD BISECT_LOG; do
  if [ -e "$gitdizin/$isaret" ]; then
    bildir "Oto-commit atlandı: yarım kalmış git işlemi var ($isaret). Önce onu bitir."
  fi
done

# --- Detached HEAD'de commit atma ------------------------------------------
dal=$(git symbolic-ref --quiet --short HEAD 2>/dev/null) \
  || bildir "Oto-commit atlandı: HEAD bir dala bağlı değil (detached)."

# --- Değişiklik yoksa çık ---------------------------------------------------
[ -n "$(git status --porcelain)" ] || sessiz_cik

# --- Büyük dosya kapısı -----------------------------------------------------
# Commit'e girecek adaylar: izlenmeyen (yoksayılmayanlar) + değişmiş dosyalar.
buyuk=""
while IFS= read -r -d '' yol; do
  [ -f "$yol" ] || continue
  boyut=$(wc -c < "$yol" 2>/dev/null) || continue
  if [ "$boyut" -gt "$AZAMI_BAYT" ]; then
    buyuk="$yol (~$((boyut / 1024 / 1024)) MB)"
    break
  fi
done < <(git ls-files -z --others --modified --exclude-standard)

if [ -n "$buyuk" ]; then
  bildir "Oto-commit atlandı: 10 MB üstü dosya var -> $buyuk . Bilerekse .gitignore'a ekle, sonra elle commit'le."
fi

# --- Sahnele ve commit'le ---------------------------------------------------
git add -A || bildir "Oto-commit başarısız: 'git add -A' hata verdi."
git diff --cached --quiet && sessiz_cik   # hepsi yoksayıldıysa commit atma

sayi=$(git diff --cached --name-only | wc -l | tr -d '[:space:]')
damga=$(date '+%Y-%m-%d %H:%M')
ozet=$(git diff --cached --name-status | head -n "$OZET_SINIRI")
if [ "$sayi" -gt "$OZET_SINIRI" ]; then
  ozet="$ozet
... ve $((sayi - OZET_SINIRI)) dosya daha"
fi

if git commit -q -F - <<MSG
oto: $sayi dosya güncellendi — $damga

Claude Code oturumunda yapılan değişiklikler, tur sonunda Stop hook
tarafından otomatik commit'lendi. Push YAPILMADI; GitHub'a göndermek
için 'git push' çalıştır.

Değişen dosyalar:
$ozet

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
MSG
then
  bildir "Oto-commit: $sayi dosya '$dal' dalına commit'lendi ($(git rev-parse --short HEAD)). Push yapılmadı."
else
  bildir "Oto-commit BAŞARISIZ: 'git commit' hata verdi. Değişiklikler sahnelenmiş (staged) hâlde duruyor, kaybolmadı."
fi
