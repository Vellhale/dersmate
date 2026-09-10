# PeerLearn — isimle arama ve kullanıcı engelleme testi
#
# İKİ ÖZELLİK TEK PAKETTE ve bu bilinçli: ayrılamazlar. "Herkes isimle aranabilir"
# kararı, keşif kapsamını üniversitesini girmiş kullanıcılardan TÜM kullanıcılara
# açtı; engelleme o açılmanın bedeli olarak aynı değişiklikte geldi. Ayrı paketlere
# bölünseydi, biri yeşilken diğeri kırmızıyken "özellik çalışıyor" denebilirdi.
#
# ─── KANIT STANDARDI ──────────────────────────────────────────────────────────
# Engellemenin GERÇEKTEN kestiğini göstermek için her yasak iddianın yanında bir
# SERBEST iddia var: engel kaldırıldığında aynı çağrı başarılı oluyor. Yalnızca
# "engelliyken 409 döndü" demek, çağrının başka bir sebeple de düşmüş olabileceğini
# dışlamaz — bu projede dört test aynı anda yanlış nedenle geçiyordu.
#
# ─── KAPSAM ───────────────────────────────────────────────────────────────────
#   A. İsimle arama (üniversitesi olmayan kişi, Türkçe katlama, kendini görmeme)
#   B. Konusuz tanışma isteği (üniversite kapısı kalktı)
#   C. Engelleme: idempotens, liste, çift yönlü istek reddi, bekleyen isteğin kapanması
#   D. Aramada görünmeme (çift yönlü)
#   E. AÇIK SOHBET kesiliyor, geçmiş okunabilir kalıyor
#   F. YENİ DERS rezervasyonu kesiliyor, eşleşme Accepted kalıyor
#   G. Sınırlar: kendini engelleme, olmayan kullanıcı, "beni kimler engelledi" yok
#   H. Günlük istek tavanı (20) — üniversite kapısının yerine gelen fren

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

# psql üç yoldan aranır (diğer paketlerle aynı gerekçe: testin KOŞAMAMASI,
# başarısız olmasından daha sinsi — özet onu kırmızı değil, hiç görünmemiş sayar).
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
        # UTF-8 bayt olarak: PS 5.1 string gövdeyi Latin-1 kodluyor ve Türkçe
        # karakterler sunucuda 400 üretiyor.
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

# Hata BEKLENEN çağrılar: durum kodu + hata kodu döner, hiç düşmez.
function ApiHata {
    param($Method, $Path, $Body, $Token)
    try {
        Api -Method $Method -Path $Path -Body $Body -Token $Token | Out-Null
        return @{ status = 200; code = 'BEKLENMEDIK_BASARI' }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ status = 0; code = 'NO_RESPONSE'; detail = $_.Exception.Message } }
        $raw = ''
        try {
            $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $raw = $reader.ReadToEnd()
        } catch {}
        $obj = $null
        try { $obj = $raw | ConvertFrom-Json } catch {}
        if ($obj) { return @{ status = [int]$resp.StatusCode; code = $obj.title; detail = $obj.detail } }
        return @{ status = [int]$resp.StatusCode; code = 'PARSE_FAIL'; detail = $raw }
    }
}

function Sql($query) {
    if (-not $PSQL) {
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return $out
    }
    $f = Join-Path $env:TEMP ("pl_engel_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return $out
}

function Tek($query) { return (($(Sql $query) -join '')).Trim() }

# Test betikleri idempotent DEĞİLDİR (CLAUDE.md): sabit HWID ban bırakır, sabit ad
# filtreleri bozar. Koşuma özel damga her ikisini de üretiyor.
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$surum = (& "$PSScriptRoot\yasal-surum.ps1")

function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }

function NewUser($ad) {
    $hwid = NewHwid
    $mail = "engel$([Guid]::NewGuid().ToString('N').Substring(0,10))@test.dev"
    $reg = Api POST '/api/auth/register' @{
        email = $mail; password = 'Parola12345'; displayName = $ad
        termsVersion = $surum; ageConfirmed = $true; hwidHash = $hwid
    }
    Api POST '/api/auth/verify-email' @{ email = $mail; code = $reg.verificationToken } | Out-Null
    $login = Api POST '/api/auth/login' @{ email = $mail; password = 'Parola12345'; hwidHash = $hwid }
    return [PSCustomObject]@{ Ad = $ad; Mail = $mail; Token = $login.accessToken; UserId = $login.userId }
}

Write-Host "PeerLearn — isimle arama ve engelleme testi" -ForegroundColor White
Write-Host "API: $API   damga: $stamp"

# ---------------------------------------------------------------------------
Step 'Hazırlık: dört kullanıcı'

# ADA'nın adı BÜYÜK NOKTALI İ ile başlıyor. Türkçe katlama tuzağının (İ/ı/i) testi
# buna dayanıyor: küçük harfle "inci" yazan kişi "İnci"yi bulabilmeli.
$adaAd  = "İnci Zqx$stamp"
$boraAd = "Bora Zqx$stamp"
$cemAd  = "Cem Zqx$stamp"
$denAd  = "Deniz Zqx$stamp"

$ada  = NewUser $adaAd
$bora = NewUser $boraAd
$cem  = NewUser $cemAd
$den  = NewUser $denAd
OK "dört kullanıcı kuruldu (damga $stamp)"

# BORA üniversitesini giriyor, ADA GİRMİYOR. Testin can alıcı ayrımı bu: ADA
# üniversite ağının parçası DEĞİL ama isimle bulunabilmeli.
Api PUT '/api/profile' @{ displayName = $boraAd; bio = $null; university = "Zqx$stamp Üniversitesi"; department = 'Fizik' } $bora.Token | Out-Null
OK 'Bora üniversitesini girdi, İnci profilini boş bıraktı'

# ---------------------------------------------------------------------------
Step 'A. İsimle arama'

$bul = Api GET "/api/discovery/users?name=Zqx$stamp" $null $bora.Token
$adlar = @($bul.items | ForEach-Object { $_.displayName })
if ($adlar -contains $adaAd) { OK 'üniversitesini GİRMEMİŞ kullanıcı isimle bulundu' }
else { Fail "İnci isimle bulunamadı — dönen: $($adlar -join ', ')" }

# MUTASYON KARŞITI: aynı kişi üniversite filtresinde ÇIKMAMALI. Bu iddia olmasaydı
# "isim aramasında bulundu" sonucu, üniversite şartının hiç var olmadığıyla da
# açıklanabilirdi.
# ARAMAYI CEM YAPIYOR, Bora değil: uç kullanıcının KENDİ kaydını sonuçtan eliyor,
# yani Bora kendi üniversitesini aradığında kendini bulamaz ve iddia yanlış nedenle
# kırılırdı.
$uni = Api GET "/api/discovery/users?university=Zqx$stamp" $null $cem.Token
$uniAdlar = @($uni.items | ForEach-Object { $_.displayName })
if ($uniAdlar -notcontains $adaAd) { OK 'aynı kişi ÜNİVERSİTE filtresinde çıkmıyor (profili boş)' }
else { Fail 'profili boş kullanıcı üniversite filtresinde çıktı' }
if ($uniAdlar -contains $boraAd) { OK 'üniversite filtresi kendi işini yapıyor (Bora bulundu)' }
else { Fail 'üniversite filtresi Bora''yı bulamadı — filtre hiç çalışmıyor olabilir' }

# Türkçe katlama: küçük harf "inci" ve büyük harf "İNCİ" aynı kişiyi bulmalı.
$kucuk = Api GET "/api/discovery/users?name=inci zqx$stamp" $null $bora.Token
Esit 'küçük harfle arandığında bulundu (İ -> i katlaması)' (@($kucuk.items).Count) 1

$buyuk = Api GET "/api/discovery/users?name=İNCİ ZQX$stamp" $null $bora.Token
Esit 'büyük harfle arandığında da bulundu' (@($buyuk.items).Count) 1

# Kendini görmeme: arama sonucu "kiminle tanışayım" listesi, kendisi orada anlamsız.
$kendi = Api GET "/api/discovery/users?name=$adaAd" $null $ada.Token
$kendiAdlar = @($kendi.items | ForEach-Object { $_.userId })
if ($kendiAdlar -notcontains $ada.UserId) { OK 'kullanıcı kendi kaydını sonuçta görmüyor' }
else { Fail 'kullanıcı kendini arama sonucunda gördü' }

# ---------------------------------------------------------------------------
Step 'B. Konusuz tanışma isteği (üniversite kapısı kalktı)'

$m1 = Api POST '/api/matches' @{ responderUserId = $ada.UserId } $bora.Token
if ($m1) { OK 'üniversitesi olmayan kişiye konusuz istek gönderildi' } else { Fail 'konusuz istek gitmedi' }

$durum = Tek "SELECT ""Status"" FROM matchmaking.""Matches"" WHERE ""Id"" = '$m1';"
Esit 'istek Pending olarak yazıldı' $durum 'Pending'

# ---------------------------------------------------------------------------
Step 'C. Engelleme'

$not = "koşum $stamp"
Api POST '/api/blocks' @{ userId = $bora.UserId; note = $not } $ada.Token | Out-Null
OK 'İnci, Bora''yı engelledi (204)'

# İdempotens: kullanıcı düğmeye iki kez bastığında hata görmesinin faydası yok.
Api POST '/api/blocks' @{ userId = $bora.UserId; note = $null } $ada.Token | Out-Null
OK 'aynı engel ikinci kez de başarılı (idempotent)'

$engelSayisi = Tek "SELECT COUNT(*) FROM identity.""UserBlocks"" WHERE ""BlockerUserId"" = '$($ada.UserId)' AND ""BlockedUserId"" = '$($bora.UserId)';"
Esit 'ikinci engelleme İKİNCİ SATIR yazmadı' $engelSayisi '1'

$liste = @(Api GET '/api/blocks' $null $ada.Token)
$kayit = $liste | Where-Object { $_.userId -eq $bora.UserId } | Select-Object -First 1
if ($kayit) { OK 'engel listesinde görünüyor' } else { Fail 'engel listesinde yok' }
Esit 'ilk engellemedeki not korundu (ikinci çağrı ezmedi)' $kayit.note $not

# ÇİFT YÖNLÜ: engelleyen de gönderemez. Tek yön yazılsaydı rahatsız eden taraf
# istek göndermeye devam ederdi.
$h1 = ApiHata POST '/api/matches' @{ responderUserId = $bora.UserId } $ada.Token
Esit 'engelleyen -> engellenen istek REDDEDİLDİ' $h1.status 409
$h2 = ApiHata POST '/api/matches' @{ responderUserId = $ada.UserId } $bora.Token
Esit 'engellenen -> engelleyen istek de REDDEDİLDİ' $h2.status 409

# Mesaj engelin varlığını SÖYLEMEMELİ: "seni engelledi" demek misilleme kapısı açardı.
if ("$($h2.detail)" -notmatch 'engel') { OK 'hata mesajı engelin varlığını sızdırmıyor' }
else { Fail "hata mesajı engeli açık ediyor: $($h2.detail)" }

# Bekleyen istek kapatılmalı: kalsaydı kabul edilince sohbet açılır ve engel
# kurulduğu gün delinirdi.
$durum2 = Tek "SELECT ""Status"" FROM matchmaking.""Matches"" WHERE ""Id"" = '$m1';"
Esit 'engellemeden önce gönderilmiş BEKLEYEN istek kapandı' $durum2 'Declined'

# ---------------------------------------------------------------------------
Step 'D. Aramada görünmeme (çift yönlü)'

$ara1 = Api GET "/api/discovery/users?name=Zqx$stamp" $null $ada.Token
$ara1Ad = @($ara1.items | ForEach-Object { $_.displayName })
if ($ara1Ad -notcontains $boraAd) { OK 'engelleyen, engellediğini aramada görmüyor' }
else { Fail 'engellenen kişi aramada çıktı' }

$ara2 = Api GET "/api/discovery/users?name=Zqx$stamp" $null $bora.Token
$ara2Ad = @($ara2.items | ForEach-Object { $_.displayName })
if ($ara2Ad -notcontains $adaAd) { OK 'engellenen, engelleyeni aramada görmüyor (çift yönlü)' }
else { Fail 'engelleyen kişi, engellenenin aramasında çıktı' }

# MUTASYON KARŞITI: aynı sorgu Cem''i hâlâ buluyor — liste boş dönmüyor, süzülüyor.
if ($ara2Ad -contains $cemAd) { OK 'aynı sorgu engelsiz kullanıcıyı hâlâ buluyor (liste boşalmadı)' }
else { Fail 'sorgu tümüyle boşaldı — eleme çok geniş' }

# ---------------------------------------------------------------------------
Step 'E. Açık sohbet kesiliyor, geçmiş okunabilir kalıyor'

# İnci <-> Cem: konusuz eşleşme kabul edilir, sohbet açılır.
$m2 = Api POST '/api/matches' @{ responderUserId = $cem.UserId } $ada.Token
$kabul = Api POST "/api/matches/$m2/respond" @{ accept = $true } $cem.Token
$sohbet = $kabul.conversationId
if ($sohbet) { OK 'eşleşme kabul edildi, sohbet açıldı' } else { Fail 'sohbet açılmadı' }

Api POST "/api/conversations/$sohbet/messages" @{ content = "merhaba $stamp" } $ada.Token | Out-Null
OK 'engelden ÖNCE mesaj gönderilebiliyor'

Api POST '/api/blocks' @{ userId = $ada.UserId; note = $null } $cem.Token | Out-Null
OK 'Cem, İnci''yi engelledi'

$hm1 = ApiHata POST "/api/conversations/$sohbet/messages" @{ content = 'gecmeli mi' } $ada.Token
Esit 'engellenen taraf artık YAZAMIYOR (403)' $hm1.status 403
$hm2 = ApiHata POST "/api/conversations/$sohbet/messages" @{ content = 'ben de mi' } $cem.Token
Esit 'engelleyen taraf da yazamıyor (çift yönlü)' $hm2.status 403

# Geçmiş OKUNABİLİR kalmalı: mesajlar bir şikâyetin dayanağı. Engellemenin geçmişi
# silmesi, tacizciye "engellet, kanıt uçsun" düğmesi vermek olurdu.
# Uç DÜZ LİSTE dönüyor (PagedResult değil) — `.items` okumak sessizce boş küme verir.
$gecmis = Api GET "/api/conversations/$sohbet/messages" $null $ada.Token
$icerikler = @($gecmis | ForEach-Object { $_.content })
if ($icerikler -contains "merhaba $stamp") { OK 'engelden sonra GEÇMİŞ hâlâ okunabiliyor (kanıt korunuyor)' }
else { Fail 'geçmiş mesajlar okunamıyor — kanıt kayboldu' }

# Eşleşme KAPATILMIYOR ve bu bilinçli: kapatmak, sonuçlanmamış ders varsa
# ekonomiyi sahipsiz bırakırdı (bkz. SendMessage''taki gerekçe).
$m2Durum = Tek "SELECT ""Status"" FROM matchmaking.""Matches"" WHERE ""Id"" = '$m2';"
Esit 'kabul edilmiş eşleşme Accepted kalıyor (kapatılmıyor)' $m2Durum 'Accepted'

# SERBEST İDDİA: engel kalkınca aynı çağrı çalışıyor — 403 engelden geliyordu.
Api DELETE "/api/blocks/$($ada.UserId)" $null $cem.Token | Out-Null
Api POST "/api/conversations/$sohbet/messages" @{ content = "tekrar $stamp" } $ada.Token | Out-Null
OK 'engel kalkınca aynı sohbete yeniden yazılabiliyor'

# ---------------------------------------------------------------------------
Step 'F. Yeni ders rezervasyonu kesiliyor'

# Konulu eşleşme gerekiyor: üniversite ağı (konusuz) eşleşmesinden ders rezerve
# edilemiyor zaten — o ayrı bir muhafız ve burada ölçülen o değil.
$konuId = [Guid]::NewGuid().ToString()
Sql @"
INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT '$konuId', s."Id", 'E2E Engel $stamp', 99, TRUE, now()
FROM catalog."Subjects" s WHERE s."Name" = 'Matematik' LIMIT 1;
"@ | Out-Null
$konuVar = Tek "SELECT COUNT(*) FROM catalog.""Topics"" WHERE ""Id"" = '$konuId';"
Esit 'koşuma özel konu üretildi' $konuVar '1'

Api POST '/api/portfolio/entries' @{ topicId = $konuId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $cem.Token | Out-Null
$m3 = Api POST '/api/matches' @{ responderUserId = $cem.UserId; requestedTopicId = $konuId } $den.Token
Api POST "/api/matches/$m3/respond" @{ accept = $true } $cem.Token | Out-Null
OK 'konulu eşleşme kuruldu ve kabul edildi'

$saat1 = [DateTime]::UtcNow.AddHours(5).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$ders1 = Api POST '/api/sessions' @{ matchId = $m3; topicId = $konuId; scheduledStartUtc = $saat1; durationMinutes = 60 } $den.Token
if ($ders1.sessionId) { OK 'engelden ÖNCE ders rezerve edilebiliyor' } else { Fail 'ilk rezervasyon başarısız' }

Api POST '/api/blocks' @{ userId = $den.UserId; note = $null } $cem.Token | Out-Null
$saat2 = [DateTime]::UtcNow.AddHours(9).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$hd = ApiHata POST '/api/sessions' @{ matchId = $m3; topicId = $konuId; scheduledStartUtc = $saat2; durationMinutes = 60 } $den.Token
Esit 'engelden SONRA yeni ders rezerve edilemiyor (409)' $hd.status 409

# Devam eden ders etkilenmemeli: iptal/onay/itiraz yollarından bitirilebilmeli.
$dersDurum = Tek "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$($ders1.sessionId)';"
Esit 'engelden önce rezerve edilmiş ders BOZULMADI' $dersDurum 'Booked'

# SERBEST İDDİA: engel kalkınca aynı rezervasyon geçiyor.
Api DELETE "/api/blocks/$($den.UserId)" $null $cem.Token | Out-Null
$ders2 = Api POST '/api/sessions' @{ matchId = $m3; topicId = $konuId; scheduledStartUtc = $saat2; durationMinutes = 60 } $den.Token
if ($ders2.sessionId) { OK 'engel kalkınca aynı rezervasyon başarılı (409 engelden geliyordu)' }
else { Fail 'engel kalktıktan sonra da rezerve edilemedi' }

# ---------------------------------------------------------------------------
Step 'F2. İlan araması ve öneriler de eliyor'

<#
  BU BOŞLUK 2026-09-10'DA AÇIK KALMIŞTI. Engelleme sevk edilirken yalnızca İSİM ARAMASI
  engeli eliyordu; ilan araması (SearchOffers) ve eşleşme önerileri (GetMatchSuggestions)
  elemiyordu. Yani engellediğin kişi Keşfet'in ana listelerinde görünmeye devam ediyordu
  ve kartına basınca hata alıyordun — "görünmemek, hata vermekten sessizdir" kuralının
  tam tersi.

  İlan aramasında süzgeç ÖNBELLEKTEN SONRA uygulanıyor (anahtar kullanıcıdan bağımsız),
  bu yüzden burada AYRICA sınanıyor: önbellekten gelen sayfa süzülmezse iddia kırılır.
#>
$konuIlan = [Guid]::NewGuid().ToString()
Sql @"
INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT '$konuIlan', s."Id", 'E2E EngelIlan $stamp', 99, TRUE, now()
FROM catalog."Subjects" s WHERE s."Name" = 'Fizik' LIMIT 1;
"@ | Out-Null

# Cem ilan açıyor, Deniz arıyor: önce görünüyor, engelden sonra görünmüyor.
Api POST '/api/portfolio/entries' @{ topicId = $konuIlan; direction = 'Offer'; selfAssessedLevel = 4; note = $null } $cem.Token | Out-Null
$arama = Api GET "/api/discovery/offers?search=EngelIlan $stamp" $null $den.Token
$oncesi = @($arama.items | Where-Object { $_.tutorUserId -eq $cem.UserId }).Count
Esit 'engelden ÖNCE ilan aramada görünüyor' $oncesi 1

Api POST '/api/blocks' @{ userId = $cem.UserId; note = $null } $den.Token | Out-Null
$arama = Api GET "/api/discovery/offers?search=EngelIlan $stamp" $null $den.Token
$sonrasi = @($arama.items | Where-Object { $_.tutorUserId -eq $cem.UserId }).Count
Esit 'engelden SONRA ilan aramada YOK' $sonrasi 0

# SERBEST İDDİA: engel kalkınca aynı arama yine buluyor — süzgecin sebebi engel.
Api DELETE "/api/blocks/$($cem.UserId)" $null $den.Token | Out-Null
$arama = Api GET "/api/discovery/offers?search=EngelIlan $stamp" $null $den.Token
Esit 'engel kalkınca ilan geri geldi' (@($arama.items | Where-Object { $_.tutorUserId -eq $cem.UserId }).Count) 1

# Öneriler: Deniz o konuyu ÖĞRENMEK istediğini beyan edince Cem önerilmeli.
Api POST '/api/portfolio/entries' @{ topicId = $konuIlan; direction = 'Seek'; selfAssessedLevel = 1; note = $null } $den.Token | Out-Null
$oneri = @(Api GET '/api/portfolio/suggestions?limit=50' $null $den.Token)
Esit 'engelsizken Cem öneriliyor' (@($oneri | Where-Object { $_.userId -eq $cem.UserId }).Count) 1

Api POST '/api/blocks' @{ userId = $cem.UserId; note = $null } $den.Token | Out-Null
$oneri = @(Api GET '/api/portfolio/suggestions?limit=50' $null $den.Token)
Esit 'engelliyken Cem ÖNERİLMİYOR' (@($oneri | Where-Object { $_.userId -eq $cem.UserId }).Count) 0
Api DELETE "/api/blocks/$($cem.UserId)" $null $den.Token | Out-Null

# ---------------------------------------------------------------------------
Step 'G. Sınırlar'

$hk = ApiHata POST '/api/blocks' @{ userId = $ada.UserId; note = $null } $ada.Token
Esit 'kendini engelleme reddedildi' $hk.status 400

$hy = ApiHata POST '/api/blocks' @{ userId = [Guid]::NewGuid().ToString(); note = $null } $ada.Token
Esit 'olmayan kullanıcıyı engelleme 404' $hy.status 404

# YAPISAL İDDİA: "beni kimler engelledi" diye bir uç YOK. Bora, İnci tarafından
# engellendi; kendi listesi buna rağmen BOŞ olmalı — o liste engellemeyi
# misillemeye çevirirdi.
# ⚠️ PS 5.1 TUZAĞI: Invoke-RestMethod boş JSON dizisinde $null döndürüyor ve
# @($null).Count 1'dir — sayı, "bir kayıt var" gibi okunur. Null'lar süzülmeli.
$boraListe = @((Api GET '/api/blocks' $null $bora.Token) | Where-Object { $null -ne $_ })
Esit 'engellenen kişi kendi listesinde hiçbir şey görmüyor' $boraListe.Count 0

# Engel kaldırma idempotent: olmayan engeli kaldırmak da başarılı.
Api DELETE "/api/blocks/$($cem.UserId)" $null $bora.Token | Out-Null
OK 'olmayan engeli kaldırmak da başarılı (idempotent)'

# ---------------------------------------------------------------------------
Step 'H. Günlük istek tavanı (20)'

# Tavan, üniversite kapısı kalktığında onun yerine konan fren: "kaç FARKLI kişiye
# istek atabilirsin". SQL ile 19 kapalı istek tohumlanıyor — 21 gerçek kullanıcı
# üretmek koşumu dakikalarca uzatırdı ve tavan CreatedAtUtc''ye bakıyor, duruma değil.
# TOHUM MEVCUDA GÖRE: Deniz F adımında gerçek bir istek gönderdi. Sabit 19 eklemek
# sayacı 20'ye çıkarır ve 20. istek zaten tavanda kalırdı — test, tavanın YERİNİ
# değil kendi kurulumunu ölçmüş olurdu.
$mevcut = [int](Tek "SELECT COUNT(*) FROM matchmaking.""Matches"" WHERE ""InitiatorUserId"" = '$($den.UserId)' AND ""CreatedAtUtc"" >= now() - interval '1 day';")
$eksik = 19 - $mevcut
if ($eksik -gt 0) {
    <#
      ⚠️ RespondedAtUtc DOLDURULUYOR — atlanması BAŞKA BİR PAKETİ kırıyor.

      e2e-social.ps1 sistem geneli bir değişmez sınıyor: "Accepted/Declined olan her
      eşleşmede RespondedAtUtc dolu". Uygulama kodu bunu her zaman yazıyor; ham INSERT
      ise yazmıyordu ve tohumlanan satırlar o pakette KIRMIZI olarak, üstelik BU
      pakette değil ORADA görünüyordu. Ham SQL ile veri üreten her test, ürettiği
      satırın uygulamanın ürettiğiyle aynı biçimde olmasından sorumludur.
    #>
    Sql @"
INSERT INTO matchmaking."Matches" ("Id","InitiatorUserId","ResponderUserId","RequestedTopicId","OfferedTopicId","Status","CreatedAtUtc","RespondedAtUtc")
SELECT gen_random_uuid(), '$($den.UserId)', '$($ada.UserId)', NULL, NULL, 'Declined', now(), now()
FROM generate_series(1, $eksik);
"@ | Out-Null
}
$tohum = Tek "SELECT COUNT(*) FROM matchmaking.""Matches"" WHERE ""InitiatorUserId"" = '$($den.UserId)' AND ""CreatedAtUtc"" >= now() - interval '1 day';"
Esit 'Deniz 24 saat içinde 19 istek göndermiş sayılıyor' $tohum '19'

# 20. istek GEÇMELİ: tavan 20''de kapanıyorsa 20. hâlâ serbest olmalı. Sınırın
# yanlış tarafından ölçmek, "tavan var" ile "tavan bir eksik" arasını ayırt etmez.
$m20 = Api POST '/api/matches' @{ responderUserId = $bora.UserId } $den.Token
if ($m20) { OK '20. istek hâlâ geçiyor (tavan sınırın doğru tarafında)' } else { Fail '20. istek reddedildi — tavan bir eksik' }

$h21 = ApiHata POST '/api/matches' @{ responderUserId = $cem.UserId } $den.Token
Esit '21. istek TAVANA takıldı (429)' $h21.status 429

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor Yellow
if ($script:failures -eq 0) { Write-Host 'TÜM ADIMLAR BAŞARILI' -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow
