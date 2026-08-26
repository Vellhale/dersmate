# PeerLearn — Modül 3 (KVKK çerez rızası) ve Modül 5 (ürün turu) testi
#
# Bu iki modülün sunucu tarafı aynı tabloyu (identity.UserPreferences) paylaşıyor,
# bu yüzden tek betikte toplandı.
#
# KAPSAM DIŞI (bilerek): banner'ın görünmesi, GA4 script'inin yüklenmesi/kaldırılması ve
# turun spot ışığı yalnızca tarayıcıda anlamlıdır; bunlar arayüz üzerinden doğrulanır.
# Burada rızanın ve tur durumunun DOĞRU ve KANITLANABİLİR biçimde saklandığı sınanır.

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'

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
if (-not (Test-Path $Psql)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $Psql = $bulunan } else { $Psql = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'
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
    $file = Join-Path $env:TEMP "pl-pref-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($file, $query, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $file } finally { Remove-Item $file -Force }
}

function Send($method, $path, $body, $token) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $json = $body | ConvertTo-Json -Depth 6
    Invoke-RestMethod -Uri "$Api$path" -Method $method -Headers $headers `
        -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}

function Get_($path, $token) { Invoke-RestMethod -Uri "$Api$path" -Headers @{ Authorization = "Bearer $token" } }
function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$hwid = NewHwid
$email = "pref$stamp@test.dev"

Write-Host "PeerLearn — Modül 3 + 5 (tercihler) testi" -ForegroundColor White
Write-Host "API: $Api   koşum: $stamp"

# ---------------------------------------------------------------------------
Section 'Hazırlık'

$reg = Send Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "Pref $stamp"; hwidHash = $hwid } $null
Send Post '/api/auth/verify-email' @{ token = $reg.verificationToken } $null | Out-Null
$login = Send Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
$token = $login.accessToken
$userId = $login.userId
OK 'kullanıcı oluşturuldu ve doğrulandı'

try { Invoke-RestMethod -Uri "$Api/api/preferences" | Out-Null; Fail 'tercihler kimliksiz okunabildi' }
catch { OK "tercihler kimlik istiyor ($([int]$_.Exception.Response.StatusCode))" }

# ---------------------------------------------------------------------------
Section 'A. Çerez rızası — başlangıç durumu'

$p0 = Get_ '/api/preferences' $token
if ($p0.analyticsConsent -eq 'NotAsked' -and $p0.functionalConsent -eq 'NotAsked') {
    OK 'yeni kullanıcıda rıza "NotAsked" (satır tembel oluşturuluyor)'
} else { Fail "başlangıç: $($p0.analyticsConsent)/$($p0.functionalConsent)" }

if ($null -eq $p0.consentVersion -and $null -eq $p0.consentUpdatedAtUtc) { OK 'sürüm ve tarih boş' }
else { Fail 'boş olmayan sürüm/tarih' }

$satirVar = (Sql "SELECT COUNT(*) FROM identity.""UserPreferences"" WHERE ""UserId"" = '$userId';").Trim()
if ($satirVar -eq '0') { OK 'okuma satır AÇMADI (gereksiz yazma yok)' } else { Fail "okuma satır açtı: $satirVar" }

# ---------------------------------------------------------------------------
Section 'B. Rıza kaydı ve kanıt alanları'

Send Put '/api/preferences/cookie-consent' @{ analytics = $false; functional = $true; consentVersion = '2026-08-14' } $token | Out-Null
$p1 = Get_ '/api/preferences' $token

if ($p1.analyticsConsent -eq 'Denied') { OK 'analitik reddi kaydedildi (Denied)' } else { Fail "analitik: $($p1.analyticsConsent)" }
if ($p1.functionalConsent -eq 'Granted') { OK 'fonksiyonel onayı kaydedildi (Granted)' } else { Fail "fonksiyonel: $($p1.functionalConsent)" }
if ($p1.consentVersion -eq '2026-08-14') { OK 'metin sürümü saklandı' } else { Fail "sürüm: $($p1.consentVersion)" }
if ($p1.consentUpdatedAtUtc) { OK 'rıza tarihi saklandı (ispat yükümlülüğü)' } else { Fail 'rıza tarihi yok' }

$ipHash = (Sql "SELECT COALESCE(""ConsentIpHash"",'') FROM identity.""UserPreferences"" p JOIN identity.""Users"" u ON u.""Id""=p.""UserId"" WHERE u.""Id""='$userId';").Trim()
if ($ipHash.Length -eq 64) { OK "IP hash'lenmiş saklanıyor (64 hane, ham adres değil)" } else { Fail "IP hash uzunluğu: $($ipHash.Length)" }
if ($ipHash -notmatch '\d+\.\d+\.\d+\.\d+' -and $ipHash -notmatch '::') { OK 'kayıtta ham IP izi yok' } else { Fail 'ham IP saklanmış' }

# ---------------------------------------------------------------------------
Section 'C. Sürüm zorunluluğu (kanıt değeri)'

try {
    Send Put '/api/preferences/cookie-consent' @{ analytics = $true; functional = $true; consentVersion = '' } $token | Out-Null
    Fail 'sürümsüz rıza kabul edildi'
} catch { OK "sürümsüz rıza reddedildi ($([int]$_.Exception.Response.StatusCode))" }

$p2 = Get_ '/api/preferences' $token
if ($p2.analyticsConsent -eq 'Denied') { OK 'reddedilen istek önceki rızayı bozmadı' } else { Fail "rıza değişti: $($p2.analyticsConsent)" }

# DB kısıtı: rıza verilmişse tarih ve sürüm ZORUNLU. Kod yolu atlansa bile tutmalı.
$kisitTuttu = $false
try {
    Sql "UPDATE identity.""UserPreferences"" SET ""ConsentVersion"" = NULL WHERE ""UserId"" = '$userId';" 2>&1 | Out-Null
    $kalan = (Sql "SELECT COALESCE(""ConsentVersion"",'BOS') FROM identity.""UserPreferences"" WHERE ""UserId"" = '$userId';").Trim()
    $kisitTuttu = ($kalan -ne 'BOS')
} catch { $kisitTuttu = $true }
if ($kisitTuttu) { OK 'DB kısıtı sürümsüz rızayı engelliyor (kod atlansa bile)' } else { Fail 'DB kısıtı tutmadı' }

# ---------------------------------------------------------------------------
Section 'D. Rıza güncellenebilirlik (geri alma)'

Send Put '/api/preferences/cookie-consent' @{ analytics = $true; functional = $true; consentVersion = '2026-08-14' } $token | Out-Null
$p3 = Get_ '/api/preferences' $token
if ($p3.analyticsConsent -eq 'Granted') { OK 'rıza sonradan verilebiliyor' } else { Fail "granted olmadı: $($p3.analyticsConsent)" }

Send Put '/api/preferences/cookie-consent' @{ analytics = $false; functional = $false; consentVersion = '2026-08-14' } $token | Out-Null
$p4 = Get_ '/api/preferences' $token
if ($p4.analyticsConsent -eq 'Denied' -and $p4.functionalConsent -eq 'Denied') { OK 'rıza geri alınabiliyor' }
else { Fail "geri alma: $($p4.analyticsConsent)/$($p4.functionalConsent)" }

if ($p4.consentUpdatedAtUtc -ne $p1.consentUpdatedAtUtc) { OK 'her değişiklikte tarih tazeleniyor' } else { Fail 'tarih güncellenmedi' }

# ---------------------------------------------------------------------------
Section 'E. Ürün turu durumu'

$t0 = Get_ '/api/preferences' $token
if (-not $t0.onboardingCompleted -and -not $t0.onboardingSuppressed -and $t0.onboardingLastStep -eq 0) {
    OK 'tur başlangıçta gösterilecek durumda'
} else { Fail "tur başlangıcı: $($t0.onboardingCompleted)/$($t0.onboardingSuppressed)/$($t0.onboardingLastStep)" }

Send Put '/api/preferences/onboarding' @{ lastStep = 2; completed = $false; suppressed = $false } $token | Out-Null
$t1 = Get_ '/api/preferences' $token
if ($t1.onboardingLastStep -eq 2 -and -not $t1.onboardingCompleted) { OK 'yarıda bırakılan adım saklandı (kaldığı yerden devam)' }
else { Fail "adım: $($t1.onboardingLastStep)" }

Send Put '/api/preferences/onboarding' @{ lastStep = 3; completed = $true; suppressed = $false } $token | Out-Null
$t2 = Get_ '/api/preferences' $token
if ($t2.onboardingCompleted) { OK 'tamamlama kaydedildi' } else { Fail 'tamamlanmadı' }

$ilkTarih = (Sql "SELECT ""OnboardingCompletedAtUtc"" FROM identity.""UserPreferences"" WHERE ""UserId"" = '$userId';").Trim()

# Turu tekrar izleyip yeniden bitirmek İLK öğrenme tarihini değiştirmemeli.
Send Put '/api/preferences/onboarding' @{ lastStep = 3; completed = $true; suppressed = $false } $token | Out-Null
$ikinciTarih = (Sql "SELECT ""OnboardingCompletedAtUtc"" FROM identity.""UserPreferences"" WHERE ""UserId"" = '$userId';").Trim()
if ($ilkTarih -eq $ikinciTarih) { OK 'ilk tamamlama tarihi üzerine YAZILMIYOR' } else { Fail "tarih değişti: $ilkTarih -> $ikinciTarih" }

Send Put '/api/preferences/onboarding' @{ lastStep = 0; completed = $false; suppressed = $true } $token | Out-Null
$t3 = Get_ '/api/preferences' $token
if ($t3.onboardingSuppressed) { OK '"bir daha gösterme" kaydedildi' } else { Fail 'suppressed yazılmadı' }
if ($t3.onboardingCompleted) { OK 'tamamlanmışlık korundu (suppress ayrı bir bilgi)' } else { Fail 'tamamlanmışlık silindi' }

try {
    Send Put '/api/preferences/onboarding' @{ lastStep = -1; completed = $false; suppressed = $false } $token | Out-Null
    Fail 'negatif adım kabul edildi'
} catch { OK "negatif adım reddedildi ($([int]$_.Exception.Response.StatusCode))" }

# ---------------------------------------------------------------------------
Section 'F. Yalıtım — tercihler kullanıcıya özel'

$hwid2 = NewHwid
$email2 = "pref2$stamp@test.dev"
$reg2 = Send Post '/api/auth/register' @{ email = $email2; password = 'Demo12345'; displayName = "Pref2 $stamp"; hwidHash = $hwid2 } $null
Send Post '/api/auth/verify-email' @{ token = $reg2.verificationToken } $null | Out-Null
$login2 = Send Post '/api/auth/login' @{ email = $email2; password = 'Demo12345'; hwidHash = $hwid2 } $null

$other = Get_ '/api/preferences' $login2.accessToken
if ($other.analyticsConsent -eq 'NotAsked' -and -not $other.onboardingCompleted) {
    OK 'ikinci kullanıcı ilkinin tercihlerinden etkilenmiyor'
} else { Fail 'tercihler kullanıcılar arasında sızıyor' }

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) {
    Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green
} else {
    Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red
    exit 1
}
Write-Host "================================" -ForegroundColor White
