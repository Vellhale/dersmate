# PeerLearn — MintGuard eğitmen tavanı (günde 8 ders) testi
#
# NEDEN AYRI PAKET:
# e2e-concurrency.ps1 yalnızca ÇİFT tavanını (24 saatte 2 ders) sınıyor ve onu aynı
# öğrenciyle zorluyor. Eğitmen tavanı (MaxSessionsPerTutorPerDay = 8) bugüne kadar HİÇ
# sınanmadı — ne sıralı ne paralel. Oysa iki tavan farklı şeylere karşı korur:
#   • çift tavanı  → iki hesabın birbirine ders yazması (karşılıklı basım)
#   • eğitmen tavanı → BİR hesabın çok sayıda farklı hesaptan ders toplaması
# İkincisi sahte öğrenci ordusuyla yapılan basımın tam karşılığıdır ve kanıtı yoktu.
#
# ÖLÇÜLEN İKİ AYRI ŞEY:
#   A. Tavan DOĞRU mu  — sıralı istekle 8 kabul, 9. ret. (İşlevsel kanıt.)
#   B. Tavan SAĞLAM mı — 12 eşzamanlı istekten yine tam 8 geçmeli. (Yarış kanıtı.)
#
# B, A'dan bağımsız olarak düşebilir: "say sonra yaz" arasındaki boşluk serileşmezse
# hepsi sayacı aynı anda okur ve tavan hiç devreye girmez. Rezervasyon kilidi ÇİFT
# bazında alınıyor; farklı öğrenciler farklı kilitlere düştüğü için aynı eğitmene
# yönelen paralel istekler birbirini hiç görmez. Bu paket tam olarak onu ölçer.
#
# İKİ INSTANCE ŞART: süreç içi kilit (SemaphoreSlim) tek instance'ta yarışı gizler.
# 5001 kapalıysa B adımı ATLANDI sayılır — sahte "geçti" üretmesin.
#
# Kullanım (proje kökünden):
#   powershell -ExecutionPolicy Bypass -File .\tools\e2e-mintguard.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
[System.Net.ServicePointManager]::DefaultConnectionLimit = 100

$API  = 'http://localhost:5000'
$API2 = 'http://localhost:5001'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

# Beklenen tavanlar. Bunlar C# tarafındaki sabitlerin KOPYASI, dolayısıyla kaynak
# değişince test sessizce yanlış şeyi ölçmeye başlayabilir — "8 kabul edildi" diyen bir
# koşum sabit 12'ye çıkarılmışsa hâlâ yeşil görünür ama artık hiçbir şey kanıtlamaz.
# Bu yüzden Hazırlık adımında sabitler MintGuard.cs'ten okunup karşılaştırılıyor.
$PAIR_CAP  = 2
$TUTOR_CAP = 8
$MINTGUARD_CS = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\PeerLearn.Application\Economy\MintGuard.cs'

$script:failures = 0
$script:skipped  = 0
function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Fail($msg)  { Write-Host "  [KALDI] $msg" -ForegroundColor Red; $script:failures++ }
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

# Reddedilen isteğin KODUNU da görmek gerekiyor: 429 dönen her şey MintGuard değildir
# (istek hız sınırlayıcı da 429 döner). Bu yüzden gövdeyi ayrıştırıp title'a bakıyoruz.
function TryApi {
    param($Method, $Path, $Body, $Token, $Base = $API)
    try {
        $r = Api $Method $Path $Body $Token $Base
        return [PSCustomObject]@{ Ok = $true; Status = 200; Code = $null; Body = $r }
    } catch {
        $resp = $_.Exception.Response
        $status = 0; $code = 'TRANSPORT'
        if ($resp) {
            $status = [int]$resp.StatusCode
            try {
                $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
                $code = ($sr.ReadToEnd() | ConvertFrom-Json).title
            } catch { $code = 'PARSE_FAIL' }
        }
        return [PSCustomObject]@{ Ok = $false; Status = $status; Code = $code; Body = $null }
    }
}

function Sql($query) {
    $f = Join-Path $env:TEMP ("mg_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return ($out -join '').Trim()
}
function SqlInt($query) {
    $v = Sql $query
    if ([string]::IsNullOrWhiteSpace($v)) { return -1 }
    return [int]$v
}

<#
  İstekleri GERÇEKTEN aynı anda gönderir: tüm mesajlar önce kurulur, sonra tek döngüde
  ateşlenir. Sıralı gönderim yarışı hiç tetiklemez ve test sahte "geçti" üretir.
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
            $msg.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, 'application/json')
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
function ServerErrors($results) {
    return @($results | Where-Object { $_.Status -ge 500 -or $_.Code -eq 'TRANSPORT' }).Count
}
function SplitBases($count) {
    $bases = @()
    for ($i = 0; $i -lt $count; $i++) { $bases += $(if ($i % 2 -eq 0) { $API } else { $API2 }) }
    return $bases
}

$script:seq = 0
function NewUser($prefix) {
    $script:seq++
    $email = "$prefix$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())m$($script:seq)@test.dev"
    $hwid = (([Guid]::NewGuid().ToString('N')) * 2).Substring(0, 64)
    $reg = Api POST '/api/auth/register' @{ email = $email; password = 'Parola12345'; displayName = "$prefix K" }
    Api POST '/api/auth/verify-email' @{ token = $reg.verificationToken } | Out-Null
    $login = Api POST '/api/auth/login' @{ email = $email; password = 'Parola12345'; hwidHash = $hwid }
    return @{ email = $email; userId = $login.userId; token = $login.accessToken }
}

<#
  BİR eğitmen, ÇOK öğrenci. e2e-concurrency'deki NewPair her seferinde YENİ bir eğitmen
  de kurduğu için eğitmen tavanına hiç yaklaşamıyordu; asıl boşluk oradaydı.
#>
function NewTutor($topicId) {
    $tutor = NewUser 'mgegt'
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $tutor.token | Out-Null
    return $tutor
}
function NewStudentFor($tutor, $topicId) {
    $student = NewUser 'mgogr'
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek'; selfAssessedLevel = 2; note = $null } $student.token | Out-Null
    $m = Api POST '/api/matches' @{ responderUserId = $tutor.userId; requestedTopicId = $topicId; offeredTopicId = $null } $student.token
    Api POST "/api/matches/$m/respond" @{ accept = $true } $tutor.token | Out-Null
    return @{ user = $student; matchId = $m }
}

# Saat: dersler ÇAKIŞMAMALI, yoksa reddin sebebi tavan değil takvim çatışması olur ve
# test yanlış şeyi kanıtlar. 30 dk'lık dersler 1'er saat arayla → çakışma yok, ve tüm
# yayılım 24 saatin altında kaldığı için hepsi birbirinin penceresinde.
function SlotAt([int]$hoursAhead) {
    return [DateTime]::UtcNow.AddHours($hoursAhead).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
}
function TutorSessions($tutorUserId) {
    SqlInt "SELECT COUNT(*) FROM scheduling.""LessonSessions"" WHERE ""TutorUserId"" = '$tutorUserId' AND ""Status"" <> 'Cancelled';"
}

# ---------------------------------------------------------------- HAZIRLIK
Step 'Hazırlık'
$second = $false
try {
    Invoke-RestMethod "$API2/health" -TimeoutSec 5 | Out-Null
    $second = $true
    OK 'ikinci instance (5001) ayakta — yarış süreç sınırını aşacak'
} catch {
    Skip 'ikinci instance (5001) KAPALI — B adımındaki yarış SINANMADI (süreç içi kilit gizler)'
}

# Sabitler gerçekten beklenen değerde mi: değilse aşağıdaki tüm sayılar anlamsızdır ve
# koşumu sürdürmek yanıltıcı bir "geçti" üretir. Bu yüzden burada DURUYORUZ.
if (-not (Test-Path -LiteralPath $MINTGUARD_CS)) { Fail "MintGuard.cs bulunamadı: $MINTGUARD_CS"; exit 1 }
$kaynak = [IO.File]::ReadAllText($MINTGUARD_CS)
foreach ($sabit in @(@{ Ad = 'MaxSessionsPerPairPerDay'; Beklenen = $PAIR_CAP },
                     @{ Ad = 'MaxSessionsPerTutorPerDay'; Beklenen = $TUTOR_CAP })) {
    $m = [regex]::Match($kaynak, "$($sabit.Ad)\s*=\s*(\d+)")
    if (-not $m.Success) { Fail "$($sabit.Ad) sabiti MintGuard.cs içinde bulunamadı"; exit 1 }
    if ([int]$m.Groups[1].Value -ne $sabit.Beklenen) {
        Fail "$($sabit.Ad) kodda $($m.Groups[1].Value), test $($sabit.Beklenen) bekliyor — testi güncelle"
        exit 1
    }
}
OK "tavanlar kodla uyuşuyor (çift=$PAIR_CAP, eğitmen=$TUTOR_CAP)"

$topicId = ((Api GET '/api/catalog/topics') | Where-Object { $_.topic -eq 'Türev' })[0].topicId
if (-not $topicId) { Fail 'Türev konusu bulunamadı — katalog eksik'; exit 1 }
OK "konu hazır ($topicId)"

# ---------------------------------------------------------------- A
# İŞLEVSEL KANIT. Yarış yok, tek tek istek. Burası düşerse tavan HİÇ çalışmıyor demektir
# ve B adımının sonucunu yorumlamanın anlamı kalmaz.
Step "A. Sıralı: eğitmen tavanı ($TUTOR_CAP) tek tek isteklerle tutuyor mu"
$tA = NewTutor $topicId
$kabulA = 0; $retA = 0; $kodA = @()
for ($i = 0; $i -lt ($TUTOR_CAP + 1); $i++) {
    $s = NewStudentFor $tA $topicId
    $r = TryApi POST '/api/sessions' @{ matchId = $s.matchId; topicId = $topicId
                                        scheduledStartUtc = SlotAt (3 + $i); durationMinutes = 30 } $s.user.token
    if ($r.Ok) { $kabulA++ } else { $retA++; $kodA += "$($r.Status)/$($r.Code)" }
}
Info "kabul=$kabulA ret=$retA $(if ($kodA) { '(' + ($kodA -join ', ') + ')' })"

if ($kabulA -eq $TUTOR_CAP) { OK "ilk $TUTOR_CAP rezervasyon kabul edildi" }
else { Fail "kabul edilen rezervasyon: $kabulA (beklenen $TUTOR_CAP)" }

if ($retA -eq 1 -and $kodA[0] -eq "429/MINT_LIMIT_REACHED") { OK "$($TUTOR_CAP + 1). rezervasyon 429 MINT_LIMIT_REACHED ile reddedildi" }
else { Fail "$($TUTOR_CAP + 1). rezervasyonun reddi beklendiği gibi değil: $($kodA -join ', ')" }

$dbA = TutorSessions $tA.userId
if ($dbA -eq $TUTOR_CAP) { OK "veritabanında $TUTOR_CAP ders var" } else { Fail "veritabanındaki ders sayısı: $dbA (beklenen $TUTOR_CAP)" }

# ---------------------------------------------------------------- B
# YARIŞ KANITI. Farklı öğrenciler = farklı çift kilitleri. Tavan yalnızca çift kilidiyle
# korunuyorsa bu adımda 12'nin 12'si de geçer.
Step "B. Paralel: aynı eğitmene 12 FARKLI öğrenciden eşzamanlı rezervasyon"
if (-not $second) {
    Skip 'ikinci instance kapalı — bu adım anlamlı sonuç veremez'
} else {
    $tB = NewTutor $topicId
    $ogrenciler = @()
    for ($i = 0; $i -lt 12; $i++) { $ogrenciler += NewStudentFor $tB $topicId }
    Info '12 öğrenci ve kabul edilmiş eşleşme hazır'

    $bases = SplitBases 12
    $reqs = @()
    for ($i = 0; $i -lt 12; $i++) {
        $reqs += @{ method = 'POST'; path = '/api/sessions'; token = $ogrenciler[$i].user.token; base = $bases[$i]
                    body = @{ matchId = $ogrenciler[$i].matchId; topicId = $topicId
                              scheduledStartUtc = SlotAt (3 + $i); durationMinutes = 30 } }
    }
    $rB = ParallelFire $reqs
    Info (Summarize $rB)

    $okB = @($rB | Where-Object { $_.Ok }).Count
    if ($okB -eq $TUTOR_CAP) { OK "eğitmen tavanı yarış altında tuttu: TAM OLARAK $TUTOR_CAP rezervasyon" }
    else { Fail "$okB rezervasyon kabul edildi — eğitmen tavanı ($TUTOR_CAP) eşzamanlılıkla AŞILDI" }

    $ret429 = @($rB | Where-Object { $_.Code -eq 'MINT_LIMIT_REACHED' }).Count
    if ($ret429 -eq (12 - $TUTOR_CAP)) { OK "kalan $(12 - $TUTOR_CAP) istek MINT_LIMIT_REACHED ile reddedildi" }
    else { Fail "MINT_LIMIT_REACHED dönen istek: $ret429 (beklenen $(12 - $TUTOR_CAP))" }

    $errB = ServerErrors $rB
    if ($errB -eq 0) { OK 'ret temiz döndü (5xx/taşıma hatası yok)' } else { Fail "$errB istek 5xx/taşıma hatasıyla düştü" }

    # Asıl ölçüt API sayısı değil VERİTABANI: tavan aşıldıysa fazlalık kalıcıdır.
    $dbB = TutorSessions $tB.userId
    if ($dbB -eq $TUTOR_CAP) { OK "veritabanında $TUTOR_CAP ders var" }
    else { Fail "veritabanındaki ders sayısı: $dbB (beklenen $TUTOR_CAP) — fazla basım kapasitesi kalıcı" }

    # ------------------------------------------------------------ C
    # İptal kotayı GERİ VERMELİ: sayım Cancelled dersleri dışlıyor. Bu davranış
    # MintGuard'ın yorumunda yazılı ama hiç sınanmamıştı.
    Step 'C. İptal edilen ders kotadan düşer mi'
    if ($dbB -eq $TUTOR_CAP) {
        $iptalEdilecek = (Sql "SELECT ""Id"" FROM scheduling.""LessonSessions"" WHERE ""TutorUserId"" = '$($tB.userId)' AND ""Status"" = 'Booked' ORDER BY ""ScheduledStartUtc"" LIMIT 1;")
        Api POST "/api/sessions/$iptalEdilecek/cancel" @{ reason = 'mintguard testi' } $tB.token | Out-Null
        Info "ders iptal edildi ($iptalEdilecek)"

        $sonrasi = TryApi POST '/api/sessions' @{ matchId = $ogrenciler[11].matchId; topicId = $topicId
                                                  scheduledStartUtc = SlotAt 16; durationMinutes = 30 } $ogrenciler[11].user.token
        if ($sonrasi.Ok) { OK 'iptalden sonra yeni rezervasyon kabul edildi (kota geri geldi)' }
        else { Fail "iptalden sonra rezervasyon reddedildi: $($sonrasi.Status)/$($sonrasi.Code)" }
    } else {
        Skip 'B adımı beklenen durumu bırakmadı — iptal ölçümü anlamsız olurdu'
    }

    # ------------------------------------------------------------ D
    # PENCERE KAYAN OLMALI, MUTLAK GÜN DEĞİL. Tavan dolu bir eğitmen üç gün sonrasına
    # ders alabilmeli; alamıyorsa fren dürüst kullanıcıyı kilitliyor demektir.
    Step 'D. Tavan dolu eğitmen 3 gün sonrasına ders alabiliyor mu (kayan pencere)'
    $ileri = TryApi POST '/api/sessions' @{ matchId = $ogrenciler[10].matchId; topicId = $topicId
                                            scheduledStartUtc = SlotAt 72; durationMinutes = 30 } $ogrenciler[10].user.token
    if ($ileri.Ok) { OK '±24 saatlik pencerenin dışındaki rezervasyon kabul edildi' }
    else { Fail "pencere dışı rezervasyon reddedildi: $($ileri.Status)/$($ileri.Code) — tavan olağan kullanımı engelliyor" }
}

# ---------------------------------------------------------------- E
# ÇİFT TAVANI hâlâ ayakta mı: eğitmen tavanı için yapılacak her düzeltme çift tavanını
# yanlışlıkla gevşetebilir. İki tavan birbirinden bağımsız ölçülmeli.
Step "E. Çift tavanı ($PAIR_CAP) hâlâ tutuyor mu (gerileme kontrolü)"
$tE = NewTutor $topicId
$sE = NewStudentFor $tE $topicId
$kabulE = 0; $kodE = @()
for ($i = 0; $i -lt ($PAIR_CAP + 1); $i++) {
    $r = TryApi POST '/api/sessions' @{ matchId = $sE.matchId; topicId = $topicId
                                        scheduledStartUtc = SlotAt (3 + $i); durationMinutes = 30 } $sE.user.token
    if ($r.Ok) { $kabulE++ } else { $kodE += "$($r.Status)/$($r.Code)" }
}
if ($kabulE -eq $PAIR_CAP -and $kodE.Count -eq 1 -and $kodE[0] -eq '429/MINT_LIMIT_REACHED') {
    OK "aynı çift $PAIR_CAP ders açtı, $($PAIR_CAP + 1). istek 429 ile reddedildi"
} else {
    Fail "çift tavanı bozuldu: kabul=$kabulE ret=$($kodE -join ', ')"
}

# ---------------------------------------------------------------- ÖZET
Write-Host ""
if ($script:failures -eq 0) { Write-Host "SONUC: TUM KONTROLLER GECTI (atlanan: $($script:skipped))" -ForegroundColor Green }
else { Write-Host "SONUC: $($script:failures) KONTROL KALDI (atlanan: $($script:skipped))" -ForegroundColor Red }
exit $script:failures
