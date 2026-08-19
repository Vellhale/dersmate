#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Claude Code "Stop" hook — oturum değişikliklerini commit'ler ve GitHub'a gönderir.
#
# Kurulum: .claude/settings.local.json → hooks.Stop bu betiği çağırır.
#
# AKIŞ: her turun sonunda
#   1. ana dal ('main') üzerindeysen -> 'claude/oturum-<tarih>' dalına geçer
#   2. çalışma ağacındaki değişiklikleri tek commit'e alır
#   3. o dalı 'origin'e gönderir
#
# ANA DALA ASLA OTOMATİK COMMIT/GÖNDERİM YAPILMAZ. CLAUDE.md kuralı: "Ana dal
# main. Doğrudan itmek yerine dal aç ve PR aç." Oturum dalı bu kuralı korurken
# değişikliklerin makine dışında yedeklenmesini de sağlıyor. İş bitince PR aç ve
# squash'la; 'oto:' commit'leri main'e hiç sızmaz.
#
# Bilerek konulmuş kapılar. Hiçbiri turu bloklamaz; koşul sağlanmazsa sessizce
# ya da tek satırlık bir uyarıyla atlar:
#   - depo dışındaysa, HEAD detached'sa, rebase/merge yarım kalmışsa: dokunmaz
#   - değişiklik yoksa: commit atmaz (sohbet turları tarihçeyi kirletmesin)
#   - 10 MB üstü bir dosya varsa: HİÇ commit atmaz, uyarır. 'git add -A'
#     yanlışlıkla veritabanı dökümü / derleme çıktısı içeri almasın diye.
#   - gönderim takılırsa: commit yerelde durur, tur bloklanmaz. Kimlik doğrulama
#     penceresi açılıp oturumu kilitlemesin diye istem kapalı + zaman aşımlı.
#
# NOT: JSON kaçırma bilerek saf bash genişletmesiyle yapılıyor, sed ile değil.
# Betik metni kabuk katmanlarından geçerken ters bölüler yarıya iniyor ve
# sed ifadesi sessizce bozuluyordu (mesaj boş çıkıyordu). Burada ters bölü yok.
# ---------------------------------------------------------------------------
set -uo pipefail

AZAMI_BAYT=$((10 * 1024 * 1024))   # tek dosya üst sınırı: 10 MB
OZET_SINIRI=30                     # commit gövdesinde listelenecek azami dosya
AZAMI_DAL_DENEMESI=20              # oturum dalı adı çakışırsa kaç kez soneklensin
PUSH_ZAMAN_ASIMI=45                # uzağa gönderim için azami bekleme (saniye)

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

# --- Ana dalı tespit et -----------------------------------------------------
ana_dal=$(git symbolic-ref --quiet --short refs/remotes/origin/HEAD 2>/dev/null)
ana_dal=${ana_dal#origin/}
[ -n "$ana_dal" ] || ana_dal=main

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

# --- Ana daldaysan oturum dalına geç ---------------------------------------
# Yeni dal HEAD'den açılır: çalışma ağacı olduğu gibi taşınır, birleştirme yok.
# Ad çakışırsa önce mevcut dala geçmeyi dener (yalnızca HEAD'in soyundansa,
# yani ileri doğru bir hareketse), olmazsa -2, -3... sonekiyle yenisini açar.
# Bu merdiven daima bir yerde biter; hiçbir dalın üstüne yazmaz.
hedef_dal=$dal
if [ "$dal" = "$ana_dal" ]; then
  taban="claude/oturum-$(date '+%Y-%m-%d')"
  aday=$taban
  ek=2
  gecildi=0
  while [ "$ek" -le "$AZAMI_DAL_DENEMESI" ]; do
    if git show-ref --verify --quiet "refs/heads/$aday"; then
      if git merge-base --is-ancestor HEAD "$aday" 2>/dev/null \
         && git checkout -q "$aday" 2>/dev/null; then
        gecildi=1; break
      fi
    elif git checkout -q -b "$aday" 2>/dev/null; then
      gecildi=1; break
    fi
    aday="$taban-$ek"
    ek=$((ek + 1))
  done
  if [ "$gecildi" -ne 1 ]; then
    bildir "Oto-commit atlandı: oturum dalı açılamadı. '$ana_dal' dalına otomatik commit atılmıyor (CLAUDE.md kuralı). Elle bir dal aç."
  fi
  hedef_dal=$aday
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
tarafından otomatik commit'lendi. Bu bir oturum dalıdır; '$ana_dal'
dalına PR ile ve squash'lanarak girmesi beklenir.

Değişen dosyalar:
$ozet

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
MSG
then
  kisa=$(git rev-parse --short HEAD)
else
  bildir "Oto-commit BAŞARISIZ: 'git commit' hata verdi. Değişiklikler sahnelenmiş (staged) hâlde duruyor, kaybolmadı."
fi

# --- Uzağa gönderim: yalnızca oturum/özellik dalı, ASLA ana dal -------------
# Ana dala gönderim CLAUDE.md kuralına aykırı; buraya düşülüyorsa dal değiştirme
# merdiveni çalışmamış demektir, sessizce ana dala itmektense commit'te bırak.
if [ "$hedef_dal" = "$ana_dal" ]; then
  bildir "Oto-commit: $sayi dosya '$hedef_dal' dalına commit'lendi ($kisa). Uzağa gönderilmedi — ana dala otomatik gönderim yapılmaz."
fi

# Kimlik doğrulama istemi kapalı + zaman aşımlı: takılan bir kimlik penceresi
# oturumu kilitlemesin. Gönderim başarısız olsa bile commit yerelde duruyor.
if command -v timeout >/dev/null 2>&1; then
  zaman_asimi="timeout $PUSH_ZAMAN_ASIMI"
else
  zaman_asimi=""
fi

gonderim_cikti=$(GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never $zaman_asimi \
  git -c credential.interactive=false push -q -u origin "$hedef_dal" 2>&1)
gonderim_kod=$?

if [ "$gonderim_kod" -eq 0 ]; then
  bildir "Oto-commit: $sayi dosya '$hedef_dal' dalına commit'lendi ($kisa) ve GitHub'a gönderildi."
elif [ "$gonderim_kod" -eq 124 ]; then
  bildir "Oto-commit: $sayi dosya commit'lendi ($kisa) ama gönderim ${PUSH_ZAMAN_ASIMI}sn'de yanıt vermedi (kimlik doğrulama bekliyor olabilir). Elle: git push -u origin $hedef_dal"
else
  ilk_satir=$(printf '%s' "$gonderim_cikti" | head -n 1 | cut -c1-160)
  bildir "Oto-commit: $sayi dosya commit'lendi ($kisa) ama GÖNDERİM BAŞARISIZ: $ilk_satir | Elle: git push -u origin $hedef_dal"
fi
