# PeerLearn — Ban, HWID ban kaçağı ve ekonomi değişmezleri
#
# NEDEN AYRI PAKET: bu bölümler e2e-dispute.ps1 içindeydi. İtiraz (Dispute) tek yönlü
# şikayete dönüştürülünce o paketin A–D bölümleri karşılıksız kaldı — itiraz AÇILAMIYOR,
# dolayısıyla "itirazı çözme" senaryosu da test edilemiyor. Yeni akışın testi
# e2e-report.ps1'de.
#
# Buradaki iki bölüm itirazdan BAĞIMSIZDI ve değerini koruyor:
#   • ban + HWID ban kaçağı — banlanan kullanıcının aynı cihazdan yeni hesapla dönmesi
#   • ekonomi değişmezleri — defter, lot ve sayaç tutarlılığı
# Paketi silmek bu kapsamı sessizce kaybettirirdi.
#
# Kapsanan senaryolar:
#   A. İtiraz öğrenci lehine   -> eğitmene BASIM YAPILMAZ, ders iptal
#   B. İtiraz eğitmen lehine   -> eğitmene puan BASILIR (60 dk = 100) ve unvan sayacı artar
#   C. İtiraz reddedildi       -> ders onay kuyruğuna döner, sayaç sıfırlanır, onay basım üretir
#   D. "Ders yapılmadı" itirazı reddedilirse ders Booked'a döner (eğitmene bedava basım kapısı açılmaz)
#   E. Ban + HWID ban kaçağı engeli
#   F. Tüm bunlardan sonra defter değişmezleri hâlâ geçerli mi
#
# KALDIRILAN KAPSAM — yeni ekonomi sözleşmesinde karşılığı olmadığı için:
#   • "escrow iade / capture" ve "kredi bloke" iddiaları. Öğrenci ders için hiçbir şey
#     ödemiyor; bloke edilecek, iade edilecek ya da transfer edilecek kredi YOK. Bu
#     iddialar silinmedi, YERİNE geçen ölçüme çevrildi: "eğitmene basım yapıldı mı,
#     miktarı doğru mu, öğrencinin bakiyesi değişmedi mi".
#   • "Sıfır toplam" (transfer bacakları toplamı = 0) ve "arz = mint − yakım" denetimleri.
#     Ders kazancı artık iki bacaklı bir transfer değil, karşılıksız BASIM; bu iki sorgu
#     her onaylanan derste yanlış alarm verirdi. Yerlerine defter değişmezi kondu:
#     SUM(cüzdan) == SUM(defterdeki tüm hareketler).
#   • "Bloke bakiye = aktif hold toplamı". Göçle tüm hold'lar Released yapıldı ve yeni
#     hold kavramı tamamen kaldırıldı (tablo ve kolon düşürüldü); iddia "bloke
#     toplamı 0" hâline geldi — sıfırlığın KENDİSİ test edilmeye değer, çünkü yeniden
#     hold yazan bir yol sızarsa ilk burada görülür.
#   • "Yetersiz kredi" (INSUFFICIENT_CREDITS) hata yolu: TAMAMEN SİLİNDİ, karşılığı yok.
#     Bakiye kontrolü kalktığı için hiçbir istek bu hatayı üretemiyor. Bu testte zaten
#     doğrudan sınanmıyordu; A bölümündeki "kredi hâlâ bloke" iddiası onun son izidir ve
#     yukarıda anlatıldığı gibi basım ölçümüne çevrildi.

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'

<#
  psql üç yoldan aranır; ilk bulunan kullanılır:
    1. Windows kurulumu   — geliştiricinin makinesinde tipik yol.
    2. PATH üzerinde psql — Linux/macOS ve CI koşucuları (postgresql-client).
    3. docker compose     — makinede psql yok ama compose yığını ayakta (bkz. Sql).

  Üçüncü yol olmadan bu paket, docs/GELISTIRME-ORTAMI.md nin tarif ettiği Docker
  kurulumunda hiç koşamıyordu: yalnızca yerel PostgreSQL 17 varsayılıyordu ve betik
  "psql bulunamadi" ile ortasında düşüyordu. Testin KOŞAMAMASI, başarısız olmasından
  daha sinsi — özet onu kırmızı değil, hiç görünmemiş sayar.
#>
if (-not (Test-Path $PSQL)) {
    # PS 5.1 UYUMU: null-koşullu operatör PowerShell 7 ile geldi ve 5.1 de SÖZDİZİMİ
    # hatasıdır — betik hiç başlamaz, yani bu kod hiç çalışmazdı.
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $PSQL = $bulunan } else { $PSQL = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'
$env:PGPASSWORD = 'PeerLearnDev2026'

# 60 dakikalık ders = iki blok x 50 puan. Sabit tek yerde: ödül ölçeği değişirse test
# tek satırda güncellenir, dört ayrı yerde birbirinden ayrılmaz.
$BEKLENEN_BASIM = 100

$script:failures = 0
function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Fail($msg)  { Write-Host "  [HATA] $msg" -ForegroundColor Red; $script:failures++ }

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

function InvokeExpectError([scriptblock]$call) {
    try { & $call | Out-Null; return $null }
    catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ code = 'NO_RESPONSE'; detail = $_.Exception.Message } }
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $raw = $reader.ReadToEnd()
        try {
            $o = $raw | ConvertFrom-Json
            return @{ code = $o.title; detail = $o.detail; status = [int]$resp.StatusCode }
        } catch { return @{ code = 'PARSE_FAIL'; detail = $raw; status = [int]$resp.StatusCode } }
    }
}

function Sql($query) {
    if (-not $PSQL) {
        # Docker yolu: sorgu STDIN den geçer. -c ile argüman olarak geçirmek,
        # identity."Users" gibi tırnaklı adlardaki tırnakları kabuğa yedirir.
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return ($out -join '').Trim()
    }
    $f = Join-Path $env:TEMP ("pl_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return ($out -join '').Trim()
}

function UploadProof {
    param($SessionId, $Code, $Token)
    $pngB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
    $fileBytes = [Convert]::FromBase64String($pngB64)
    $boundary = [Guid]::NewGuid().ToString('N')
    $enc = [System.Text.Encoding]::UTF8
    $ms = New-Object System.IO.MemoryStream
    $head = "--$boundary`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$Code`r`n" +
            "--$boundary`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"proof.png`"`r`nContent-Type: image/png`r`n`r`n"
    $headBytes = $enc.GetBytes($head)
    $tailBytes = $enc.GetBytes("`r`n--$boundary--`r`n")
    $ms.Write($headBytes, 0, $headBytes.Length)
    $ms.Write($fileBytes, 0, $fileBytes.Length)
    $ms.Write($tailBytes, 0, $tailBytes.Length)
    $body = $ms.ToArray(); $ms.Dispose()
    Invoke-RestMethod -Uri "$API/api/sessions/$SessionId/complete" -Method Post `
        -Headers @{ Authorization = "Bearer $Token" } `
        -ContentType "multipart/form-data; boundary=$boundary" -Body $body -TimeoutSec 60
}

$script:seq = 0
function NewUser($prefix, $hwid) {
    $script:seq++
    $email = "$prefix$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())x$($script:seq)@test.dev"
    $reg = Api POST '/api/auth/register' @{ email = $email; password = 'Parola12345'; displayName = "$prefix Kullanici"; termsVersion = '2026-08-27'; ageConfirmed = $true }
    Api POST '/api/auth/verify-email' @{ token = $reg.verificationToken } | Out-Null
    $login = Api POST '/api/auth/login' @{ email = $email; password = 'Parola12345'; hwidHash = $hwid }
    return @{ email = $email; userId = $login.userId; token = $login.accessToken }
}

# /api/sessions artık aktif/geçmiş olarak ayrılmış bir nesne döner; bu yardımcı ikisini
# tek listede birleştirir (çağıranlar belirli bir dersi id ile arıyor).
function SessionsOf($token) {
    $r = Api GET '/api/sessions' $null $token
    @($r.active) + @($r.past.items)
}

function Wallet($token) { Api GET '/api/wallet' $null $token }

# ---- Yeni ekonominin ölçüm yardımcıları ---------------------------------------------
# "Basım yapıldı mı" sorusunun tek güvenilir kaynağı defterdeki LessonEarning bacağıdır:
# cüzdan bakiyesi hoş geldin kredisiyle karışır, TotalEarnedCredits ise başka derslerden
# de birikebilir. Ders bazında bakmak ikisini de dışarıda bırakır.
function MintedFor($sessionId) {
    [int](Sql "SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$sessionId' AND ""Type"" = 'LessonEarning';")
}

# Unvanın dayandığı birikimli sayaç. Basımla AYNI miktarda artmalı ve hiçbir yolda azalmamalı;
# bu iki sayı ayrışırsa unvanlar sessizce yanlışlanır (kimse şikâyet etmeden).
function TotalEarned($userId) {
    [int](Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$userId';")
}

# Escrow kaldırıldı: rezervasyon artık hold YAZMAMALI. Sıfırdan farklı her sonuç, ölü
# sanılan bir kod yolunun hâlâ koştuğunu gösterir.
function HoldCount($sessionId) {
    [int](Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';")
}

# Öğrenci + eğitmen + kabul edilmiş eşleşme + rezerve edilmiş ders üretir.
#
# HER SENARYO KENDİ KULLANICI ÇİFTİNİ KURAR — bu artık bir zorunluluk. MintGuard aynı
# eğitmen-öğrenci çiftine 24 saat içinde en fazla 2, aynı eğitmene 8 ders veriyor; ortak
# bir çift paylaşılsaydı üçüncü senaryo MINT_LIMIT_REACHED (429) alır ve test, konusu
# olmayan bir yerde kırılırdı. Senaryo başına tek ders açıldığı için tavanlara uzağız.
function NewScenario($topicId) {
    $student = NewUser 'ogr' (('1' * 64))
    $tutor   = NewUser 'egt' (('2' * 64))

    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $tutor.token | Out-Null
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek';  selfAssessedLevel = 2; note = $null } $student.token | Out-Null

    $matchId = Api POST '/api/matches' @{ responderUserId = $tutor.userId; requestedTopicId = $topicId; offeredTopicId = $null } $student.token
    Api POST "/api/matches/$matchId/respond" @{ accept = $true } $tutor.token | Out-Null

    # REZERVASYON ÖNCESİ FOTOĞRAF: "ders almak bedava" iddiası ancak öncesi/sonrası
    # karşılaştırmasıyla ölçülebilir. Hoş geldin kredisi sabit sayı olarak yazılmıyor —
    # miktarı config'ten geliyor ve değişirse test yalan söylemesin.
    $ogrenciBakiyeOnce = (Wallet $student.token).currentBalance
    $egitmenBakiyeOnce = (Wallet $tutor.token).currentBalance
    $egitmenPuanOnce   = TotalEarned $tutor.userId

    $start = [DateTime]::UtcNow.AddHours(2).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $booking = Api POST '/api/sessions' @{ matchId = $matchId; topicId = $topicId; scheduledStartUtc = $start; durationMinutes = 60 } $student.token

    return @{
        student   = $student
        tutor     = $tutor
        sessionId = $booking.sessionId
        code      = $booking.verificationCode
        # Yanıttaki alan adı creditCost DEĞİL mintAmount: tutar öğrenciden tahsil edilmiyor,
        # eğitmene basılacak ödülü gösteriyor.
        mint      = $booking.mintAmount
        volunteer = $booking.isVolunteer
        sBakiye   = $ogrenciBakiyeOnce
        tBakiye   = $egitmenBakiyeOnce
        tPuan     = $egitmenPuanOnce
    }
}

# Dersi geçmişe alır (time-lock penceresini açar).
function ShiftToPast($sessionId) {
    $q = @"
UPDATE scheduling."LessonSessions"
SET "ScheduledStartUtc" = now() - interval '2 hours',
    "ScheduledEndUtc"   = now() - interval '1 hour'
WHERE "Id" = '$sessionId';
"@
    Sql $q | Out-Null
}

function SessionStatus($sessionId) {
    Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$sessionId';"
}

# ---------------------------------------------------------------- HAZIRLIK
Step 'Hazırlık: admin hesabı + konu'
$topics = Api GET '/api/catalog/topics'
$topicId = ($topics | Where-Object { $_.topic -eq 'Türev' })[0].topicId

$admin = NewUser 'admin' (('9' * 64))
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($admin.userId)';" | Out-Null
# Rol claim'i token'a yazıldığı için yeniden giriş şart.
$adminLogin = Api POST '/api/auth/login' @{ email = $admin.email; password = 'Parola12345'; hwidHash = ('9' * 64) }
$adminT = $adminLogin.accessToken
if ($adminLogin.isAdmin) { OK 'admin hesabı hazır (isAdmin=true)' } else { Fail 'admin bayrağı token''a yansımadı' }

# ---------------------------------------------------------------- A
Step 'A. Ban + HWID ban kaçağı'
# HWID banı KALICIDIR: sabit bir cihaz kimliği kullanılsaydı test yalnızca bir kez
# çalışır, ikinci koşuda kendi banladığı cihazdan giriş yapamazdı. Koşuma özel üret.
$banHwid   = (([Guid]::NewGuid().ToString('N')) * 2).Substring(0, 64)
$cleanHwid = (([Guid]::NewGuid().ToString('N')) * 2).Substring(0, 64)
$otherHwid = (([Guid]::NewGuid().ToString('N')) * 2).Substring(0, 64)

$hileci = NewUser 'hile' $banHwid
$ban = Api POST "/api/admin/users/$($hileci.userId)/ban" @{ reason = 'Sahte kanıt yükleme (test).' } $adminT
OK "kullanıcı banlandı, $($ban.devicesBanned) cihaz ban listesine eklendi"

# Kendi (artık banlı) cihazından: cihaz kontrolü ÖNCE çalışır, DEVICE_BANNED döner.
# İkisi de geçerli engelleme; cihaz kontrolünün önce olması hesap varlığını da sızdırmaz.
$banned = InvokeExpectError { Api POST '/api/auth/login' @{ email = $hileci.email; password = 'Parola12345'; hwidHash = $banHwid } }
if ($banned.code -in @('USER_BANNED', 'DEVICE_BANNED')) { OK "banlı hesap kendi cihazından giremiyor: $($banned.code)" }
else { Fail "beklenen USER_BANNED/DEVICE_BANNED, gelen $($banned.code)" }

# TEMİZ cihazdan: burada cihaz banı devrede değil, dolayısıyla HESAP banının kendi başına
# çalıştığı kanıtlanır (iki mekanizma birbirine bağımlı değil).
$bannedClean = InvokeExpectError { Api POST '/api/auth/login' @{ email = $hileci.email; password = 'Parola12345'; hwidHash = $otherHwid } }
if ($bannedClean.code -eq 'USER_BANNED') { OK 'banlı hesap temiz cihazdan da giremiyor (hesap banı bağımsız çalışıyor)' }
else { Fail "beklenen USER_BANNED, gelen $($bannedClean.code)" }

# Aynı cihazdan YENİ hesap: kayıt olabilir ama giriş HWID banı yüzünden engellenmeli
$yeniHesap = "kacak$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())@test.dev"
$reg2 = Api POST '/api/auth/register' @{ email = $yeniHesap; password = 'Parola12345'; displayName = 'Kacak Deneme'; termsVersion = '2026-08-27'; ageConfirmed = $true }
Api POST '/api/auth/verify-email' @{ token = $reg2.verificationToken } | Out-Null
$evasion = InvokeExpectError { Api POST '/api/auth/login' @{ email = $yeniHesap; password = 'Parola12345'; hwidHash = $banHwid } }
if ($evasion.code -eq 'DEVICE_BANNED') { OK 'aynı cihazdan açılan yeni hesabın girişi engellendi (ban kaçağı kapalı)' }
else { Fail "beklenen DEVICE_BANNED, gelen $($evasion.code)" }

# Farklı cihazdan aynı hesap girebilmeli (ban cihaza bağlı, hesap temiz)
$clean = Api POST '/api/auth/login' @{ email = $yeniHesap; password = 'Parola12345'; hwidHash = $cleanHwid }
if ($clean.accessToken) { OK 'temiz cihazdan giriş çalışıyor (ban aşırı geniş değil)' } else { Fail 'temiz cihaz girişi başarısız' }

# ---------------------------------------------------------------- F
Step 'B. Ekonomi değişmezleri'

# ESKİ DEĞİŞMEZ: "transfer bacaklarının toplamı 0" ve "arz = mint − yakım". İkisi de sıfır
# toplamlı modele aitti; ders kazancı artık karşılıksız basım olduğu için her onaylanan
# derste yanlış alarm verirlerdi. YERİNE geçen tek ve daha genel iddia: cüzdanlardaki
# toplam, defterdeki tüm hareketlerin toplamına eşit olmalı — yani hiçbir kredi
# defterde karşılığı olmadan bir cüzdana yazılamaz.
$defter = Sql "SELECT (SELECT COALESCE(SUM(""AvailableBalance""),0) FROM economy.""Wallets"") - (SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"");"
if ($defter -eq '0') { OK 'defter değişmezi: cüzdan toplamı = hareket toplamı' } else { Fail "defter farkı: $defter" }

# Panelin aynı iddiayı kendi tanımıyla ölçmesi: sayı DB'de tutup panelde tutmuyorsa
# hakem yanlış bilgiyle karar veriyor demektir.
$metrics = Api GET '/api/admin/metrics' $null $adminT
if ($metrics.ledgerBalanced) { OK "panel defteri dengede (dolaşımdaki: $($metrics.circulatingCredits))" }
else { Fail "panel ledgerBalanced=false (dolaşımdaki: $($metrics.circulatingCredits), basılan: $($metrics.totalMinted))" }

# Ders başına TEK basım (unique index'in fiilen çalıştığının kanıtı): aynı ders için iki
# LessonEarning satırı görülürse ödül iki kez basılmış demektir.
$ciftBasim = Sql "SELECT COUNT(*) FROM (SELECT ""RelatedSessionId"" FROM economy.""CreditTransactions"" WHERE ""Type"" = 'LessonEarning' GROUP BY ""RelatedSessionId"" HAVING COUNT(*) > 1) x;"
if ($ciftBasim -eq '0') { OK 'hiçbir ders için ikinci basım yok' } else { Fail "$ciftBasim derste çifte basım" }

# Unvan sayacı defterle tutmalı: sayaç basımdan bağımsız artarsa unvanlar hak edilmeden
# yükselir, geri kalırsa hak edilmiş unvan görünmez olur. İkisi de sessiz hatadır.
# ⚠️ DEGISMEZ GENISLEDI (2026-08-29): topluluk katkisi da unvan sayacina giriyor.
# AdminAdjustment disarida — yonetim eliyle unvan dagitilamaz.
$sayacSorgu = @"
SELECT COUNT(*) FROM identity."Users" u
WHERE u."TotalEarnedCredits" <> (
    SELECT COALESCE(SUM(t."Amount"), 0)
    FROM economy."CreditTransactions" t
    JOIN economy."Wallets" w ON w."Id" = t."WalletId"
    WHERE w."UserId" = u."Id" AND t."Type" IN ('LessonEarning', 'CommunityReward'));
"@
$sayacFark = Sql $sayacSorgu
if ($sayacFark -eq '0') { OK 'TotalEarnedCredits = defterdeki LessonEarning + CommunityReward toplami' }
else { Fail "$sayacFark kullanıcıda unvan sayacı defterle tutmuyor" }

$lotCheck = Sql "SELECT COUNT(*) FROM economy.""Wallets"" w WHERE w.""AvailableBalance"" <> (SELECT COALESCE(SUM(l.""RemainingAmount""),0) FROM economy.""CreditLots"" l WHERE l.""WalletId"" = w.""Id"");"
if ($lotCheck -eq '0') { OK 'cüzdan bakiyesi = lot toplamı' } else { Fail "$lotCheck cüzdanda tutarsızlık" }

# ESKİ İDDİA: "bloke bakiye = aktif hold toplamı". Escrow tümüyle kaldırıldı; göç açık
# hold bırakmadı ve yeni hold yazan yol yok. İddia bu yüzden daha sert bir hâle geldi:
# HİÇ aktif hold olmamalı ve bloke bakiye toplamı sıfır olmalı. Sıfırdan sapma, ölü
# sanılan escrow yolunun geri döndüğünün ilk işareti olur.
$aktifHold = Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';"
if ($aktifHold -eq '0') { OK 'açık escrow (Active hold) kalmadı' } else { Fail "$aktifHold aktif hold var (escrow geri döndü)" }

# Bloke kolonu kaldırıldı; iddia "toplam 0" değil "kolon yok" biçimine geçti.
$blokeToplam = Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='Wallets' AND column_name='LockedBalance';"
if ($blokeToplam -eq '0') { OK 'hiçbir cüzdanda bloke bakiye yok' } else { Fail "bloke bakiye toplamı: $blokeToplam" }

# İADE (reversal) KAVRAMI KALDIRILDI. Kolon escrow ile birlikte düşürüldü
# (RemoveEscrowMechanics). İddia artık "kaç iade var" değil "kolon geri gelmiş mi":
# geri gelmesi, sökülen release yolunun bir şekilde diriltildiği anlamına gelirdi.
$reversalKolon = Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='CreditLotConsumptions' AND column_name='IsReversal';"
if ($reversalKolon.Trim() -eq '0') { OK 'iade (reversal) kolonu şemada yok — escrow tamamen söküldü' }
else { Fail 'IsReversal kolonu geri gelmiş' }

# Lot denetimi artık DÜZ: Initial − Remaining == SUM(tüketim). Eskiden iade satırları
# çıkarılıyordu; iade diye bir şey kalmadığı için formül de sadeleşti.
$bozukLot = Sql "SELECT COUNT(*) FROM economy.""CreditLots"" l WHERE l.""InitialAmount"" - l.""RemainingAmount"" <> COALESCE((SELECT SUM(k.""Amount"") FROM economy.""CreditLotConsumptions"" k WHERE k.""CreditLotId"" = l.""Id""), 0);"
if ($bozukLot.Trim() -eq '0') { OK 'her lotta Initial − Remaining = tüketim toplamı' }
else { Fail "$($bozukLot.Trim()) lotta tüketim toplamı tutmuyor" }

# ---------------------------------------------------------------- SONUÇ
Write-Host "`n================================" -ForegroundColor Yellow
if ($script:failures -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI" -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow
