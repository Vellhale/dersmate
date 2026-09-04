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
        $r = Api POST '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = $name; termsVersion = (& "$PSScriptRoot\yasal-surum.ps1"); ageConfirmed = $true }
        Api POST '/api/auth/verify-email' @{ email = $email; code = $r.verificationToken } | Out-Null
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

# Konu adları müfredatla birlikte değişiyor (CatalogSeeder eşitleyici): eskiden var olan
# "Organik Kimya" ve "İntegral" artık katalogda "Organik Kimyaya Giriş" / "Belirsiz
# İntegral" olarak duruyor. Tam ad tutmazsa $null dönüyordu ve betik ilerideki bir
# satırda, sebebi görünmeyen bir hatayla düşüyordu. Aday listesi + baştan eşleşme, ada
# bağlılığı gevşetiyor; hiçbiri tutmazsa NEDEN düştüğünü söyleyerek duruyor.
function Konu($katalog, [string[]]$adaylar) {
    foreach ($ad in $adaylar) {
        $tam = @($katalog | Where-Object { $_.topic -eq $ad })
        if ($tam.Count -gt 0) { return $tam[0] }
    }
    foreach ($ad in $adaylar) {
        $bas = @($katalog | Where-Object { $_.topic -like ($ad + '*') })
        if ($bas.Count -gt 0) { return $bas[0] }
    }
    throw ("Katalogda konu bulunamadi: " + ($adaylar -join ' / ') + ". Katalog esitlendi mi? (dotnet run --project src/PeerLearn.Api -- --migrate)")
}

$turev    = Konu $topics @('Türev')
$organik  = Konu $topics @('Organik Kimya', 'Organik Kimyaya Giriş')
$integral = Konu $topics @('İntegral', 'Belirsiz İntegral')
Write-Host ("konular: " + $turev.topic + " / " + $organik.topic + " / " + $integral.topic)

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
# SQL yolu: yerel psql varsa o, yoksa Docker konteyneri.
#
# Betik yalnızca yerel PostgreSQL 17 kurulumunu biliyordu ve `docker compose` ile çalışan
# bir makinede "psql bulunamadi" ile düşüyordu — oysa docs/GELISTIRME-ORTAMI.md sıfırdan
# kurulum için Docker'ı tarif ediyor. Yığının kalanı (API, arayüz) iki kurulumda da aynı;
# ayrım yalnızca burada, çünkü admin rolünü ve itiraz sayımını API değil SQL veriyor.
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$YerelPsql = Test-Path $PSQL
$Compose = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'
$env:PGPASSWORD = 'PeerLearnDev2026'
if (-not $YerelPsql) { Write-Host "psql yerelde yok - Docker konteyneri kullanilacak" }

function Sql($query) {
    # İKİ DALDA DA `-c` KULLANILMIYOR. Sorgular şema/tablo adlarını çift tırnakla yazıyor
    # (identity."Users"); bunu komut satırı argümanı olarak geçirmek tırnakları kabuğa
    # yedirip sorguyu sessizce bozuyor. Yerelde dosya, Docker'da STDIN kullanılıyor.
    if ($YerelPsql) {
        $f = Join-Path $env:TEMP ("pl_" + [Guid]::NewGuid().ToString('N') + ".sql")
        Set-Content -Path $f -Value $query -Encoding UTF8
        $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
        Remove-Item $f -Force -ErrorAction SilentlyContinue
    } else {
        $out = $query | docker compose -f $Compose exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
    }
    return ($out -join '').Trim()
}

$adminLogin = Register 'admin@demo.dev' 'Demo Yönetici' ('d' * 64)
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($adminLogin.userId)';" | Out-Null
Write-Host "admin hesabi hazir"

# İTİRAZ (Dispute) DEĞİL, ŞİKAYET (Report).
#
# Betik `POST /api/sessions/{id}/dispute` çağırıyordu; o uç kaldırıldı (bkz.
# Domain/Moderation/Report.cs — iki taraflı itiraz, tek yönlü şikayetle değiştirildi) ve
# çağrı 404 ile düşüyordu. Disputes tablosu denetim izi olarak DURUYOR ama yeni kayıt
# almıyor, dolayısıyla "açık kayıt var mı" kontrolü de Reports'a bakmalı — Disputes'a
# bakan eski kontrol her koşuda 0 görüp bloğu baştan çalıştırıyordu.
$acikSikayet = Sql "SELECT COUNT(*) FROM moderation.""Reports"" WHERE ""Status"" = 'Open';"
if ($acikSikayet -eq '0') {
    $ali  = Register 'ali@demo.dev'  'Ali Öğrenci' ('e' * 64)
    $veli = Register 'veli@demo.dev' 'Veli Eğitmen' ('f' * 64)

    try { Api POST '/api/portfolio/entries' @{ topicId = $integral.topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $veli.accessToken | Out-Null } catch {}
    try { Api POST '/api/portfolio/entries' @{ topicId = $integral.topicId; direction = 'Seek'; selfAssessedLevel = 2; note = $null } $ali.accessToken | Out-Null } catch {}

    # Eşleşme zaten varsa yenisini kurma: ikinci koşuda POST /api/matches 409 döner ve
    # betik burada düşerdi. (Ayşe-Berk bloğu bu kontrolü zaten yapıyordu, bu blok
    # atlanmıştı.)
    $aliMatches = Api GET '/api/matches' $null $ali.accessToken
    $aktifVeli = @($aliMatches.active | Where-Object { $_.otherUserId -eq $veli.userId })

    if ($aktifVeli.Count -gt 0) {
        $m = $aktifVeli[0].matchId
    } else {
        $bekleyenVeli = @($aliMatches.outgoing | Where-Object { $_.otherUserId -eq $veli.userId })
        if ($bekleyenVeli.Count -gt 0) {
            $m = $bekleyenVeli[0].matchId
        } else {
            $m = Api POST '/api/matches' @{ responderUserId = $veli.userId; requestedTopicId = $integral.topicId; offeredTopicId = $null } $ali.accessToken
        }
        Api POST "/api/matches/$m/respond" @{ accept = $true } $veli.accessToken | Out-Null
    }

    $st = [DateTime]::UtcNow.AddHours(2).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $bk = Api POST '/api/sessions' @{ matchId = $m; topicId = $integral.topicId; scheduledStartUtc = $st; durationMinutes = 60 } $ali.accessToken

    # Dersi geçmişe al, eğitmen kanıt yüklesin, öğrenci şikayet etsin.
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

    # Açıklama en az 15 karakter olmalı (CreateReportHandler.MinDescriptionLength).
    Api POST "/api/sessions/$($bk.sessionId)/report" @{ reason = 'FakeProof'; description = 'Ekran görüntüsündeki saat ders saatiyle uyuşmuyor, katılımcı listesi de görünmüyor.' } $ali.accessToken | Out-Null
    Write-Host "karar bekleyen sikayet olusturuldu (ali -> veli)"
} else {
    Write-Host "acik sikayet zaten var ($acikSikayet adet)"
}

Write-Host "`nDEMO HESAPLAR (sifre: Demo12345)" -ForegroundColor Green
Write-Host "  ayse@demo.dev  / berk@demo.dev   — eslesme, sohbet, rezerve ders" -ForegroundColor Green
Write-Host "  ali@demo.dev   / veli@demo.dev   — karar bekleyen sikayet (hakem paneli)" -ForegroundColor Green
Write-Host "  admin@demo.dev                   — yonetim paneli" -ForegroundColor Green
