# Arayüzü gerçek veriyle denemek için sabit demo hesapları + senaryo
$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'

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

function Register($email, $name, $hwid) {
    try {
        $r = Api POST '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = $name }
        Api POST '/api/auth/verify-email' @{ token = $r.verificationToken } | Out-Null
        Write-Host "kaydedildi: $email"
    } catch { Write-Host "zaten var: $email" }
    # Token + userId birlikte dönmeli: isimle eşleştirme, testlerden kalan aynı adlı
    # kullanıcıları yakalayıp yanlış kişiye istek göndermeye yol açıyordu.
    return (Api POST '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid })
}

$ayse = Register 'ayse@demo.dev' 'Ayşe Yılmaz' ('a' * 64)
$berk = Register 'berk@demo.dev' 'Berk Demir'  ('b' * 64)
$aT = $ayse.accessToken
$bT = $berk.accessToken
if (-not $aT -or -not $bT) { throw 'token alinamadi' }

$topics = Api GET '/api/catalog/topics'
$turev   = ($topics | Where-Object { $_.topic -eq 'Türev' })[0]
$organik = ($topics | Where-Object { $_.topic -eq 'Organik Kimya' })[0]
$integral = ($topics | Where-Object { $_.topic -eq 'İntegral' })[0]

function AddEntry($token, $topicId, $direction, $level) {
    try { Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = $direction; selfAssessedLevel = $level; note = $null } $token | Out-Null } catch {}
}
AddEntry $aT $turev.topicId    'Offer' 5
AddEntry $aT $integral.topicId 'Offer' 4
AddEntry $aT $organik.topicId  'Seek'  2
AddEntry $bT $organik.topicId  'Offer' 4
AddEntry $bT $turev.topicId    'Seek'  1
Write-Host "portföyler hazır"

# Eşleşme + sohbet
$existing = Api GET '/api/matches' $null $aT
$activeWithBerk = @($existing.active | Where-Object { $_.otherUserId -eq $berk.userId })

if ($activeWithBerk.Count -gt 0) {
    $matchIdFinal = $activeWithBerk[0].matchId
    Write-Host "aktif eşleşme zaten var"
} else {
    # Yalnızca BERK'e giden bekleyen isteği kullan (başkasına gitmiş eski istekler işe yaramaz).
    $pendingToBerk = @($existing.outgoing | Where-Object { $_.otherUserId -eq $berk.userId })
    if ($pendingToBerk.Count -gt 0) {
        $matchIdFinal = $pendingToBerk[0].matchId
        Write-Host "Berk'e giden bekleyen istek bulundu"
    } else {
        $matchIdFinal = Api POST '/api/matches' @{ responderUserId = $berk.userId; requestedTopicId = $organik.topicId; offeredTopicId = $turev.topicId } $aT
        Write-Host "yeni istek gönderildi"
    }

    $resp = Api POST "/api/matches/$matchIdFinal/respond" @{ accept = $true } $bT
    Api POST "/api/conversations/$($resp.conversationId)/messages" @{ content = 'Merhaba! Organik kimya için yarın 20:00 uygun mu?' } $bT | Out-Null
    Api POST "/api/conversations/$($resp.conversationId)/messages" @{ content = 'Harika, link: https://meet.google.com/abc-defg-hij' } $aT | Out-Null
    Write-Host "eşleşme + sohbet hazır"
}

# Yaklaşan ders (Time-Lock geri sayımı arayüzde görünsün)
$sessions = Api GET '/api/sessions' $null $aT
if (($sessions | Where-Object { $_.status -eq 'Booked' }).Count -eq 0) {
    $start = [DateTime]::UtcNow.AddHours(3).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $b = Api POST '/api/sessions' @{ matchId = $matchIdFinal; topicId = $organik.topicId; scheduledStartUtc = $start; durationMinutes = 60 } $aT
    Write-Host "ders rezerve edildi, kod: $($b.verificationCode)"
} else { Write-Host "rezerve ders zaten var" }

# ---------------------------------------------------------------- ADMIN + AÇIK İTİRAZ
# Yönetim panelini gerçek veriyle denemek için: bir admin hesabı ve karar bekleyen bir itiraz.
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'
function Sql($query) {
    $f = Join-Path $env:TEMP ("pl_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return ($out -join '').Trim()
}

$adminLogin = Register 'admin@demo.dev' 'Demo Yönetici' ('d' * 64)
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($adminLogin.userId)';" | Out-Null
Write-Host "admin hesabi hazir"

# Karar bekleyen itiraz zaten varsa yenisini üretme.
$openDisputes = Sql "SELECT COUNT(*) FROM moderation.""Disputes"" WHERE ""Status"" IN ('Open','UnderReview');"
if ($openDisputes -eq '0') {
    $ali  = Register 'ali@demo.dev'  'Ali Öğrenci' ('e' * 64)
    $veli = Register 'veli@demo.dev' 'Veli Eğitmen' ('f' * 64)

    try { Api POST '/api/portfolio/entries' @{ topicId = $integral.topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $veli.accessToken | Out-Null } catch {}
    try { Api POST '/api/portfolio/entries' @{ topicId = $integral.topicId; direction = 'Seek'; selfAssessedLevel = 2; note = $null } $ali.accessToken | Out-Null } catch {}

    $m = Api POST '/api/matches' @{ responderUserId = $veli.userId; requestedTopicId = $integral.topicId; offeredTopicId = $null } $ali.accessToken
    Api POST "/api/matches/$m/respond" @{ accept = $true } $veli.accessToken | Out-Null

    $st = [DateTime]::UtcNow.AddHours(2).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $bk = Api POST '/api/sessions' @{ matchId = $m; topicId = $integral.topicId; scheduledStartUtc = $st; durationMinutes = 60 } $ali.accessToken

    # Dersi geçmişe al, eğitmen kanıt yüklesin, öğrenci itiraz etsin.
    Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '2 hours', ""ScheduledEndUtc"" = now() - interval '1 hour' WHERE ""Id"" = '$($bk.sessionId)';" | Out-Null

    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    $bd = [Guid]::NewGuid().ToString('N')
    $enc = [System.Text.Encoding]::UTF8
    $ms = New-Object System.IO.MemoryStream
    $h = $enc.GetBytes("--$bd`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$($bk.verificationCode)`r`n--$bd`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"proof.png`"`r`nContent-Type: image/png`r`n`r`n")
    $t = $enc.GetBytes("`r`n--$bd--`r`n")
    $ms.Write($h,0,$h.Length); $ms.Write($png,0,$png.Length); $ms.Write($t,0,$t.Length)
    Invoke-RestMethod -Uri "$API/api/sessions/$($bk.sessionId)/complete" -Method Post -Headers @{ Authorization = "Bearer $($veli.accessToken)" } -ContentType "multipart/form-data; boundary=$bd" -Body $ms.ToArray() -TimeoutSec 60 | Out-Null
    $ms.Dispose()

    Api POST "/api/sessions/$($bk.sessionId)/dispute" @{ reason = 'FakeProof'; description = 'Ekran görüntüsündeki saat ders saatiyle uyuşmuyor, katılımcı listesi de görünmüyor.' } $ali.accessToken | Out-Null
    Write-Host "karar bekleyen itiraz olusturuldu (ali -> veli)"
} else {
    Write-Host "acik itiraz zaten var ($openDisputes adet)"
}

Write-Host "`nDEMO HESAPLAR (sifre: Demo12345)" -ForegroundColor Green
Write-Host "  ayse@demo.dev  / berk@demo.dev   — eslesme, sohbet, rezerve ders" -ForegroundColor Green
Write-Host "  ali@demo.dev   / veli@demo.dev   — karar bekleyen itiraz" -ForegroundColor Green
Write-Host "  admin@demo.dev                   — yonetim paneli" -ForegroundColor Green
