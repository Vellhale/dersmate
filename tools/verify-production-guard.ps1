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

Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green }
else { Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red; exit 1 }
Write-Host "================================" -ForegroundColor White
