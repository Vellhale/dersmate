# PeerLearn — Modül 2: Hakem ve yönetim paneli uçtan uca testi
#
# Kapsam:
#   • RBAC: moderatör karara bağlayabilir, ban VEREMEZ; öğrenci panele hiç giremez.
#   • İki taraflı hakemlik: eğitmenin savunması hakem detayında görünüyor mu?
#   • İtiraz detayı: ders künyesi, taraflar, basım bekliyor mu (mintPending), geçmiş itiraz.
#   • Rezervasyonun ekonomiye DOKUNMADIĞI: öğrenci bakiyesi sabit, CreditHold yazılmıyor.
#   • Öğrenci lehine kararın karşılığı: eğitmene HİÇ puan basılmaması.
#   • Denetim izi: karar ve ban iz bırakıyor mu, karar veren rol doğru mu?
#   • Ekonomi metrikleri: defter dengesi (cüzdan toplamı == hareket toplamı).
#
# KAPSAM DIŞI BIRAKILDI — "escrow öğrenciye iade edildi" kontrolü. Yeni ekonomide öğrenci
# ders için hiçbir şey ödemiyor; bloke edilen kredi olmadığı için iade edilecek kredi de
# yok. İddianın birebir karşılığı kalmadığından silindi; yerine aynı riski kapatan iki
# kontrol kondu: rezervasyonda bakiyenin/hold'un hiç oynamadığı (B bölümü) ve öğrenci
# lehine kararda basım yapılmadığı (E bölümü).
#
# NOT: "ders yapılmadı" yolu bilinçli seçildi — kanıt yüklemesi (multipart) gerektirmez,
# böylece test kırılganlığı azalır. Kanıtlı yol zaten e2e-dispute.ps1'de kapsanıyor.
#
# NOT: Bu koşum eğitmen-öğrenci çifti başına TEK ders açar; suistimal freni (MintGuard —
# 24 saatte çift başına 2, eğitmen başına 8 ders) bu yüzden tetiklenmiyor. Buraya ikinci
# bir ders eklenecekse ya ayrı kullanıcı çifti kurulmalı ya da ScheduledStartUtc 24 saatlik
# pencerenin dışına taşınmalı; yoksa rezervasyon 429 / MINT_LIMIT_REACHED ile düşer.
#
# Betikler idempotent DEĞİLDİR: HWID ve isimler koşuma özel üretilir (ban kalıcıdır).

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

$script:Pass = 0
$script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Sql($query) {
    if (-not $PSQL) {
        # Docker yolu: sorgu STDIN den geçer. -c ile argüman olarak geçirmek,
        # identity."Users" gibi tırnaklı adlardaki tırnakları kabuğa yedirir.
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return ($out -join '').Trim()
    }
    $file = Join-Path $env:TEMP "peerlearn-mod-$([Guid]::NewGuid().ToString('N')).sql"
    # Gömülü tırnaklar native argüman geçişinde kayboluyor; dosyadan çalıştırılır.
    [IO.File]::WriteAllText($file, $query, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $file } finally { Remove-Item $file -Force }
}

function Post($path, $body, $token) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $json = $body | ConvertTo-Json -Depth 6
    # PowerShell 5.1 gövdeyi Latin-1 gönderir; Türkçe karakterler bozulmasın diye UTF-8 bayt.
    Invoke-RestMethod -Uri "$Api$path" -Method Post -Headers $headers `
        -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}

function Get_($path, $token) {
    Invoke-RestMethod -Uri "$Api$path" -Headers @{ Authorization = "Bearer $token" }
}

function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }

function NewUser($prefix, $stamp) {
    $hwid = NewHwid
    $email = "$prefix$stamp@test.dev"
    $reg = Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix $stamp"; hwidHash = $hwid } $null
    Post '/api/auth/verify-email' @{ token = $reg.verificationToken } $null | Out-Null
    $login = Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    return [pscustomobject]@{ Email = $email; Hwid = $hwid; Token = $login.accessToken; UserId = $login.userId }
}

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

Write-Host "PeerLearn — Modül 2 (hakem paneli) testi" -ForegroundColor White
Write-Host "API: $Api   koşum: $stamp"

# ---------------------------------------------------------------------------
Section 'Hazırlık: kullanıcılar, rol, konu'

$tutor = NewUser 'modtutor' $stamp
$student = NewUser 'modstudent' $stamp
$admin = NewUser 'modadmin' $stamp
$moder = NewUser 'modmoder' $stamp
OK 'dört kullanıcı oluşturuldu (eğitmen, öğrenci, admin, moderatör)'

Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($admin.UserId)';" | Out-Null
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Moderator' WHERE ""Id"" = '$($moder.UserId)';" | Out-Null

# Rol değişikliği token'a ancak yeni girişte yansır.
$admin.Token = (Post '/api/auth/login' @{ email = $admin.Email; password = 'Demo12345'; hwidHash = $admin.Hwid } $null).accessToken
$moderLogin = Post '/api/auth/login' @{ email = $moder.Email; password = 'Demo12345'; hwidHash = $moder.Hwid } $null
$moder.Token = $moderLogin.accessToken

if ($moderLogin.role -eq 'Moderator') { OK 'moderatör rolü token''a yansıdı' } else { Fail "moderatör rolü: $($moderLogin.role)" }
if ($moderLogin.isAdmin) { OK 'moderatör yönetim panelini görebiliyor (isAdmin türetilmiş)' } else { Fail 'moderatör isAdmin=false' }

# Koşuma özel konu: paylaşılan katalog konuları birikmiş test verisiyle kirleniyor.
$topicName = "Mod Konu $stamp"
Sql @"
INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT gen_random_uuid(), s."Id", '$topicName', 999, TRUE, now()
FROM catalog."Subjects" s ORDER BY s."Name" LIMIT 1;
"@ | Out-Null
$topicId = (Sql "SELECT ""Id"" FROM catalog.""Topics"" WHERE ""Name"" = '$topicName';").Trim()
OK "koşuma özel konu: $topicName"

# ---------------------------------------------------------------------------
Section 'A. Yetki sınırları (RBAC)'

try { Get_ '/api/admin/disputes' $student.Token | Out-Null; Fail 'öğrenci itiraz kuyruğunu gördü' }
catch { OK "öğrenci panele giremiyor ($([int]$_.Exception.Response.StatusCode))" }

try { Get_ '/api/admin/metrics' $moder.Token | Out-Null; OK 'moderatör metrikleri görüyor' }
catch { Fail "moderatör metrikleri göremedi: $($_.Exception.Message)" }

try {
    Post "/api/admin/users/$($student.UserId)/ban" @{ reason = 'yetki testi' } $moder.Token | Out-Null
    Fail 'moderatör BAN verebildi (yetki ayrımı yok)'
} catch { OK "moderatör ban veremiyor ($([int]$_.Exception.Response.StatusCode))" }

# ---------------------------------------------------------------------------
<#
  B–E BÖLÜMLERİ KALDIRILDI (2026-08-18).

  Bunlar itiraz hakemliğini test ediyordu: itiraz açma, eğitmenin savunması, hakem
  detay ekranı ve karar + denetim izi. İtiraz tek yönlü şikayete dönüştürülünce bu
  yeteneklerin hiçbiri kalmadı — itiraz AÇILAMIYOR, savunma ucu YOK.

  Yeni akışın karşılığı: tools/e2e-report.ps1 (gizlilik, tekillik, basımı engellememe,
  yönetim kuyruğu ve denetim izi dahil 22 kontrol).

  Bu paketin geri kalanı — RBAC (A), ban + denetim izi (F), ekonomi metrikleri (G) —
  itirazdan bağımsızdı ve olduğu gibi duruyor. Ban bölümü kendi kullanıcısını kuruyor,
  B'de açılan derse bağımlı değil.
#>


Section 'F. Ban + denetim izi'

$banResult = Post "/api/admin/users/$($tutor.UserId)/ban" @{ reason = "Modül 2 testi: sahte ders iddiası" } $admin.Token
OK "admin banladı, $($banResult.devicesBanned) cihaz engellendi"

$logBan = Get_ '/api/admin/audit-log?page=1&pageSize=5' $admin.Token
$banEntry = $logBan.items | Where-Object { $_.action -eq 'UserBanned' -and $_.targetId -eq $tutor.UserId } | Select-Object -First 1
if ($banEntry) { OK "ban denetim izinde: $($banEntry.summary)" } else { Fail 'ban denetim izine düşmedi' }
if ($banEntry -and $banEntry.actorRole -eq 'Admin') { OK 'ban Admin rolüyle kaydedildi' } else { Fail 'ban rolü hatalı' }

try { Post '/api/auth/login' @{ email = $tutor.Email; password = 'Demo12345'; hwidHash = $tutor.Hwid } $null | Out-Null; Fail 'banlı kullanıcı giriş yapabildi' }
catch { OK 'banlı kullanıcı giremiyor' }

# ---------------------------------------------------------------------------
Section 'G. Ekonomi metrikleri'

$m = Get_ '/api/admin/metrics' $admin.Token
# ledgerBalanced'in TANIMI değişti: artık "basılan − yakılan" değil, doğrudan
# SUM(Wallets.Available+Locked) == SUM(CreditTransactions.Amount). Sıfır toplam ekonomi
# bittiği için eski formül puan basıldıkça kendiliğinden tutmaz olurdu.
if ($m.ledgerBalanced) { OK "defter dengede (dolaşım $($m.circulatingCredits) == hareket toplamı)" }
else { Fail "DEFTER TUTMUYOR: dolaşım=$($m.circulatingCredits) kullanılabilir=$($m.availableCredits) bloke=$($m.lockedCredits)" }
if ($m.bannedUsers -ge 1) { OK "banlı kullanıcı sayısı yansıyor ($($m.bannedUsers))" } else { Fail 'banlı kullanıcı sayısı 0' }
if ($m.walletCount -gt 0) { OK "cüzdan sayısı: $($m.walletCount)" } else { Fail 'cüzdan sayısı 0' }

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) {
    Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green
} else {
    Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red
    exit 1
}
Write-Host "================================" -ForegroundColor White
