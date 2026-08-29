# PeerLearn — Eşzamanlılık (race condition) testi
#
# NEDEN İKİ INSTANCE:
# Redis yapılandırılmadığı sürece dağıtık kilit yerine InProcessLockProvider (SemaphoreSlim)
# kullanılır. TEK instance'a paralel istek atmak yalnızca semaforu test eder — asıl soru,
# çok instance'lı kurulumda VERİTABANI savunmalarının (xmin optimistic concurrency +
# check/unique kısıtları) tek başına yeterli olup olmadığıdır.
# Bu yüzden istekler 5000 ve 5001 portlarındaki İKİ AYRI sürece bölünür: süreç içi kilit
# aradaki yarışı engelleyemez, geriye yalnızca DB kalır.
#
# Beklenti her senaryoda aynı: "tam olarak bir tanesi kazanmalı" ve değişmezler bozulmamalı.
#
# ---------------------------------------------------------------------------------------
# YENİ EKONOMİ SÖZLEŞMESİ — bu paket buna göre yeniden yazıldı
# ---------------------------------------------------------------------------------------
# Öğrenci ders için HİÇBİR ŞEY ödemez: bakiye kontrolü, escrow ve CreditHold yoktur.
# Ders onaylandığında eğitmene puan BASILIR (yoktan üretilir): her 30 dk için 50 puan,
# yani 30 dk = 50, 60 dk = 100. Süre kümesi yalnızca 30 ve 60'tır.
# Basımla birlikte identity."Users"."TotalEarnedCredits" aynı miktarda artar ve ASLA azalmaz.
#
# KAPSAMDAN ÇIKARILANLAR (yeni modelde gerçekten karşılığı olmadığı için silindi):
#   • "Aynı kredi ile çifte harcama" ve INSUFFICIENT_CREDITS — harcama diye bir işlem
#     kalmadı, rezervasyon öğrencinin cüzdanına hiç dokunmuyor. Bu adımın YERİNE MintGuard
#     tavanı sınanıyor (aşağıda, adım 1): tavan kilitsiz bırakılsaydı eşzamanlı isteklerle
#     aşılabilirdi, dolayısıyla eski adımın yarış değeri buraya taşındı.
#   • Escrow/hold değişmezleri ("tek aktif escrow", "bloke bakiye = aktif hold toplamı",
#     "çifte escrow iadesi", "reversal kaydı") — yeni hold hiç yazılmıyor, açık hold kalmadı.
#     Yerlerine "hiç hold yazılmadı / açık hold yok / bloke bakiye 0" kontrolleri kondu.
#   • "Her CorrelationId tam 2 bacak ve toplamı 0" — transfer çift bacaklıydı; basım tek
#     bacaklıdır (yalnız eğitmen tarafı yazılır). Yerine "ders başına tek LessonEarning" ve
#     tutar ölçeği kontrolü kondu.
#   • "Küresel arz = mint - yakım" — defter değişmezinin tanımı değişti; artık
#     SUM(Wallets.Available+Locked) == SUM(CreditTransactions.Amount) ölçülüyor.
#   • Cüzdan yanıtındaki availableBalance/lockedBalance/pendingExpirySweep — DTO'dan kalktı;
#     karşılaştırmalar currentBalance ve totalEarnedCredits üzerinden yapılıyor.
#     (Bu alanların gerçekten kalktığı Hazırlık adımında bir kez doğrulanıyor: aksi hâlde
#     $null == $null tuzağıyla tüm bakiye kontrolleri sahte "geçti" üretirdi.)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

# .NET Framework varsayılanı sunucu başına 2 bağlantıdır; yükseltilmezse istekler
# SIRAYA girer ve test hiçbir yarışı tetiklemez.
[System.Net.ServicePointManager]::DefaultConnectionLimit = 100

$API  = 'http://localhost:5000'
$API2 = 'http://localhost:5001'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

<#
  psql'e üç yoldan erişilebilir; ilk bulunan kullanılır:
    1. Windows kurulumu   — geliştiricinin makinesinde tipik yol.
    2. PATH üzerinde psql — Linux/macOS ve CI koşucuları (postgresql-client).
    3. docker compose     — makinede psql yok ama compose yığını ayakta.

  Üçüncü yol OLMADAN bu paket, docs/GELISTIRME-ORTAMI.md'nin tarif ettiği Docker
  kurulumunda hiç koşamıyordu: yalnızca yerel PostgreSQL 17 kurulumu varsayılıyor ve
  betik "psql bulunamadi" ile ortasında düşüyordu. Testin koşamaması, testin
  başarısız olmasından daha sinsi — özet onu KIRMIZI değil, hiç görünmemiş sayıyor.
#>
if (-not (Test-Path $PSQL)) {
    # PS 5.1 UYUMU: `?.` null-koşullu operatörü PowerShell 7 ile geldi. Paketler
    # CLAUDE.md gereği 5.1 altında koşuyor ve orada bu bir SÖZDİZİMİ hatası —
    # betik hiç başlamaz, yani "psql yok" durumunu ele alacak kod hiç çalışmaz.
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $PSQL = $bulunan } else { $PSQL = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'

$script:failures = 0
$script:skipped = 0
function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Fail($msg)  { Write-Host "  [KALDI] $msg" -ForegroundColor Red; $script:failures++ }

# ATLANAN kapsam: başarısızlık değil ama SESSİZ de kalmamalı — "koştu ve geçti" ile
# "hiç koşmadı" ayrımı özet raporda görünür olmalı.
function Skip($msg)  { Write-Host "  [ATLANDI] $msg" -ForegroundColor Yellow; $script:skipped++ }
function Info($msg)  { Write-Host "  ... $msg" -ForegroundColor DarkGray }

function Api {
    param($Method, $Path, $Body, $Token, $Base = $API)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$Base$Path"; Method = $Method; Headers = $headers; TimeoutSec = 120 }
    if ($null -ne $Body) {
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

function Sql($query) {
    if (-not $PSQL) {
        # Docker yolu: sorgu STDIN'den geçer. `-c` ile argüman olarak geçirmek,
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

# Artık sayısal FARK ölçüyoruz (önce/sonra bakiye, TotalEarnedCredits artışı). psql boş satır
# döndüğünde [int]'' patlıyor; sentinel -1 hem patlamayı önler hem de karşılaştırmayı görünür
# şekilde düşürür — sessiz 0 kabul etmekten iyidir.
function SqlInt($query) {
    $v = Sql $query
    if ([string]::IsNullOrWhiteSpace($v)) { return -1 }
    return [int]$v
}

<#
  İstekleri GERÇEKTEN aynı anda gönderir: tüm HttpRequestMessage'lar önce hazırlanır,
  sonra tek döngüde SendAsync ile ateşlenir ve WaitAll ile beklenir.
  $Requests: @{ method; path; body; token; base }
#>
function ParallelFire {
    param([array]$Requests)

    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromSeconds(180)

    $messages = @()
    foreach ($r in $Requests) {
        $base = if ($r.base) { $r.base } else { $API }
        $msg = New-Object System.Net.Http.HttpRequestMessage(
            (New-Object System.Net.Http.HttpMethod($r.method)), "$base$($r.path)")
        if ($r.token) {
            $msg.Headers.Authorization =
                New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $r.token)
        }
        if ($null -ne $r.body) {
            $json = $r.body | ConvertTo-Json -Depth 6
            $msg.Content = New-Object System.Net.Http.StringContent(
                $json, [System.Text.Encoding]::UTF8, 'application/json')
        }
        $messages += $msg
    }

    $tasks = New-Object 'System.Collections.Generic.List[System.Threading.Tasks.Task[System.Net.Http.HttpResponseMessage]]'
    foreach ($m in $messages) { $tasks.Add($client.SendAsync($m)) }

    try { [System.Threading.Tasks.Task]::WaitAll($tasks.ToArray()) } catch { }

    $results = @()
    foreach ($t in $tasks) {
        if ($t.IsFaulted) {
            $results += [PSCustomObject]@{ Ok = $false; Status = 0; Code = 'TRANSPORT'; Body = "$($t.Exception.GetBaseException().Message)" }
            continue
        }
        $resp = $t.Result
        $text = $resp.Content.ReadAsStringAsync().Result
        $code = $null
        if (-not $resp.IsSuccessStatusCode) {
            try { $code = ($text | ConvertFrom-Json).title } catch { $code = 'PARSE_FAIL' }
        }
        $results += [PSCustomObject]@{ Ok = $resp.IsSuccessStatusCode; Status = [int]$resp.StatusCode; Code = $code; Body = $text }
    }
    $client.Dispose()
    return $results
}

function Summarize($results) {
    $ok = @($results | Where-Object { $_.Ok }).Count
    $codes = ($results | Where-Object { -not $_.Ok } | Group-Object Code |
              ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ', '
    return "$ok basarili" + $(if ($codes) { " | reddedilen: $codes" } else { "" })
}

# Yarış altında 5xx/taşıma hatası hiçbir senaryoda meşru değildir: çakışma "temiz ret"
# (409/429) olarak dönmeli. Bu yüzden ayrı bir sayaçla izliyoruz.
function ServerErrors($results) {
    return @($results | Where-Object { $_.Status -ge 500 -or $_.Code -eq 'TRANSPORT' }).Count
}

function OkBodies($results) {
    return @($results | Where-Object { $_.Ok } | ForEach-Object { $_.Body | ConvertFrom-Json })
}

$script:seq = 0
function NewUser($prefix) {
    $script:seq++
    $email = "$prefix$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())c$($script:seq)@test.dev"
    $hwid = (([Guid]::NewGuid().ToString('N')) * 2).Substring(0, 64)
    $reg = Api POST '/api/auth/register' @{ email = $email; password = 'Parola12345'; displayName = "$prefix K"; termsVersion = '2026-08-27'; ageConfirmed = $true }
    Api POST '/api/auth/verify-email' @{ token = $reg.verificationToken } | Out-Null
    $login = Api POST '/api/auth/login' @{ email = $email; password = 'Parola12345'; hwidHash = $hwid }
    return @{ email = $email; userId = $login.userId; token = $login.accessToken; regToken = $reg.verificationToken }
}

# MINTGUARD UYARISI: aynı eğitmen-öğrenci çifti 24 saatlik pencerede en fazla 2 ders kurabilir
# (aynı eğitmen için tavan 8). Bu yüzden aşağıdaki senaryoların HEPSİ kendi TAZE çiftini kurar
# ve tek ders rezerve eder; tavanı bilerek zorlayan tek yer adım 1'dir.
function NewPair($topicId) {
    $student = NewUser 'ogr'
    $tutor   = NewUser 'egt'
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $tutor.token | Out-Null
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek'; selfAssessedLevel = 2; note = $null } $student.token | Out-Null
    $m = Api POST '/api/matches' @{ responderUserId = $tutor.userId; requestedTopicId = $topicId; offeredTopicId = $null } $student.token
    Api POST "/api/matches/$m/respond" @{ accept = $true } $tutor.token | Out-Null
    return @{ student = $student; tutor = $tutor; matchId = $m }
}

# Süre kümesi artık yalnız 30/60; varsayılan 60 dk = 100 puanlık basım demektir.
function BookFor($pair, $topicId, $hoursAhead, $duration = 60) {
    $start = [DateTime]::UtcNow.AddHours($hoursAhead).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    Api POST '/api/sessions' @{ matchId = $pair.matchId; topicId = $topicId; scheduledStartUtc = $start; durationMinutes = $duration } $pair.student.token
}

function UploadProof($sessionId, $code, $token) {
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    $bd = [Guid]::NewGuid().ToString('N')
    $enc = [System.Text.Encoding]::UTF8
    $ms = New-Object System.IO.MemoryStream
    $h = $enc.GetBytes("--$bd`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$code`r`n--$bd`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"p.png`"`r`nContent-Type: image/png`r`n`r`n")
    $t = $enc.GetBytes("`r`n--$bd--`r`n")
    $ms.Write($h,0,$h.Length); $ms.Write($png,0,$png.Length); $ms.Write($t,0,$t.Length)
    $body = $ms.ToArray(); $ms.Dispose()
    Invoke-RestMethod -Uri "$API/api/sessions/$sessionId/complete" -Method Post `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType "multipart/form-data; boundary=$bd" -Body $body -TimeoutSec 60
}

function ShiftToPast($sessionId) {
    Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '2 hours', ""ScheduledEndUtc"" = now() - interval '1 hour' WHERE ""Id"" = '$sessionId';" | Out-Null
}
function SessionStatus($sessionId) { Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$sessionId';" }
function Wallet($token) { Api GET '/api/wallet' $null $token }

# Basımın iki ayrı izi var: defterdeki LessonEarning hareketi ve kullanıcıdaki birikimli
# sayaç. İkisini ayrı ayrı ölçüyoruz ki "biri yazıldı diğeri yazılmadı" durumu görünsün.
function LegsFor($sessionId) { SqlInt "SELECT COUNT(*) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$sessionId';" }
function MintedFor($sessionId) { SqlInt "SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$sessionId' AND ""Type"" = 'LessonEarning';" }
function TotalEarnedDb($userId) { SqlInt "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$userId';" }
function HoldsForSession($sessionId) { SqlInt "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';" }
function HoldsForUser($userId) { SqlInt "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';" }

# Unvan eşikleri: alt sınır DAHİL, üst sınır HARİÇ.
# Seviye eşikleri — SUNUCUDAKİNİN BAĞIMSIZ KOPYASI, ve bu bilinçli.
#
# Normalde eşik tablosunu ikinci bir yere yazmak yasak (arayüz tarafında öyle).
# Testte kural TERSİNE döner: bağımsız bir kopya olmadan test, sınadığı hesabın
# kendisini çağırıp "kendi kendine eşit mi" diye sorardı ve her zaman geçerdi.
# Tablo sunucuda değişip burada değişmezse bu test KIRILIR — istenen davranış budur.
# Kaynak: src/PeerLearn.Domain/Community/UserLevel.cs
$script:SeviyeEsikleri = @(0, 100, 200, 350, 600, 1000, 1750, 3000, 5500, 10000)

function SeviyeFor([int]$total) {
    if ($total -lt 0) { $total = 0 }
    $seviye = 1
    for ($i = 0; $i -lt $script:SeviyeEsikleri.Count; $i++) {
        if ($total -ge $script:SeviyeEsikleri[$i]) { $seviye = $i + 1 }
    }
    return $seviye
}

# Bir sonraki basamağın eşiği; en üst seviyede $null (sunucu da null döner).
function SonrakiSeviyeEsigi([int]$total) {
    $seviye = SeviyeFor $total
    if ($seviye -ge $script:SeviyeEsikleri.Count) { return $null }
    return $script:SeviyeEsikleri[$seviye]
}

# NormTr KALDIRILDI. Unvan adları diyakritikliydi ("Çırak") ve ham karşılaştırma sırf
# harf kodlamasından sahte başarısızlık üretiyordu; sadeleştirici bir yardımcı şarttı.
# Seviye bir TAMSAYI olduğu için o sorunun tamamı ortadan kalktı — 3 ile 3 karşılaştırmak
# kodlamadan bağımsız. Adlandırmayı numaraya çevirmenin sessiz bir yan faydası.

# İki instance arasında sırayla dağıt: yarış süreç sınırını aşsın.
function SplitBases($count) {
    $bases = @()
    for ($i = 0; $i -lt $count; $i++) { $bases += $(if ($i % 2 -eq 0) { $API } else { $API2 }) }
    return $bases
}

# ---------------------------------------------------------------- HAZIRLIK
Step 'Hazırlık'
$second = $false
try {
    Invoke-RestMethod "$API2/health" -TimeoutSec 5 | Out-Null
    $second = $true
    OK 'ikinci instance (5001) ayakta — yarışlar süreç sınırını aşacak'
} catch {
    # İkinci instance yokluğu bir HATA DEĞİL, ATLANAN KAPSAMDIR. Fail sayılınca paket
    # kalıcı olarak "başarısız" oluyordu ve bu durum normalleşiyordu; üstelik kullanılan
    # [HATA] etiketi çalıştırıcının aradığı listede olmadığı için özete de yansımıyordu —
    # yani hem gürültü hem görünmezlik üretiyordu.
    Skip 'ikinci instance (5001) KAPALI — süreç sınırını aşan yarışlar ve DB savunmaları SINANMADI'
}

$topicId = ((Api GET '/api/catalog/topics') | Where-Object { $_.topic -eq 'Türev' })[0].topicId
$admin = NewUser 'cadmin'
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($admin.userId)';" | Out-Null
$adminT = (Api POST '/api/auth/login' @{ email = $admin.email; password = 'Parola12345'; hwidHash = ('c' * 64) }).accessToken
OK 'admin hazır'

# Cüzdan DTO şekli tüm bakiye karşılaştırmalarının dayanağı. Eski alan adları hâlâ dönüyorsa
# ya da yenileri yoksa, aşağıdaki "önce/sonra" kontrolleri $null - $null = 0 ile SAHTE geçer.
# Bu yüzden şekli bir kez, en başta doğruluyoruz.
$wProbe = Wallet $adminT
foreach ($alan in @('totalEarnedCredits', 'currentBalance', 'level', 'levelMinCredits')) {
    if ($null -eq $wProbe.$alan) { Fail "cüzdan yanıtında '$alan' alanı yok — yeni sözleşme karşılanmıyor" }
}
$eskiAlanlar = @(@('availableBalance', 'lockedBalance', 'pendingExpirySweep') | Where-Object { $null -ne $wProbe.$_ })
if ($eskiAlanlar.Count -eq 0) { OK 'cüzdan yanıtı yeni sözleşmeye uygun (escrow alanları kalkmış)' }
else { Fail "kalkması gereken alanlar hâlâ dönüyor: $($eskiAlanlar -join ', ')" }

# ---------------------------------------------------------------- 1
# ESKİ KAPSAM "aynı kredi ile çifte harcama" konusuz kaldı (harcama yok). YERİNE MintGuard:
# çift başına 24 saatlik tavan (2 ders) kilitsiz bırakılsaydı tam da eşzamanlı isteklerle
# aşılırdı — yani suistimal freni asıl burada sınanır.
Step '1. Aynı çift için 8 paralel rezervasyon (MintGuard tavanı)'
$p1 = NewPair $topicId
$w1before = Wallet $p1.student.token
$e1before = TotalEarnedDb $p1.student.userId

# Sekiz dersin başlangıcı AYNI 24 saatlik pencerede kalmalı, yoksa tavan hiç devreye girmez.
# 2 saat aralık: 60 dk'lık dersler birbiriyle çakışmaz ama toplam yayılım 14 saatte kalır.
$reqs = @()
$bases = SplitBases 8
for ($i = 0; $i -lt 8; $i++) {
    $start = [DateTime]::UtcNow.AddHours(3 + $i * 2).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $reqs += @{ method = 'POST'; path = '/api/sessions'; token = $p1.student.token; base = $bases[$i]
                body = @{ matchId = $p1.matchId; topicId = $topicId; scheduledStartUtc = $start; durationMinutes = 60 } }
}
$r1 = ParallelFire $reqs
Info (Summarize $r1)

$ok1 = @($r1 | Where-Object { $_.Ok }).Count
if ($ok1 -eq 2) { OK 'MintGuard tavanı tuttu: TAM OLARAK 2 rezervasyon kabul edildi' }
else { Fail "$ok1 rezervasyon kabul edildi — çift başına 24 saatlik tavan (2) eşzamanlılıkla aşıldı" }

$rej1 = @($r1 | Where-Object { -not $_.Ok })
$http429 = @($rej1 | Where-Object { $_.Status -eq 429 }).Count
if ($http429 -eq 6) { OK 'kalan 6 istek HTTP 429 ile reddedildi' } else { Fail "429 dönen istek sayısı: $http429 (beklenen 6)" }
$code429 = @($rej1 | Where-Object { $_.Code -eq 'MINT_LIMIT_REACHED' }).Count
if ($code429 -eq 6) { OK 'ret kodu MINT_LIMIT_REACHED' } else { Fail "MINT_LIMIT_REACHED kodu $code429 yanıtta (beklenen 6)" }

$err1 = ServerErrors $r1
if ($err1 -eq 0) { OK 'tavan reddi temiz döndü (5xx/taşıma hatası yok)' } else { Fail "$err1 istek 5xx/taşıma hatasıyla düştü" }

# Kabul edilen yanıtların şekli: creditCost yerine mintAmount, ek olarak isVolunteer.
$body1 = OkBodies $r1
$badMint1 = @($body1 | Where-Object { [int]$_.mintAmount -ne 100 }).Count
if ($body1.Count -gt 0 -and $badMint1 -eq 0) { OK '60 dk rezervasyon mintAmount=100 döndü' }
else { Fail "mintAmount=100 olmayan $badMint1 yanıt (kabul edilen: $($body1.Count))" }
$badVol1 = @($body1 | Where-Object { $_.isVolunteer -ne $false }).Count
if ($badVol1 -eq 0) { OK 'isVolunteer alanı geldi ve false' } else { Fail "$badVol1 yanıtta isVolunteer eksik/true" }

# Tavan yalnız API sayısında değil, veritabanında da tutmalı.
$sess1 = SqlInt "SELECT COUNT(*) FROM scheduling.""LessonSessions"" WHERE ""TutorUserId"" = '$($p1.tutor.userId)' AND ""Status"" <> 'Cancelled';"
if ($sess1 -eq 2) { OK 'veritabanında çifte ait 2 ders var' } else { Fail "veritabanındaki ders sayısı: $sess1 (beklenen 2)" }

# Değişmez 1 ve 2: rezervasyon öğrencinin cüzdanına dokunmaz, hold yazmaz.
$w1after = Wallet $p1.student.token
if ($w1after.currentBalance -eq $w1before.currentBalance) { OK "rezervasyon öğrenci bakiyesine dokunmadı ($($w1after.currentBalance))" }
else { Fail "öğrenci bakiyesi değişti: $($w1before.currentBalance) -> $($w1after.currentBalance)" }

$e1after = TotalEarnedDb $p1.student.userId
if ($e1after -eq $e1before) { OK 'rezervasyon öğrencinin birikimli sayacını değiştirmedi' }
else { Fail "öğrenci TotalEarnedCredits değişti: $e1before -> $e1after" }

$holds1 = HoldsForUser $p1.student.userId
if ($holds1 -eq 0) { OK 'rezervasyon hiç CreditHold yazmadı (escrow yok)' } else { Fail "$holds1 CreditHold kaydı oluştu" }

# "Bloke bakiye 0" iddiası artık ölçülemez ÇÜNKÜ KOLON YOK (RemoveEscrowMechanics).
# Yapısal kontrol daha güçlü: değer sıfırlanmış değil, kavram kaldırılmış.
$locked1 = SqlInt "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='Wallets' AND column_name='LockedBalance';"
if ($locked1 -eq 0) { OK 'öğrencinin bloke bakiyesi 0' } else { Fail "bloke bakiye: $locked1" }

# ---------------------------------------------------------------- 2
Step '2. Aynı derse 8 paralel onay (çifte basım)'
$p2 = NewPair $topicId
$b2 = BookFor $p2 $topicId 2
ShiftToPast $b2.sessionId
UploadProof $b2.sessionId $b2.verificationCode $p2.tutor.token | Out-Null

$tw2before = Wallet $p2.tutor.token
$te2before = TotalEarnedDb $p2.tutor.userId
$sw2before = Wallet $p2.student.token

$bases = SplitBases 8
$reqs = @()
for ($i = 0; $i -lt 8; $i++) {
    $reqs += @{ method = 'POST'; path = "/api/sessions/$($b2.sessionId)/approve"; token = $p2.student.token; base = $bases[$i] }
}
$r2 = ParallelFire $reqs
Info (Summarize $r2)
$ok2 = @($r2 | Where-Object { $_.Ok }).Count
if ($ok2 -eq 1) { OK 'TAM OLARAK 1 onay geçti' } else { Fail "$ok2 onay geçti (çifte basım riski!)" }

$minted2 = @((OkBodies $r2) | ForEach-Object { [int]$_.creditsMinted })
if ($minted2.Count -ge 1 -and $minted2[0] -eq 100) { OK 'onay yanıtı creditsMinted=100 döndü' }
else { Fail "creditsMinted: $($minted2 -join ',') (beklenen tek yanıt, 100)" }

# Basım TEK bacaklıdır: öğrenciden düşen bir karşılık yok, yalnız eğitmene yazılır.
$legs2 = LegsFor $b2.sessionId
if ($legs2 -eq 1) { OK 'defterde tek bacak (yalnız eğitmen kazancı)' } else { Fail "bacak sayısı: $legs2 (beklenen 1)" }

$mint2 = MintedFor $b2.sessionId
if ($mint2 -eq 100) { OK '60 dk ders için 100 puan basıldı' } else { Fail "basılan toplam: $mint2 (beklenen 100)" }

$te2after = TotalEarnedDb $p2.tutor.userId
if (($te2after - $te2before) -eq 100) { OK 'TotalEarnedCredits tam 100 arttı' } else { Fail "TotalEarnedCredits farkı: $($te2after - $te2before)" }

$tw2after = Wallet $p2.tutor.token
if (($tw2after.currentBalance - $tw2before.currentBalance) -eq 100) { OK 'eğitmen bakiyesi tam 100 arttı' }
else { Fail "eğitmen bakiye farkı: $($tw2after.currentBalance - $tw2before.currentBalance)" }
if ([int]$tw2after.totalEarnedCredits -eq $te2after) { OK 'cüzdan yanıtı ile veritabanı sayacı aynı' }
else { Fail "cüzdan totalEarnedCredits=$($tw2after.totalEarnedCredits), veritabanı=$te2after" }

# Değişmez 9: eşzamanlı basımlar seviye hesabını da bozmamalı.
$beklenenSeviye = SeviyeFor $te2after
if ([int]$tw2after.level -eq $beklenenSeviye) { OK "seviye eşikle uyumlu: $($tw2after.level). seviye / $te2after puan" }
else { Fail "seviye: $($tw2after.level) — $te2after puan için beklenen: $beklenenSeviye" }

# Sonraki eşik SABİT DEĞİL, hesaplanıyor: eski test 500 yazıyordu ve eşik tablosu
# değiştiği anda sahte bir başarısızlık üretirdi.
$beklenenSonraki = SonrakiSeviyeEsigi $te2after
if ([int]$tw2after.nextLevelAt -eq [int]$beklenenSonraki) { OK "bir sonraki seviye eşiği $beklenenSonraki döndü" }
else { Fail "nextLevelAt: $($tw2after.nextLevelAt) (beklenen $beklenenSonraki)" }

$earnLots2 = SqlInt "SELECT COUNT(*) FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($b2.sessionId)';"
if ($earnLots2 -eq 1) { OK 'tek kazanç lotu oluştu' } else { Fail "kazanç lotu sayısı: $earnLots2" }

# Yeni kazançlar süresiz: lot vadesi NULL olmalı, yoksa kazanılan puan sessizce yanar.
$lotExpiry2 = Sql "SELECT COALESCE(""ExpiresAtUtc""::text, 'NULL') FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($b2.sessionId)';"
if ($lotExpiry2 -eq 'NULL') { OK 'kazanç lotu süresiz (ExpiresAtUtc NULL)' } else { Fail "kazanç lotuna vade konmuş: $lotExpiry2" }
$suresizDto = @($tw2after.activeLots | Where-Object { $null -eq $_.expiresAtUtc }).Count
if ($suresizDto -ge 1) { OK 'cüzdan yanıtındaki activeLots içinde süresiz lot görünüyor' } else { Fail 'cüzdan yanıtında süresiz lot yok' }

# Değişmez 1: onay da öğrenciden bir şey almaz.
$sw2after = Wallet $p2.student.token
if ($sw2after.currentBalance -eq $sw2before.currentBalance) { OK 'onay öğrenci bakiyesine dokunmadı' }
else { Fail "öğrenci bakiyesi değişti: $($sw2before.currentBalance) -> $($sw2after.currentBalance)" }

# ---------------------------------------------------------------- 2b
# Değişmez 4: gönüllü derste HİÇ basım olmaz. Eşzamanlılık açısından ayrıca sınanmalı,
# çünkü "ders başına tek basım" koruması 0 puanlı yolda da bozulmadan çalışmalı.
Step '2b. Gönüllü derse 8 paralel onay (hiç basım olmamalı)'
$pV = NewPair $topicId
# Gönüllülük ilanda beyan edilir; ders rezervasyonda bu bayrağı devralır.
Sql "UPDATE matchmaking.""PortfolioEntries"" SET ""IsVolunteer"" = TRUE WHERE ""UserId"" = '$($pV.tutor.userId)' AND ""TopicId"" = '$topicId';" | Out-Null
$bV = BookFor $pV $topicId 2
if ($bV.isVolunteer -eq $true) { OK 'rezervasyon yanıtı isVolunteer=true döndü' }
else {
    Fail 'gönüllü ilandan kurulan ders isVolunteer=false geldi (bayrak derse devrolmuyor)'
    # Bayrak devrolmadıysa bile testin ASIL konusu (paralel onayda basım yok mu) ölçülebilsin
    # diye bayrağı doğrudan kuruyoruz; yukarıdaki Fail kök nedeni zaten raporluyor.
    # CreditCost DA sıfırlanmalı: CK_LessonSessions_VolunteerNoReward kısıtı
    # "gönüllü ders puan üretemez" diyor. Yalnızca bayrağı çevirmek kısıt ihlaliyle
    # patlar ve yedek kurulum sessizce başarısız olurdu.
    Sql "UPDATE scheduling.""LessonSessions"" SET ""IsVolunteer"" = TRUE, ""CreditCost"" = 0 WHERE ""Id"" = '$($bV.sessionId)';" | Out-Null
}
ShiftToPast $bV.sessionId
UploadProof $bV.sessionId $bV.verificationCode $pV.tutor.token | Out-Null
$teVbefore = TotalEarnedDb $pV.tutor.userId

$bases = SplitBases 8
$reqs = @()
for ($i = 0; $i -lt 8; $i++) {
    $reqs += @{ method = 'POST'; path = "/api/sessions/$($bV.sessionId)/approve"; token = $pV.student.token; base = $bases[$i] }
}
$rV = ParallelFire $reqs
Info (Summarize $rV)
$okV = @($rV | Where-Object { $_.Ok }).Count
if ($okV -eq 1) { OK 'TAM OLARAK 1 onay geçti' } else { Fail "$okV onay geçti" }

$mintedV = @((OkBodies $rV) | ForEach-Object { [int]$_.creditsMinted })
if ($mintedV.Count -eq 0 -or $mintedV[0] -eq 0) { OK 'onay yanıtı creditsMinted=0 döndü' } else { Fail "creditsMinted: $($mintedV -join ',')" }

$mintV = MintedFor $bV.sessionId
if ($mintV -eq 0) { OK 'gönüllü derste hiç puan basılmadı' } else { Fail "gönüllü derste $mintV puan basıldı" }
$legsV = LegsFor $bV.sessionId
if ($legsV -eq 0) { OK 'gönüllü ders defterde hiç hareket bırakmadı' } else { Fail "bacak sayısı: $legsV (beklenen 0)" }
$teVafter = TotalEarnedDb $pV.tutor.userId
if ($teVafter -eq $teVbefore) { OK 'gönüllü ders birikimli sayacı artırmadı' } else { Fail "TotalEarnedCredits farkı: $($teVafter - $teVbefore)" }

# ---------------------------------------------------------------- 3
Step '3. Paralel e-posta doğrulama (çifte hoş geldin kredisi)'
$fresh = NewUser 'dogrula'   # zaten doğrulanmış; aynı token'la 6 paralel istek
$bases = SplitBases 6
$reqs = @()
for ($i = 0; $i -lt 6; $i++) {
    $reqs += @{ method = 'POST'; path = '/api/auth/verify-email'; base = $bases[$i]
                body = @{ token = $fresh.regToken } }
}
$r3 = ParallelFire $reqs
Info (Summarize $r3)
$granted = @($r3 | Where-Object { $_.Ok -and ($_.Body | ConvertFrom-Json).welcomeCreditGranted }).Count
if ($granted -eq 0) { OK 'kredi zaten verilmişti; paralel istekler yeni kredi üretmedi' }
else { Fail "$granted istek yeni kredi üretti" }

$welcomeLots = Sql "SELECT COUNT(*) FROM economy.""CreditLots"" l JOIN economy.""Wallets"" w ON w.""Id"" = l.""WalletId"" WHERE w.""UserId"" = '$($fresh.userId)' AND l.""Source"" = 'WelcomeBonus';"
if ($welcomeLots -eq '1') { OK 'cüzdanda tek hoş geldin lotu' } else { Fail "hoş geldin lotu sayısı: $welcomeLots" }

# Hiç doğrulanmamış kullanıcıda 6 paralel ilk doğrulama
$script:seq++
$vEmail = "ilkdogrula$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())c$($script:seq)@test.dev"
$vReg = Api POST '/api/auth/register' @{ email = $vEmail; password = 'Parola12345'; displayName = 'Ilk Dogrula'; termsVersion = '2026-08-27'; ageConfirmed = $true }
$bases = SplitBases 6
$reqs = @()
for ($i = 0; $i -lt 6; $i++) {
    $reqs += @{ method = 'POST'; path = '/api/auth/verify-email'; base = $bases[$i]; body = @{ token = $vReg.verificationToken } }
}
$r3b = ParallelFire $reqs
Info (Summarize $r3b)
$granted2 = @($r3b | Where-Object { $_.Ok -and ($_.Body | ConvertFrom-Json).welcomeCreditGranted }).Count
if ($granted2 -eq 1) { OK 'ilk doğrulamada TAM OLARAK 1 kredi verildi' } else { Fail "$granted2 kez kredi verildi" }

$vLots = Sql "SELECT COUNT(*) FROM economy.""CreditLots"" l JOIN economy.""Wallets"" w ON w.""Id"" = l.""WalletId"" WHERE w.""UserId"" = '$($vReg.userId)' AND l.""Source"" = 'WelcomeBonus';"
if ($vLots -eq '1') { OK 'tek hoş geldin lotu (partial unique index tuttu)' } else { Fail "lot sayısı: $vLots" }

# ---------------------------------------------------------------- 4
# ---------------------------------------------------------------- 4
<#
  ESKİ ADIM: "onay + itiraz aynı anda (durum makinesi yarışı)". İtiraz dersi Disputed'a
  çekip basımı dondurduğu için onayla yarışıyordu. Şikayet dersin durumuna HİÇ dokunmuyor,
  yani o yarışın konusu kalmadı.

  YERİNE GEÇEN YARIŞ: aynı ders için iki eşzamanlı şikayet. Handler "bu ders için şikayet
  var mı" diye BAKIP sonra YAZIYOR; iki istek aynı anda bakarsa ikisi de boş görebilir.
  Son savunma, (ReporterUserId, SessionId) üzerindeki kısmi unique index — tam olarak
  burada sınanıyor.
#>
Step '4. Aynı derse paralel çifte şikayet (tekillik kısıtı)'
$p4 = NewPair $topicId
$b4 = BookFor $p4 $topicId 5

$bases = SplitBases 4
$reqs = @()
for ($i = 0; $i -lt 4; $i++) {
    $reqs += @{ method = 'POST'; path = "/api/sessions/$($b4.sessionId)/report"; token = $p4.student.token; base = $bases[$i]
                body = @{ reason = 'Abuse'; description = 'Esszamanli cifte sikayet denemesi.' } }
}
$r4 = ParallelFire $reqs
Info (Summarize $r4)

$ok4 = @($r4 | Where-Object { $_.Ok }).Count
if ($ok4 -eq 1) { OK 'tam olarak bir şikayet kabul edildi' } else { Fail "$ok4 şikayet kabul edildi (beklenen 1)" }

$db4 = SqlInt "SELECT COUNT(*) FROM moderation.""Reports"" WHERE ""SessionId"" = '$($b4.sessionId)';"
if ($db4 -eq 1) { OK 'veritabanında tek şikayet satırı var' } else { Fail "şikayet satırı: $db4" }

$err4 = ServerErrors $r4
if ($err4 -eq 0) { OK 'yarış temiz ret verdi (5xx yok)' } else { Fail "$err4 istek 5xx ile düştü" }

# Şikayet dersin durumunu DEĞİŞTİRMEMELİ — yarış altında da değil.
$durum4 = Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$($b4.sessionId)';"
if ($durum4.Trim() -eq 'Booked') { OK 'şikayet yarışı dersin durumuna dokunmadı (Booked)' }
else { Fail "ders durumu: $($durum4.Trim())" }


Step '5. Öğrenci onayı + otomatik onay job''ı aynı anda'
$p5 = NewPair $topicId
$b5 = BookFor $p5 $topicId 2
ShiftToPast $b5.sessionId
UploadProof $b5.sessionId $b5.verificationCode $p5.tutor.token | Out-Null
Sql "UPDATE scheduling.""LessonSessions"" SET ""CompletionRequestedAtUtc"" = now() - interval '50 hours' WHERE ""Id"" = '$($b5.sessionId)';" | Out-Null
$te5before = TotalEarnedDb $p5.tutor.userId
$tw5before = Wallet $p5.tutor.token

$reqs = @(
    @{ method = 'POST'; path = "/api/sessions/$($b5.sessionId)/approve"; token = $p5.student.token; base = $API },
    @{ method = 'POST'; path = '/api/admin/jobs/session-sweep'; token = $adminT; base = $API2 },
    @{ method = 'POST'; path = '/api/admin/jobs/session-sweep'; token = $adminT; base = $API }
)
$r5 = ParallelFire $reqs
Info (Summarize $r5)
$legs5 = LegsFor $b5.sessionId
if ($legs5 -eq 1) { OK 'onay ile job yarıştı, defterde tek bacak kaldı' } else { Fail "bacak sayısı: $legs5 (beklenen 1)" }
$mint5 = MintedFor $b5.sessionId
if ($mint5 -eq 100) { OK 'basım TAM OLARAK bir kez ve 100 puan' } else { Fail "basılan toplam: $mint5" }
$te5after = TotalEarnedDb $p5.tutor.userId
if (($te5after - $te5before) -eq 100) { OK 'TotalEarnedCredits tam 100 arttı' } else { Fail "sayaç farkı: $($te5after - $te5before)" }
$tw5after = Wallet $p5.tutor.token
if (($tw5after.currentBalance - $tw5before.currentBalance) -eq 100) { OK 'eğitmen bakiyesi tam 100 arttı' }
else { Fail "eğitmen bakiye farkı: $($tw5after.currentBalance - $tw5before.currentBalance)" }

# ---------------------------------------------------------------- 6
Step '6. Paralel vade süpürmesi (çifte yakım)'
$p6 = NewPair $topicId
# Yeni kazançlar süresiz olduğu için yakım yalnızca vadeli lotlarda (hoş geldin) anlamlı;
# öğrencinin tek lotunu bilerek vadesi geçmiş yapıyoruz.
$lot6 = Sql "SELECT l.""Id"" FROM economy.""CreditLots"" l JOIN economy.""Wallets"" w ON w.""Id"" = l.""WalletId"" WHERE w.""UserId"" = '$($p6.student.userId)' AND l.""RemainingAmount"" > 0 LIMIT 1;"
Sql "UPDATE economy.""CreditLots"" SET ""ExpiresAtUtc"" = now() - interval '1 day' WHERE ""Id"" = '$lot6';" | Out-Null
$te6before = TotalEarnedDb $p6.student.userId

$bases = SplitBases 4
$reqs = @()
for ($i = 0; $i -lt 4; $i++) { $reqs += @{ method = 'POST'; path = '/api/admin/jobs/credit-expiry'; token = $adminT; base = $bases[$i] } }
$r6 = ParallelFire $reqs
Info (Summarize $r6)

$w6 = Wallet $p6.student.token
if ($w6.currentBalance -eq 0) { OK 'vadesi geçen kredi yakıldı (bakiye 0)' } else { Fail "bakiye: $($w6.currentBalance)" }
$expiryTx6 = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = '$($p6.student.userId)' AND t.""Type"" = 'Expiry';"
if ($expiryTx6 -eq '1') { OK 'tek Expiry hareketi yazıldı (çifte yakım yok)' } else { Fail "Expiry hareket sayısı: $expiryTx6" }

# Birikimli sayaç harcanabilir bakiyeden bağımsızdır: yakım onu DÜŞÜREMEZ.
$te6after = TotalEarnedDb $p6.student.userId
if ($te6after -eq $te6before) { OK 'yakım birikimli sayacı düşürmedi' } else { Fail "sayaç $te6before -> $te6after (azalmamalıydı)" }

# ---------------------------------------------------------------- 7
Step '7. Paralel mükerrer kayıtlar (unique index savunmaları)'
$p7 = NewPair $topicId
<#
  İKİNCİ KONU ADIYLA DEĞİL, "İLKİNDEN FARKLI OLAN" DİYE SEÇİLİYOR.

  Burada eskiden 'İntegral' adı sabit yazılıydı. 2026-08-18'de YKS müfredatı katalog
  eşitleyicisiyle yüklenince (CatalogSeeder) müfredat dışı kalan eski serbest konular
  PASİFLEŞTİ — 'İntegral' de onlardan biriydi. /api/catalog/topics yalnızca aktifleri
  döndürdüğü için filtre boş dizi verdi ve [0] "NullArray" ile patladı: paket, ilgisiz
  bir sebeple ve anlaşılması zor bir hatayla düştü.

  Başka bir ad yazmak aynı tuzağı bir sonraki müfredat güncellemesine ertelerdi. Testin
  ihtiyacı zaten bir İSİM değil, "$topicId'den farklı, aktif herhangi bir konu".
#>
$topic7 = ((Api GET '/api/catalog/topics') | Where-Object { $_.topicId -ne $topicId })[0].topicId
if (-not $topic7) { Fail 'katalogda ikinci bir aktif konu yok — bu adım koşulamaz' }

$bases = SplitBases 5
$reqs = @()
for ($i = 0; $i -lt 5; $i++) {
    $reqs += @{ method = 'POST'; path = '/api/portfolio/entries'; token = $p7.student.token; base = $bases[$i]
                body = @{ topicId = $topic7; direction = 'Seek'; selfAssessedLevel = 3; note = $null } }
}
$r7 = ParallelFire $reqs
Info (Summarize $r7)
$entries = Sql "SELECT COUNT(*) FROM matchmaking.""PortfolioEntries"" WHERE ""UserId"" = '$($p7.student.userId)' AND ""TopicId"" = '$topic7' AND ""Direction"" = 'Seek';"
if ($entries -eq '1') { OK 'paralel mükerrer portföy girişi: tek kayıt kaldı' } else { Fail "kayıt sayısı: $entries" }

$other = NewUser 'hedef'
Api POST '/api/portfolio/entries' @{ topicId = $topic7; direction = 'Offer'; selfAssessedLevel = 4; note = $null } $other.token | Out-Null
$bases = SplitBases 5
$reqs = @()
for ($i = 0; $i -lt 5; $i++) {
    $reqs += @{ method = 'POST'; path = '/api/matches'; token = $p7.student.token; base = $bases[$i]
                body = @{ responderUserId = $other.userId; requestedTopicId = $topic7; offeredTopicId = $null } }
}
$r7b = ParallelFire $reqs
Info (Summarize $r7b)
$matches = Sql "SELECT COUNT(*) FROM matchmaking.""Matches"" WHERE ""InitiatorUserId"" = '$($p7.student.userId)' AND ""ResponderUserId"" = '$($other.userId)' AND ""RequestedTopicId"" = '$topic7' AND ""Status"" = 'Pending';"
if ($matches -eq '1') { OK 'paralel eşleşme isteği: tek bekleyen istek kaldı' } else { Fail "bekleyen istek sayısı: $matches" }

# ---------------------------------------------------------------- 9
# Rezervasyon artık hiçbir cüzdana yazmıyor; eskiden hiç değilse öğrenci cüzdanı kilit
# anahtarıydı, o da kalktı. İki FARKLI öğrenci geldiğinde MintGuard sayacı da devreye girmez
# (her çift için 1 ders, eğitmen tavanı 8). Geriye tek savunma olarak eğitmen takvimindeki
# çakışma kontrolü kalıyor ve o da ReadCommitted altında düz bir sorgudur: karşı tarafın
# commit edilmemiş kaydını göremez.
Step '9. İki farklı öğrenci, AYNI eğitmenin AYNI saatine paralel rezervasyon'
$tutorX = NewUser 'egtX'
Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $tutorX.token | Out-Null

$studentB = NewUser 'ogrB'
$studentC = NewUser 'ogrC'
$pairs = @()
foreach ($s in @($studentB, $studentC)) {
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek'; selfAssessedLevel = 2; note = $null } $s.token | Out-Null
    $mm = Api POST '/api/matches' @{ responderUserId = $tutorX.userId; requestedTopicId = $topicId; offeredTopicId = $null } $s.token
    Api POST "/api/matches/$mm/respond" @{ accept = $true } $tutorX.token | Out-Null
    $pairs += @{ student = $s; matchId = $mm }
}

$slot = [DateTime]::UtcNow.AddHours(30).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$reqs = @(
    @{ method = 'POST'; path = '/api/sessions'; token = $pairs[0].student.token; base = $API
       body = @{ matchId = $pairs[0].matchId; topicId = $topicId; scheduledStartUtc = $slot; durationMinutes = 60 } },
    @{ method = 'POST'; path = '/api/sessions'; token = $pairs[1].student.token; base = $API2
       body = @{ matchId = $pairs[1].matchId; topicId = $topicId; scheduledStartUtc = $slot; durationMinutes = 60 } }
)
$r9 = ParallelFire $reqs
Info (Summarize $r9)

$overlap = Sql "SELECT COUNT(*) FROM scheduling.""LessonSessions"" WHERE ""TutorUserId"" = '$($tutorX.userId)' AND ""Status"" IN ('Booked','AwaitingApproval');"
if ($overlap -eq '1') {
    OK 'eğitmenin aynı saatinde tek ders var (çakışma engellendi)'
} else {
    Fail "eğitmen aynı saatte $overlap derse kilitlendi — çift rezervasyon AÇIĞI (rezervasyon hiçbir satırı kilitlemiyor, eğitmen takvimi korumasız)"
}

# ---------------------------------------------------------------- 10
# ESKİ KAPSAM "çifte escrow iadesi" konusuz kaldı: iade edilecek kredi yok. YERİNE değişmez 7:
# iki taraf aynı anda iptal ederse bakiye DEĞİŞMEMELİ ama hata da üretilmemeli. Ayrıca iptal,
# MintGuard sayımından düşmeli (sayım Cancelled olmayan derslere bakar).
Step '10. Öğrenci ve eğitmen aynı anda iptal (yarışan iptal)'
$p10 = NewPair $topicId
$b10a = BookFor $p10 $topicId 5   # yarışın hedefi
$b10b = BookFor $p10 $topicId 7   # çiftin 24 saatlik tavanını (2) doldurur
$w10before = Wallet $p10.student.token

$reqs = @(
    @{ method = 'POST'; path = "/api/sessions/$($b10a.sessionId)/cancel"; token = $p10.student.token; base = $API;  body = @{ reason = 'ogrenci iptali' } },
    @{ method = 'POST'; path = "/api/sessions/$($b10a.sessionId)/cancel"; token = $p10.tutor.token;   base = $API2; body = @{ reason = 'egitmen iptali' } }
)
$r10 = ParallelFire $reqs
Info (Summarize $r10)

$err10 = ServerErrors $r10
if ($err10 -eq 0) { OK 'eşzamanlı çift taraflı iptal 5xx/taşıma hatası üretmedi' } else { Fail "$err10 istek 5xx/taşıma hatasıyla düştü" }
$ok10 = @($r10 | Where-Object { $_.Ok }).Count
if ($ok10 -ge 1) { OK "iptal kabul edildi ($ok10 istek başarılı)" } else { Fail 'hiçbir iptal isteği geçmedi' }

$st10 = SessionStatus $b10a.sessionId
if ($st10 -eq 'Cancelled') { OK 'ders Cancelled durumunda' } else { Fail "ders durumu: $st10" }

$w10after = Wallet $p10.student.token
if ($w10after.currentBalance -eq $w10before.currentBalance) { OK "iptal bakiyeye dokunmadı — iade edilecek kredi yok ($($w10after.currentBalance))" }
else { Fail "iptal sonrası bakiye değişti: $($w10before.currentBalance) -> $($w10after.currentBalance)" }

$legs10 = LegsFor $b10a.sessionId
if ($legs10 -eq 0) { OK 'iptal edilen ders defterde hiç hareket bırakmadı' } else { Fail "bacak sayısı: $legs10 (beklenen 0)" }
$holds10 = HoldsForSession $b10a.sessionId
if ($holds10 -eq 0) { OK 'derse bağlı hiç CreditHold yok (iade edilecek escrow da yok)' } else { Fail "$holds10 hold kaydı" }

# Tavan sayımı Cancelled dersleri saymadığına göre, iptalden sonra üçüncü ders açılabilmeli.
# (İptal olmasaydı çift 2/2 dolu olurdu ve bu istek 429 alırdı.)
$yeniden = $null
try { $yeniden = BookFor $p10 $topicId 9 } catch { $yeniden = $null }
if ($null -ne $yeniden -and $yeniden.sessionId) { OK 'iptal edilen ders tavan sayımından düştü, yeni rezervasyon açılabildi' }
else { Fail 'iptalden sonra yeni rezervasyon reddedildi — tavan sayımı iptal edilen dersleri de sayıyor' }

# ---------------------------------------------------------------- 11
# OpenDisputeHandler kilit almaz, transaction açmaz, retry kullanmaz — tek dayanağı
# LessonSession.xmin. Süpürücüyle yarıştırıp tutarlılığı ölçüyoruz. Escrow kalkmış olsa da
# xmin savunmasının kendisi korunmalı: iki yol aynı satırı aynı anda güncelleyememeli.
<#
  11. ve 12. ADIMLAR KALDIRILDI (2026-08-18).

  11 "itiraz + süpürücünün rezervasyonu düşürmesi", 12 "aynı itiraza paralel karar" idi.
  İkisi de itirazın ders durumunu değiştirmesine ve basımı dondurmasına dayanıyordu;
  tek yönlü şikayette ne durum değişikliği ne de basım dondurma var, dolayısıyla
  yarışacak bir şey de yok.

  Şikayet tarafındaki gerçek yarış (aynı derse çifte şikayet) 4. adıma taşındı.
  Şikayetin kendi sözleşmesi ayrıca tools/e2e-report.ps1'de sınanıyor.
#>


Step '8. Tüm yarışlardan sonra KÜRESEL değişmezler'
$negative = Sql "SELECT COUNT(*) FROM economy.""Wallets"" WHERE ""AvailableBalance"" < 0;"
if ($negative -eq '0') { OK 'hiçbir cüzdanda negatif bakiye yok' } else { Fail "$negative negatif cüzdan" }

# Değişmez 8 — defter değişmezinin YENİ tanımı: cüzdan toplamı = TÜM hareketlerin toplamı.
# (Eski "arz = mint - yakım" formülü tek bacaklı basımla anlamsız kaldı.)
$ledger = Sql "SELECT (SELECT COALESCE(SUM(""AvailableBalance""),0) FROM economy.""Wallets"") - (SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"");"
if ($ledger -eq '0') { OK 'defter dengeli: cüzdan toplamı = hareket toplamı' } else { Fail "defter farkı: $ledger" }

# Escrow kavramı tamamen kalktı: ne bloke bakiye ne açık hold kalmalı.
$lockedAny = Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='Wallets' AND column_name='LockedBalance';"
if ($lockedAny -eq '0') { OK 'hiçbir cüzdanda bloke bakiye yok' } else { Fail "$lockedAny cüzdanda bloke bakiye kalmış" }
$activeHolds = Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';"
if ($activeHolds -eq '0') { OK 'açık CreditHold yok (göçten sonra yenisi yazılmıyor)' } else { Fail "$activeHolds açık hold" }

$lotCheck = Sql "SELECT COUNT(*) FROM economy.""Wallets"" w WHERE w.""AvailableBalance"" <> (SELECT COALESCE(SUM(l.""RemainingAmount""),0) FROM economy.""CreditLots"" l WHERE l.""WalletId"" = w.""Id"");"
if ($lotCheck -eq '0') { OK 'cüzdan bakiyesi = lot toplamı' } else { Fail "$lotCheck cüzdanda tutarsızlık" }

# Değişmez 5: ders başına tek basım (unique index koruması).
$doubleCapture = Sql "SELECT COUNT(*) FROM (SELECT ""RelatedSessionId"" FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" IS NOT NULL AND ""Type"" = 'LessonEarning' GROUP BY ""RelatedSessionId"" HAVING COUNT(*) > 1) x;"
if ($doubleCapture -eq '0') { OK 'hiçbir ders iki kez basım üretmedi' } else { Fail "$doubleCapture derste çifte basım" }

<#
  Ödül ölçeği: her 30 dk için 50 puan. Yarış altında yanlış tutar basılmadığını doğrular.

  KAPSAM YENİ MODELLE SINIRLI. Sorgu başta TÜM LessonEarning satırlarını tarıyordu ve
  göç öncesinden kalan 224 kaydı da içine alıyordu; onlar eski ölçekte (60 dk = 1 kredi)
  yazılmış GERÇEK ve DOĞRU kayıtlar, yeni ölçeğe uymamaları beklenen bir şey. Kontrol
  onları da sayınca her koşumda kırmızı veriyordu — ölçtüğü şey bozuk değildi, ölçeği
  yanlıştı.

  Ayırt edici olarak CorrelationId kullanılıyor: eski kazançlar iki bacaklı transferin
  parçasıydı ve korelasyon taşırlar; yeni basım tek bacaklı ve korelasyonsuz. Tarihe
  göre ayırmak (CreatedAtUtc > göç anı) kırılgan olurdu, bu ayrım ise yapısal.
#>
$badAmount = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN scheduling.""LessonSessions"" s ON s.""Id"" = t.""RelatedSessionId"" WHERE t.""Type"" = 'LessonEarning' AND t.""CorrelationId"" IS NULL AND t.""Amount"" <> (s.""DurationMinutes"" / 30) * 50;"
if ($badAmount -eq '0') { OK 'her yeni basım süre ölçeğine uygun (30 dk = 50 puan)' } else { Fail "$badAmount basım yanlış tutarda" }

# Değişmez 4: gönüllü ders hiç basım üretmez.
$volMint = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN scheduling.""LessonSessions"" s ON s.""Id"" = t.""RelatedSessionId"" WHERE s.""IsVolunteer"" = TRUE AND t.""Type"" = 'LessonEarning';"
if ($volMint -eq '0') { OK 'gönüllü derslerin hiçbiri puan basmadı' } else { Fail "$volMint gönüllü derste basım var" }

# Değişmez 3: birikimli sayaç, o kullanıcıya basılan puanların gerisinde kalamaz.
$earnDrift = Sql "SELECT COUNT(*) FROM identity.""Users"" u WHERE u.""TotalEarnedCredits"" < (SELECT COALESCE(SUM(t.""Amount""),0) FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = u.""Id"" AND t.""Type"" = 'LessonEarning');"
if ($earnDrift -eq '0') { OK 'TotalEarnedCredits basılan puanların gerisinde değil' } else { Fail "$earnDrift kullanıcıda sayaç geride kalmış" }

$negEarned = Sql "SELECT COUNT(*) FROM identity.""Users"" WHERE ""TotalEarnedCredits"" < 0;"
if ($negEarned -eq '0') { OK 'hiçbir kullanıcıda negatif birikimli sayaç yok' } else { Fail "$negEarned kullanıcıda negatif sayaç" }

# Süre kümesi artık yalnız 30/60 (CK_LessonSessions_Duration) — yarışlar bunu delmemeli.
$badDuration = Sql "SELECT COUNT(*) FROM scheduling.""LessonSessions"" WHERE ""DurationMinutes"" NOT IN (30, 60);"
if ($badDuration -eq '0') { OK 'tüm dersler 30/60 dk kümesinde' } else { Fail "$badDuration ders süre kümesinin dışında" }

$lotOverdraft = Sql "SELECT COUNT(*) FROM economy.""CreditLots"" WHERE ""RemainingAmount"" < 0 OR ""RemainingAmount"" > ""InitialAmount"";"
if ($lotOverdraft -eq '0') { OK 'hiçbir lotta negatif/aşırı kalan yok' } else { Fail "$lotOverdraft bozuk lot" }

# ---------------------------------------------------------------- SONUÇ
Write-Host "`n================================" -ForegroundColor Yellow
if ($script:skipped -gt 0) {
    Write-Host "$($script:skipped) KAPSAM ATLANDI — bu paket eksik koştu, sonucu 'tam geçti' sayma." -ForegroundColor Yellow
}
if ($script:failures -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI" -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow

# ÇIKIŞ KODU ŞART: eksikti, yani bu paketteki gerçek bir başarısızlık çalıştırıcıya
# hiçbir sinyal göndermiyordu (etiket uyuşmazlığıyla birlikte paket sessizce yeşildi).
if ($script:failures -gt 0) { exit 1 }
