# PeerLearn — Tek yönlü şikayet (Report) testi
#
# NEDEN BU PAKET VAR: itiraz (Dispute) kaldırıldı, yerine tek yönlü şikayet geldi.
# e2e-dispute.ps1'in test ettiği yeteneğin çoğu artık YOK; buradaki iddialar yeni
# sözleşmeyi kilitliyor.
#
# ASIL SINANAN ŞEY GİZLİLİK. Şikayetin işe yaraması değil — o kolay; şikayet edilen
# kişinin ONU HİÇBİR YERDEN GÖREMEMESİ. Bu özellik sessizce bozulabilir: birine profil
# DTO'suna alan eklemek ya da ders listesine bir durum sızdırmak yeter, hiçbir test
# kırılmaz ve kimse fark etmez. Bu yüzden gizlilik burada tek tek kontrol ediliyor.
#
# Kullanım: powershell -ExecutionPolicy Bypass -File .\tools\e2e-report.ps1

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

$script:Pass = 0; $script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Sql($q) {
    $f = Join-Path $env:TEMP "pl-rep-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $f } finally { Remove-Item $f -Force }
}
function SqlInt($q) { $v = (Sql $q) -join ''; if ([string]::IsNullOrWhiteSpace($v)) { -1 } else { [int]$v.Trim() } }

function Send($method, $path, $body, $token) {
    $h = @{}; if ($token) { $h['Authorization'] = "Bearer $token" }
    $p = @{ Uri = "$Api$path"; Method = $method; Headers = $h; TimeoutSec = 60 }
    if ($null -ne $body) {
        $p['Body'] = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 6))
        $p['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @p
}
function HataKodu($e) { if ($e.Exception.Response) { [int]$e.Exception.Response.StatusCode } else { 0 } }
function HataGovde($e) {
    try { (New-Object IO.StreamReader($e.Exception.Response.GetResponseStream())).ReadToEnd() | ConvertFrom-Json | Select-Object -Expand title }
    catch { '(okunamadi)' }
}

$script:seq = 0
function NewUser($prefix, $stamp) {
    $script:seq++
    $hwid = (([Guid]::NewGuid().ToString('N')) * 2).Substring(0, 64)
    $email = "$prefix$stamp r$($script:seq)@test.dev" -replace ' ', ''
    $r = Send Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix K" } $null
    Send Post '/api/auth/verify-email' @{ token = $r.verificationToken } $null | Out-Null
    $l = Send Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    [pscustomobject]@{ Email = $email; Token = $l.accessToken; UserId = $l.userId; Hwid = $hwid }
}
function RolVer($u, $rol) {
    Sql "UPDATE identity.""Users"" SET ""Role"" = '$rol' WHERE ""Id"" = '$($u.UserId)';" | Out-Null
    $l = Send Post '/api/auth/login' @{ email = $u.Email; password = 'Demo12345'; hwidHash = $u.Hwid } $null
    $u.Token = $l.accessToken; $u
}

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
Write-Host "PeerLearn — tek yönlü şikayet" -ForegroundColor White

Section 'Hazırlık: ders yapılmış bir çift'
$topicId = ((Send Get '/api/catalog/topics' $null $null) | Where-Object { $_.topic -eq 'Türev' })[0].topicId
$ogr = NewUser 'repo' $stamp
$egt = NewUser 'repe' $stamp
$mod = RolVer (NewUser 'repm' $stamp) 'Admin'

Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $egt.Token | Out-Null
Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek'; selfAssessedLevel = 2; note = $null } $ogr.Token | Out-Null
$matchId = Send Post '/api/matches' @{ responderUserId = $egt.UserId; requestedTopicId = $topicId; offeredTopicId = $null } $ogr.Token
Send Post "/api/matches/$matchId/respond" @{ accept = $true } $egt.Token | Out-Null

$start = [DateTime]::UtcNow.AddHours(3).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$ders = Send Post '/api/sessions' @{ matchId = $matchId; topicId = $topicId; scheduledStartUtc = $start; durationMinutes = 60 } $ogr.Token
OK "ders rezerve edildi (basılacak puan: $($ders.mintAmount))"

Section 'A. Eski itiraz yolları KAPALI'

foreach ($u in @(
    @{ yol = "/api/sessions/$($ders.sessionId)/dispute"; govde = @{ reason = 'SessionNotHeld'; description = 'eski yol denemesi' }; ad = 'itiraz açma' },
    @{ yol = "/api/sessions/$($ders.sessionId)/dispute-response"; govde = @{ statement = 'savunma yazmaya calisiyorum' }; ad = 'savunma yazma' }
)) {
    try { Send Post $u.yol $u.govde $ogr.Token | Out-Null; Fail "$($u.ad) ucu hâlâ çalışıyor" }
    catch { if ((HataKodu $_) -eq 404) { OK "$($u.ad) ucu kaldırılmış (404)" } else { Fail "$($u.ad): $(HataKodu $_)" } }
}

Section 'B. Şikayet oluşturma ve doğrulama'

try { Send Post "/api/sessions/$($ders.sessionId)/report" @{ reason = 'Abuse'; description = 'kisa' } $ogr.Token | Out-Null
      Fail 'kısa açıklama kabul edildi' }
catch { if ((HataKodu $_) -eq 400) { OK 'kısa açıklama reddedildi (400)' } else { Fail "kod: $(HataKodu $_)" } }

$yabanci = NewUser 'repy' $stamp
try { Send Post "/api/sessions/$($ders.sessionId)/report" @{ reason = 'Abuse'; description = 'dersin tarafi degilim ama deniyorum' } $yabanci.Token | Out-Null
      Fail 'dersin tarafı olmayan şikayet edebildi' }
catch { if ((HataKodu $_) -eq 403) { OK 'dersin tarafı olmayan reddedildi (403)' } else { Fail "kod: $(HataKodu $_)" } }

$reportId = Send Post "/api/sessions/$($ders.sessionId)/report" @{ reason = 'Abuse'; description = 'Ders sirasinda hakaret etti, ekran goruntusu var.' } $ogr.Token
if ($reportId) { OK 'şikayet oluşturuldu' } else { Fail 'şikayet kimliği dönmedi' }

$hedef = Sql "SELECT ""ReportedUserId"" FROM moderation.""Reports"" WHERE ""Id"" = '$reportId';"
if ($hedef.Trim() -eq $egt.UserId) { OK 'şikayet edilen taraf DERSTEN türetildi (eğitmen)' }
else { Fail "şikayet edilen: $($hedef.Trim())" }

try { Send Post "/api/sessions/$($ders.sessionId)/report" @{ reason = 'Other'; description = 'ayni ders icin ikinci sikayet denemesi' } $ogr.Token | Out-Null
      Fail 'aynı ders için ikinci şikayet kabul edildi' }
catch {
    if ((HataKodu $_) -eq 409 -and (HataGovde $_) -eq 'REPORT_ALREADY_EXISTS') { OK 'aynı ders için ikinci şikayet reddedildi (409)' }
    else { Fail "kod: $(HataKodu $_) / $(HataGovde $_)" }
}

Section 'C. GİZLİLİK — şikayet edilen hiçbir yerden göremez'

# 1. Şikayet kuyruğu yalnızca yönetime açık.
foreach ($k in @(@{ t = $egt.Token; ad = 'şikayet edilen' }, @{ t = $ogr.Token; ad = 'şikayet eden' })) {
    try { Send Get '/api/admin/reports' $null $k.t | Out-Null; Fail "$($k.ad) şikayet kuyruğunu görebildi" }
    catch { if ((HataKodu $_) -in 401, 403) { OK "$($k.ad) kuyruğa erişemedi ($(HataKodu $_))" } else { Fail "kod: $(HataKodu $_)" } }
}

# 2. Şikayet edilenin ders listesinde hiçbir iz yok.
$egtDersler = (Send Get '/api/sessions' $null $egt.Token | ConvertTo-Json -Depth 10)
foreach ($sizinti in @('report', 'Report', 'şikayet', 'Şikayet', 'hakaret', 'Disputed')) {
    if ($egtDersler -match $sizinti) { Fail "eğitmenin ders listesinde '$sizinti' geçiyor" }
}
OK 'eğitmenin ders listesinde şikayete dair hiçbir iz yok'

# 3. Şikayet edilenin kendi profilinde iz yok.
$egtProfil = (Send Get "/api/users/$($egt.UserId)/profile" $null $egt.Token | ConvertTo-Json -Depth 10)
if ($egtProfil -match 'report|şikayet|hakaret') { Fail 'eğitmenin profilinde şikayet izi var' }
else { OK 'eğitmenin profilinde şikayet izi yok' }

# 4. Ders durumu DEĞİŞMEDİ: şikayet dersi dondurmaz.
$durum = (Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$($ders.sessionId)';").Trim()
if ($durum -eq 'Booked') { OK "ders durumu değişmedi ($durum) — şikayet akışı dondurmuyor" }
else { Fail "ders durumu: $durum (beklenen Booked)" }

Section 'D. Şikayet puan basımını ENGELLEMİYOR'

Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '2 hours', ""ScheduledEndUtc"" = now() - interval '1 hour' WHERE ""Id"" = '$($ders.sessionId)';" | Out-Null

$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
$bd = [Guid]::NewGuid().ToString('N'); $enc = [Text.Encoding]::UTF8
$ms = New-Object IO.MemoryStream
$h = $enc.GetBytes("--$bd`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$($ders.verificationCode)`r`n--$bd`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"p.png`"`r`nContent-Type: image/png`r`n`r`n")
$t = $enc.GetBytes("`r`n--$bd--`r`n")
$ms.Write($h,0,$h.Length); $ms.Write($png,0,$png.Length); $ms.Write($t,0,$t.Length)
Invoke-RestMethod -Uri "$Api/api/sessions/$($ders.sessionId)/complete" -Method Post `
    -Headers @{ Authorization = "Bearer $($egt.Token)" } `
    -ContentType "multipart/form-data; boundary=$bd" -Body $ms.ToArray() -TimeoutSec 60 | Out-Null
$ms.Dispose()

Send Post "/api/sessions/$($ders.sessionId)/approve" $null $ogr.Token | Out-Null

$basim = SqlInt "SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$($ders.sessionId)' AND ""Type"" = 'LessonEarning';"
if ($basim -eq $ders.mintAmount) { OK "şikayete rağmen puan normal basıldı ($basim)" }
else { Fail "basılan puan: $basim (beklenen $($ders.mintAmount))" }

$sonDurum = (Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$($ders.sessionId)';").Trim()
if ($sonDurum -eq 'Completed') { OK 'ders normal akışında tamamlandı' } else { Fail "durum: $sonDurum" }

Section 'E. Yönetim tarafı'

$kuyruk = Send Get '/api/admin/reports' $null $mod.Token
$bizim = $kuyruk | Where-Object { $_.reportId -eq $reportId }
if ($bizim) { OK 'şikayet yönetim kuyruğunda görünüyor' } else { Fail 'şikayet kuyrukta yok' }
if ($bizim.reportedDisplayName -and $bizim.reporterDisplayName) { OK 'kuyruk iki tarafın adını da gösteriyor (yalnızca yönetime)' }
else { Fail 'kuyrukta taraf adları eksik' }
if ($bizim.reportedUserTotalReports -ge 1) { OK "kişi başına toplam şikayet sayısı geliyor ($($bizim.reportedUserTotalReports))" }
else { Fail "toplam şikayet: $($bizim.reportedUserTotalReports)" }

Send Post "/api/admin/reports/$reportId/resolve" @{ actionTaken = $true; note = 'Uyari verildi.' } $mod.Token | Out-Null
$kapali = (Sql "SELECT ""Status"" FROM moderation.""Reports"" WHERE ""Id"" = '$reportId';").Trim()
if ($kapali -eq 'ActionTaken') { OK 'şikayet kapatıldı (ActionTaken)' } else { Fail "durum: $kapali" }

$iz = SqlInt "SELECT COUNT(*) FROM moderation.""AdminActionLogs"" WHERE ""Action"" = 'ReportReviewed' AND ""TargetId"" = '$reportId';"
if ($iz -eq 1) { OK 'karar denetim izine yazıldı' } else { Fail "denetim izi satırı: $iz" }

# Denetim izi ŞİKAYETE bağlanır, şikayet edilen kullanıcıya değil: yaptırım uygulanmamış
# şikayetler kişinin geçmişinde "hakkında karar verildi" olarak birikmemeli.
$hedefIz = SqlInt "SELECT COUNT(*) FROM moderation.""AdminActionLogs"" WHERE ""Action"" = 'ReportReviewed' AND ""TargetId"" = '$($egt.UserId)';"
if ($hedefIz -eq 0) { OK 'denetim izi şikayete bağlandı, kişiye değil' } else { Fail "kişiye bağlı iz: $hedefIz" }

try { Send Post "/api/admin/reports/$reportId/resolve" @{ actionTaken = $false; note = 'ikinci karar' } $mod.Token | Out-Null
      Fail 'kapalı şikayet ikinci kez kapatılabildi' }
catch { if ((HataKodu $_) -eq 409) { OK 'kapalı şikayet ikinci kez kapatılamadı (409)' } else { Fail "kod: $(HataKodu $_)" } }

Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green }
else { Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red; exit 1 }
