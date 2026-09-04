# PeerLearn — üretim kapısı ve hız sınırı doğrulaması
#
# NEDEN AYRI BİR BETİK (e2e paketlerinden değil): ikisi de AÇILIŞ davranışını sınıyor —
# uygulamayı farklı ortam/ayarla yeniden başlatmayı gerektiriyorlar. Çalışan API'ye istek
# atan e2e paketlerine sığmazlar.
#
# Kullanım: powershell -ExecutionPolicy Bypass -File .\tools\verify-production-guard.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$api = Join-Path $root 'src\PeerLearn.Api'

$script:Pass = 0; $script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
Section 'A. Üretim kapısı: geliştirme ayarlarıyla açılmamalı'

# Production ortamında, depodaki ayarlarla başlatmayı dene. Kapı çalışıyorsa süreç
# açılmadan hata verir; çalışmıyorsa uygulama güvensiz biçimde ayağa kalkar.
# --no-launch-profile ŞART: launchSettings.json ASPNETCORE_ENVIRONMENT=Development yazıyor
# ve ortam değişkenimizi eziyordu — kapı hiç çalışmadan süreç :5000'e bağlanmaya çalışıyordu.
$env:ASPNETCORE_ENVIRONMENT = 'Production'

# Sürecin ÇÖKMESİ beklenen sonuç: ErrorActionPreference'ı geçici olarak gevşetmezsek
# betiğin kendisi o çöküşte durur ve asıl kontroller hiç koşmaz.
$eskiTercih = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$cikti = & dotnet run --project $api --no-build --no-launch-profile --urls http://localhost:5099 2>&1 | Out-String
$ErrorActionPreference = $eskiTercih

Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue

if ($cikti -match 'ÜRETİM AYARLARI GÜVENLİ DEĞİL') { OK 'kapı açılışı durdurdu' }
else { Fail "kapı devreye girmedi. Çıktı: $($cikti.Substring(0, [Math]::Min(400, $cikti.Length)))" }

foreach ($beklenen in @('Jwt:Key', 'ExposeVerificationTokenInResponse', 'ConnectionStrings:Postgres', 'Cors:Origins', 'Email:Provider')) {
    if ($cikti -match [regex]::Escape($beklenen)) { OK "sorun bildirildi: $beklenen" }
    else { Fail "bu sorun bildirilmedi: $beklenen" }
}

# ---------------------------------------------------------------------------
Section 'B. Hız sınırı: kimlik ucunda tetikleniyor'

# Sınırı DÜŞÜK bir değerle geçici olarak devreye alıp gerçekten 429 döndüğünü görüyoruz.
# Geliştirme appsettings'i sınırı bilerek yüksek tutuyor (testler yüzlerce istek atıyor),
# bu yüzden burada ortam değişkeniyle eziliyor.
# Ortam AÇIKÇA Development: --no-launch-profile ile ASPNETCORE_ENVIRONMENT hiç set edilmez
# ve ASP.NET varsayılan olarak Production'a düşer — o zaman üretim kapısı devreye girip
# sunucu hiç açılmaz. (Bu varsayılan doğru ve güvenli; burada bilerek geçiliyor.)
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:RateLimit__AuthPerMinute = '3'

# Yol TIRNAKLANMALI: proje dizininde boşluk olabilir ve Start-Process argümanı tırnaksız
# verilince yolu ilk boşluktan böler — süreç anlamsız bir hatayla düşer.
$sunucu = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', "`"$api`"", '--no-build', '--no-launch-profile', '--urls', 'http://localhost:5098') `
    -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $env:TEMP 'pl-rl-out.txt') `
    -RedirectStandardError (Join-Path $env:TEMP 'pl-rl-err.txt')

try {
    $hazir = $false
    foreach ($deneme in 1..30) {
        Start-Sleep -Seconds 1
        try { Invoke-RestMethod -Uri 'http://localhost:5098/health' -TimeoutSec 2 | Out-Null; $hazir = $true; break } catch { }
    }
    if (-not $hazir) { Fail 'test sunucusu ayağa kalkmadı'; return }
    OK 'düşük sınırla test sunucusu hazır'

    $kodlar = @()
    foreach ($i in 1..8) {
        try {
            Invoke-RestMethod -Uri 'http://localhost:5098/api/auth/login' -Method Post `
                -ContentType 'application/json' `
                -Body '{"email":"yok@test.dev","password":"YanlisSifre1","hwidHash":"aa"}' -TimeoutSec 5 | Out-Null
            $kodlar += 200
        } catch { $kodlar += [int]$_.Exception.Response.StatusCode }
    }

    $limitli = @($kodlar | Where-Object { $_ -eq 429 }).Count
    if ($limitli -gt 0) { OK "sınır tetiklendi: $limitli/8 istek 429 aldı (kodlar: $($kodlar -join ','))" }
    else { Fail "hiç 429 gelmedi (kodlar: $($kodlar -join ','))" }

    # İlk istekler sınıra takılmamalı: sınır çalışıyor ama normal kullanımı da boğmuyor.
    if ($kodlar[0] -ne 429) { OK 'ilk istek sınıra takılmadı' } else { Fail 'daha ilk istek 429 aldı' }

    # Sağlık ucu sınır DIŞI olmalı (yük dengeleyicinin yoklaması kullanıcı trafiğiyle
    # aynı kovaya girerse yoğunlukta sağlıklı instance havuzdan düşer).
    $saglik = @()
    foreach ($i in 1..15) {
        try { Invoke-RestMethod -Uri 'http://localhost:5098/health' -TimeoutSec 3 | Out-Null; $saglik += 200 }
        catch { $saglik += [int]$_.Exception.Response.StatusCode }
    }
    if (@($saglik | Where-Object { $_ -eq 429 }).Count -eq 0) { OK 'sağlık ucu sınır dışı kaldı' }
    else { Fail 'sağlık ucu da sınırlandı' }
}
finally {
    if ($sunucu -and -not $sunucu.HasExited) { Stop-Process -Id $sunucu.Id -Force }
    Get-Process -Name 'PeerLearn.Api' -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-5) } | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item Env:\RateLimit__AuthPerMinute, Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
Section 'C. Genel sınır KULLANICI başına bölümleniyor (CGNAT)'

<#
  NEDEN BU TEST VAR: genel sınır bir dönem IP başına bölümleniyordu. Mobil operatörler
  CGNAT kullandığı için binlerce abone tek genel IP paylaşıyor; tek IP'nin 300/dk tavanı
  ~15-30 EŞZAMANLI kullanıcıda doluyordu. Mağazaya çıkıldığında ilk 429 dalgası giriş
  ekranından değil uygulama içi gezinmeden gelecekti.

  ⚠️ ESKİ TEST BU TUZAĞI YAKALAYAMAZDI. B bölümü tek IP'den istek atıyor; tek kova ile
  IP başına kova aynı sonucu verir. Bölümlemeyi sınamanın tek yolu İKİ FARKLI ANAHTAR
  kullanmak — ve artık anahtar kullanıcı olduğu için bu mümkün: aynı makineden, aynı
  IP'den, İKİ AYRI kullanıcı.

  Kanıt şu: A kendi kovasını doldurup 429 alıyor, hemen ardından B 200 alıyor. Bölümleme
  IP'ye dönerse B de 429 alır ve test kırılır.
#>

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:RateLimit__GlobalPerMinute = '30'
$env:RateLimit__AuthPerMinute = '60'   # kurulum (2 kayıt + 2 doğrulama + 2 giriş) sığsın

$sunucuC = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', "`"$api`"", '--no-build', '--no-launch-profile', '--urls', 'http://localhost:5097') `
    -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $env:TEMP 'pl-rl2-out.txt') `
    -RedirectStandardError (Join-Path $env:TEMP 'pl-rl2-err.txt')

try {
    $B = 'http://localhost:5097'
    $hazir = $false
    foreach ($deneme in 1..30) {
        Start-Sleep -Seconds 1
        try { Invoke-RestMethod -Uri "$B/health" -TimeoutSec 2 | Out-Null; $hazir = $true; break } catch { }
    }
    if (-not $hazir) { Fail 'C: test sunucusu ayağa kalkmadı' }
    else {
        OK 'C: düşük genel sınırla sunucu hazır'

        function YeniKullanici($onek) {
            $hwid = -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] })
            $eposta = "$onek$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())$(Get-Random -Max 999)@test.dev"
            $govde = @{ email = $eposta; password = 'Demo12345'; displayName = "$onek test"; termsVersion = (& "$PSScriptRoot\yasal-surum.ps1"); ageConfirmed = $true; hwidHash = $hwid } | ConvertTo-Json
            $kayit = Invoke-RestMethod -Uri "$B/api/auth/register" -Method Post -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($govde))
            $dog = @{ email = $eposta; code = $kayit.verificationToken } | ConvertTo-Json
            Invoke-RestMethod -Uri "$B/api/auth/verify-email" -Method Post -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($dog)) | Out-Null
            $giris = @{ email = $eposta; password = 'Demo12345'; hwidHash = $hwid } | ConvertTo-Json
            $o = Invoke-RestMethod -Uri "$B/api/auth/login" -Method Post -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($giris))
            return [pscustomobject]@{ Token = $o.accessToken; UserId = $o.userId }
        }

        $A = YeniKullanici 'cgnatA'
        $C = YeniKullanici 'cgnatB'
        OK 'C: aynı IP''den iki kullanıcı oluşturuldu'

        # A kendi kovasını doldurana kadar bas (sınır 30; 40 istek kesin taşırır).
        $aKodlari = @()
        foreach ($i in 1..40) {
            try {
                Invoke-RestMethod -Uri "$B/api/users/$($A.UserId)/profile" -Headers @{ Authorization = "Bearer $($A.Token)" } -TimeoutSec 5 | Out-Null
                $aKodlari += 200
            } catch { $aKodlari += [int]$_.Exception.Response.StatusCode }
        }
        $aRed = @($aKodlari | Where-Object { $_ -eq 429 }).Count
        if ($aRed -gt 0) { OK "C: A kendi kovasını doldurdu ($aRed/40 istek 429)" }
        else { Fail "C: A hiç 429 almadı — sınır hiç uygulanmıyor (kodlar: $($aKodlari -join ','))" }

        # ASIL İDDİA: B, A'nın kovasından etkilenmemeli. IP'ye dönülürse burası kırılır.
        $bKod = 0
        try {
            Invoke-RestMethod -Uri "$B/api/users/$($C.UserId)/profile" -Headers @{ Authorization = "Bearer $($C.Token)" } -TimeoutSec 5 | Out-Null
            $bKod = 200
        } catch { $bKod = [int]$_.Exception.Response.StatusCode }

        if ($bKod -eq 200) {
            OK 'C: ikinci kullanıcı ETKİLENMEDİ — bölümleme kullanıcı başına'
        } else {
            Fail "C: ikinci kullanıcı $bKod aldı — bölümleme IP başına düşmüş (CGNAT arızası geri geldi)"
        }
    }
}
finally {
    if ($sunucuC -and -not $sunucuC.HasExited) { Stop-Process -Id $sunucuC.Id -Force }
    Get-Process -Name 'PeerLearn.Api' -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-5) } | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item Env:\RateLimit__GlobalPerMinute, Env:\RateLimit__AuthPerMinute, Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
Section 'D. Sınırı bilerek yükseltme kaldıracı'

<#
  Kapı tek yönlüydü: 10'un üstünde bir değerle uygulama HİÇ açılmıyordu. Amacı doğruydu
  (geliştirme değeri sızmasın) ama olay anında hiçbir hafifletme bırakmıyordu — CGNAT
  kaynaklı bir 429 dalgasında kod değişikliği + derleme + dağıtım gerekiyordu.

  Bayrak kapıyı KALDIRMIYOR, AÇIK BEYAN istiyor: sızma kazayla olur, bu bayrak kazayla
  yazılmaz. İki yönlü sınanıyor — bayraksız engelliyor, bayrakla geçiyor.
#>

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:RateLimit__AuthPerMinute = '999'
$eskiTercih2 = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

$ciktiD1 = & dotnet run --project $api --no-build --no-launch-profile --urls http://localhost:5096 2>&1 | Out-String
if ($ciktiD1 -match 'RateLimit:AuthPerMinute') { OK 'D: bayraksız — yüksek sınır engellendi' }
else { Fail 'D: bayraksız yüksek sınır bildirilmedi' }

$env:RateLimit__YuksekSinirBilerek = 'true'
$ciktiD2 = & dotnet run --project $api --no-build --no-launch-profile --urls http://localhost:5096 2>&1 | Out-String
$ErrorActionPreference = $eskiTercih2

if ($ciktiD2 -notmatch 'RateLimit:AuthPerMinute') { OK 'D: bayrakla — yüksek sınıra izin verildi' }
else { Fail 'D: bayrak verildi ama sınır yine engellendi' }

# Sessizce geçmemeli: kapatılan koruma günlükte iz bırakmalı.
if ($ciktiD2 -match 'YuksekSinirBilerek' -or $ciktiD2 -match 'hız sınırı güvenli tavanın') { OK 'D: uyarı loglandı' }
else { Fail 'D: bayrak sessizce kabul edildi, uyarı yok' }

# Kapının GERİ KALANI hâlâ çalışmalı — bayrak yalnızca hız sınırını açıyor.
if ($ciktiD2 -match 'ÜRETİM AYARLARI GÜVENLİ DEĞİL') { OK 'D: kapının diğer kontrolleri hâlâ engelliyor' }
else { Fail 'D: bayrak tüm kapıyı devre dışı bıraktı' }

Remove-Item Env:\RateLimit__AuthPerMinute, Env:\RateLimit__YuksekSinirBilerek, Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue

Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green }
else { Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red; exit 1 }
Write-Host "================================" -ForegroundColor White
