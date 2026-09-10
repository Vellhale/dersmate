# PeerLearn — profil arkadaş bölümü testi
#
# ARKADAŞ = KABUL EDİLMİŞ EŞLEŞME (matchmaking."Matches", Status='Accepted').
# Ayrı bir tablo yok; bu paket o tanımın üç yüzeyde de aynı şekilde uygulandığını sınıyor:
#   • arkadaş SAYISI      — herkesin profilinde
#   • TAM LİSTE           — yalnızca kendi profilinde
#   • ORTAK ARKADAŞLAR    — başkasının profilinde
#
# ─── EN KRİTİK İDDİA: TEKİLLEŞTİRME ──────────────────────────────────────────
# Aynı çift arasında BİRDEN FAZLA 'Accepted' satırı olabilir ve bu bir bozukluk değil,
# tasarımın sonucu: tekillik indeksleri yalnızca Status='Pending' üzerinde ve yön+konu
# bazlı. Yani A→B "Matematik" kabul + A→B "Fizik" kabul + B→A konusuz kabul = aynı çift
# için ÜÇ satır. Ham bir COUNT o kişiyi ÜÇ ARKADAŞ sayardı.
#
# BU HATA SESSİZDİR: tek eşleşmeli test kullanıcılarıyla bakılırsa HİÇ görünmez. Bu
# yüzden C bölümü aynı çifte ikinci bir kabul satırı YAZIYOR ve sayının değişmediğini
# sınıyor — kanıt standardı mutasyondur (CLAUDE.md).
#
# ─── SAYI = LİSTE (KÂHİN KONTROLÜ) ───────────────────────────────────────────
# Sayı ile listenin ayrışması yalnızca çirkin değil, TEHLİKELİ: "Ortak arkadaşlar (3)"
# yazıp 2 kart göstermek, izleyiciye "seninle bu kişi arasında gizlenmiş biri var" der,
# yani engel ilişkisinin varlığını üçüncü bir kişinin profilinde ilan eder. Nötr hata
# mesajlarıyla korunan gizlilik tek hamlede yok olurdu. Her bölümde ayrıca sınanıyor.

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

if (-not (Test-Path $PSQL)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $PSQL = $bulunan } else { $PSQL = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'

$script:failures = 0
function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Fail($msg)  { Write-Host "  [KALDI] $msg" -ForegroundColor Red; $script:failures++ }
function Esit($etiket, $gelen, $beklenen) {
    if ("$gelen" -eq "$beklenen") { OK $etiket } else { Fail "$etiket — beklenen '$beklenen', gelen '$gelen'" }
}

function Api {
    param($Method, $Path, $Body, $Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$API$Path"; Method = $Method; Headers = $headers; TimeoutSec = 60 }
    if ($null -ne $Body) {
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

function Sql($query) {
    if (-not $PSQL) {
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return $out
    }
    $f = Join-Path $env:TEMP ("pl_ark_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return $out
}
function Tek($query) { return (($(Sql $query)) -join '').Trim() }

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$surum = (& "$PSScriptRoot\yasal-surum.ps1")

function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }

function NewUser($ad) {
    $hwid = NewHwid
    $mail = "ark$([Guid]::NewGuid().ToString('N').Substring(0,10))@test.dev"
    $reg = Api POST '/api/auth/register' @{
        email = $mail; password = 'Parola12345'; displayName = $ad
        termsVersion = $surum; ageConfirmed = $true; hwidHash = $hwid
    }
    Api POST '/api/auth/verify-email' @{ email = $mail; code = $reg.verificationToken } | Out-Null
    $login = Api POST '/api/auth/login' @{ email = $mail; password = 'Parola12345'; hwidHash = $hwid }
    return [PSCustomObject]@{ Ad = $ad; Token = $login.accessToken; UserId = $login.userId }
}

# Konusuz istek gönder + kabul et → iki kişi arkadaş olur.
function Arkadas($gonderen, $alan) {
    $mid = Api POST '/api/matches' @{ responderUserId = $alan.UserId } $gonderen.Token
    Api POST "/api/matches/$mid/respond" @{ accept = $true } $alan.Token | Out-Null
    return $mid
}

# Bir profilin arkadaş yüzeyini okur ve SAYI=LİSTE değişmezini her okumada sınar.
function Yuzey($hedef, $bakan, $etiket) {
    $r = Api GET "/api/users/$($hedef.UserId)/friends" $null $bakan.Token
    $liste  = @($r.friends       | Where-Object { $null -ne $_ })
    $ortak  = @($r.mutualFriends | Where-Object { $null -ne $_ })

    # KÂHİN KONTROLÜ — her okumada. Ortak sayı ile ortak liste ayrışırsa engel ilişkisinin
    # varlığı üçüncü bir profilden okunabilir hâle gelir.
    if ($r.isSelf) {
        if ($liste.Count -ne [Math]::Min($r.friendCount, 100)) {
            Fail "$etiket — friendCount ($($r.friendCount)) ile liste ($($liste.Count)) ayrıştı"
        }
    }
    else {
        if ($liste.Count -ne 0) { Fail "$etiket — başkasının profilinde TAM LİSTE sızdı ($($liste.Count) kişi)" }
        if ($ortak.Count -ne [Math]::Min($r.mutualCount, 12)) {
            Fail "$etiket — mutualCount ($($r.mutualCount)) ile ortak liste ($($ortak.Count)) ayrıştı"
        }
    }
    return [PSCustomObject]@{
        Sayi = [int]$r.friendCount; Kendisi = [bool]$r.isSelf
        Liste = $liste; OrtakSayi = [int]$r.mutualCount; Ortak = $ortak
    }
}

function Icinde($liste, $kisi) { return (@($liste | Where-Object { $_.userId -eq $kisi.UserId }).Count) }

Write-Host "PeerLearn — profil arkadaş bölümü testi" -ForegroundColor White
Write-Host "API: $API   damga: $stamp"

# ---------------------------------------------------------------------------
Step 'Hazırlık: dört kullanıcı'
$ada  = NewUser "ArkAda$stamp"
$bora = NewUser "ArkBora$stamp"
$ceren = NewUser "ArkCeren$stamp"
$demir = NewUser "ArkDemir$stamp"
OK 'dört kullanıcı kuruldu'

# ---------------------------------------------------------------------------
Step 'A. Boş durum'
$y = Yuzey $ada $ada 'A/boş'
Esit 'yeni kullanıcının arkadaş sayısı 0' $y.Sayi 0
Esit 'kendi profilinde isSelf true' $y.Kendisi $true
Esit 'liste boş' $y.Liste.Count 0

# ---------------------------------------------------------------------------
Step 'B. Arkadaşlık kuruluyor'
$mAB = Arkadas $ada $bora
Arkadas $ada $ceren | Out-Null
Arkadas $bora $ceren | Out-Null
Arkadas $bora $demir | Out-Null

$y = Yuzey $ada $ada 'B/ada'
Esit 'Ada''nın 2 arkadaşı var' $y.Sayi 2
Esit 'listede Bora görünüyor' (Icinde $y.Liste $bora) 1
Esit 'listede Ceren görünüyor' (Icinde $y.Liste $ceren) 1
Esit 'listede Demir YOK (arkadaş değil)' (Icinde $y.Liste $demir) 0

# İSTEK YÖNÜ ÖNEMSİZ: Ada isteği gönderdi, Bora kabul etti. İkisinin de listesinde
# diğeri olmalı — sorgu iki bacağı da (Initiator ve Responder) taramazsa bu iddia kırılır.
$y = Yuzey $bora $bora 'B/bora'
Esit 'kabul eden tarafın da listesinde var (iki yönlü)' (Icinde $y.Liste $ada) 1
Esit 'Bora''nın 3 arkadaşı var' $y.Sayi 3

# ---------------------------------------------------------------------------
Step 'C. ⚠️ AYNI ÇİFTE İKİNCİ KABUL SAYIYI ŞİŞİRMİYOR'

# Ham SQL: uygulama aynı çifte ikinci bir Pending istek yazdırmıyor ama FARKLI KONULU
# ya da TERS YÖNLÜ istekler bu yolu açıyor. RespondedAtUtc dolduruluyor — boş bırakmak
# e2e-social.ps1'in sistem geneli değişmezini kırar (o hata bir kez yapıldı).
Sql @"
INSERT INTO matchmaking."Matches" ("Id","InitiatorUserId","ResponderUserId","RequestedTopicId","OfferedTopicId","Status","CreatedAtUtc","RespondedAtUtc")
VALUES (gen_random_uuid(), '$($bora.UserId)', '$($ada.UserId)', NULL, NULL, 'Accepted', now(), now());
"@ | Out-Null

$cift = Tek "SELECT COUNT(*) FROM matchmaking.""Matches"" WHERE ""Status"" = 'Accepted' AND ((""InitiatorUserId"" = '$($ada.UserId)' AND ""ResponderUserId"" = '$($bora.UserId)') OR (""InitiatorUserId"" = '$($bora.UserId)' AND ""ResponderUserId"" = '$($ada.UserId)'));"
Esit 'kurulum: aynı çift için DB''de 2 Accepted satır var' $cift '2'

$y = Yuzey $ada $ada 'C/tekil'
Esit 'arkadaş sayısı HÂLÂ 2 (tekilleştirme çalışıyor)' $y.Sayi 2
Esit 'Bora listede BİR KEZ görünüyor' (Icinde $y.Liste $bora) 1

# ---------------------------------------------------------------------------
Step 'D. Başkasının profili: tam liste yok, ortak var'
$y = Yuzey $bora $ada 'D/ortak'
Esit 'isSelf false' $y.Kendisi $false
Esit 'Bora''nın arkadaş sayısı herkese görünüyor' $y.Sayi 3
Esit 'TAM LİSTE boş (başkasının listesi sızmıyor)' $y.Liste.Count 0
Esit 'ortak arkadaş sayısı 1 (Ceren)' $y.OrtakSayi 1
Esit 'ortak listede Ceren var' (Icinde $y.Ortak $ceren) 1
Esit 'ortak listede Demir YOK (Ada onu tanımıyor)' (Icinde $y.Ortak $demir) 0

# ---------------------------------------------------------------------------
Step 'E. Engel arkadaş yüzeyinden de kesiyor'

# ⚠️ ENGELLEME KABUL EDİLMİŞ EŞLEŞMEYİ KAPATMIYOR (bilinçli sınır, gerekçe
# SendMessage.cs'te). Yani Status 'Accepted' KALIYOR ve arkadaş sorgusu engeli AYRICA
# elemek zorunda. Bu bölüm tam olarak o elemenin yapıldığını sınıyor.
Api POST '/api/blocks' @{ userId = $ceren.UserId; note = $null } $ada.Token | Out-Null

$durum = Tek "SELECT ""Status"" FROM matchmaking.""Matches"" WHERE (""InitiatorUserId"" = '$($ada.UserId)' AND ""ResponderUserId"" = '$($ceren.UserId)') OR (""InitiatorUserId"" = '$($ceren.UserId)' AND ""ResponderUserId"" = '$($ada.UserId)');"
Esit 'eşleşme DB''de hâlâ Accepted (engelleme kapatmıyor)' $durum 'Accepted'

$y = Yuzey $ada $ada 'E/engelli'
Esit 'engellenen arkadaş SAYIDAN düştü' $y.Sayi 1
Esit 'engellenen arkadaş LİSTEDEN düştü' (Icinde $y.Liste $ceren) 0

$y = Yuzey $bora $ada 'E/ortak-engelli'
Esit 'engellenen kişi ORTAK ARKADAŞLARDAN düştü' $y.OrtakSayi 0
Esit 'profil sahibinin sayısı DEĞİŞMEDİ (bakanın engeli sayıya karışmıyor)' $y.Sayi 3

# ÇİFT YÖNLÜLÜK: engeli karşı taraf koyduğunda da eleniyor mu?
Api POST '/api/blocks' @{ userId = $bora.UserId; note = $null } $demir.Token | Out-Null
$y = Yuzey $bora $bora 'E/ters-yon'
Esit 'BENİ engelleyen kişi de listemden düştü (çift yönlü)' (Icinde $y.Liste $demir) 0
Esit 'Bora''nın sayısı 2''ye düştü' $y.Sayi 2

Write-Host '  --- SERBEST İDDİA: engel kalkınca geri geliyor' -ForegroundColor DarkGray
Api DELETE "/api/blocks/$($ceren.UserId)" $null $ada.Token | Out-Null
Api DELETE "/api/blocks/$($bora.UserId)" $null $demir.Token | Out-Null
$y = Yuzey $ada $ada 'E/serbest'
Esit 'engel kalkınca sayı 2''ye döndü' $y.Sayi 2
Esit 'Ceren listeye geri geldi' (Icinde $y.Liste $ceren) 1
$y = Yuzey $bora $ada 'E/serbest-ortak'
Esit 'ortak arkadaş geri geldi' $y.OrtakSayi 1

# ---------------------------------------------------------------------------
Step 'F. Aktif olmayan hesaplar düşüyor'

# Hesap silme Users satırını SİLMİYOR, anonimleştiriyor ve eşleşme satırları duruyor.
# Süzgeç yazılmazsa liste "Silinmiş kullanıcı" kartlarıyla dolar. Tek koşul
# (Status == Active) Deleted/Banned/Suspended/PendingVerification'ın dördünü kapatıyor.
foreach ($d in @('Suspended', 'Banned', 'Deleted')) {
    Sql "UPDATE identity.""Users"" SET ""Status"" = '$d' WHERE ""Id"" = '$($ceren.UserId)';" | Out-Null
    $y = Yuzey $ada $ada "F/$d"
    Esit "$d hesap sayıdan düştü" $y.Sayi 1
    Esit "$d hesap listede yok" (Icinde $y.Liste $ceren) 0
}
Sql "UPDATE identity.""Users"" SET ""Status"" = 'Active' WHERE ""Id"" = '$($ceren.UserId)';" | Out-Null
$y = Yuzey $ada $ada 'F/geri'
Esit 'hesap tekrar Active olunca geri geldi' $y.Sayi 2

# ---------------------------------------------------------------------------
Step 'G. Arkadaşlıktan çıkarma sayıyı düşürüyor'
Api POST "/api/matches/$mAB/close" $null $ada.Token | Out-Null
$y = Yuzey $ada $ada 'G/kapatildi'

# ⚠️ AYNI ÇİFTİN İKİNCİ 'Accepted' SATIRI C BÖLÜMÜNDE YAZILMIŞTI ve CloseMatch tek bir
# satırı kapatıyor. Yani arkadaşlık DEVAM EDİYOR — bu bir hata değil, tekilleştirmenin
# doğal sonucu ve ürün kararı olarak kayıtlı (docs/DEVAM-EDILECEK.md). İddia bunu
# YAPISAL olarak sabitliyor: davranış bir gün değişirse burası kırılır ve karar
# yeniden konuşulur.
Esit 'ikinci kabul satırı durduğu için arkadaşlık sürüyor' $y.Sayi 2
Esit 'Bora hâlâ listede' (Icinde $y.Liste $bora) 1

Sql "UPDATE matchmaking.""Matches"" SET ""Status"" = 'Closed', ""ClosedAtUtc"" = now() WHERE ""Status"" = 'Accepted' AND ((""InitiatorUserId"" = '$($ada.UserId)' AND ""ResponderUserId"" = '$($bora.UserId)') OR (""InitiatorUserId"" = '$($bora.UserId)' AND ""ResponderUserId"" = '$($ada.UserId)'));" | Out-Null
$y = Yuzey $ada $ada 'G/hepsi-kapali'
Esit 'çiftin TÜM kabul satırları kapanınca arkadaşlık bitti' $y.Sayi 1
Esit 'Bora listeden düştü' (Icinde $y.Liste $bora) 0

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor Yellow
if ($script:failures -eq 0) { Write-Host 'TÜM ADIMLAR BAŞARILI' -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow
