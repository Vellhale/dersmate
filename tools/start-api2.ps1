# İkinci API instance'ı (:5001) — eşzamanlılık testi için.
#
# NEDEN AYRI BETİK: instance ikinci bir launch profili olmadan başlatılıyor
# (--no-launch-profile), bu durumda ASP.NET ortamı ASPNETCORE_ENVIRONMENT'tan okur ve
# değişken yoksa PRODUCTION varsayar. Üretim ayar kapısı (ProductionGuard) da haklı olarak
# süreci hiç başlatmaz. Ortam değişkenini komut satırı argümanıyla değil, doğrudan burada
# set etmek tek güvenilir yol.
#
# Kullanım: powershell -ExecutionPolicy Bypass -File .\tools\start-api2.ps1

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$env:ASPNETCORE_ENVIRONMENT = 'Development'

Write-Host "İkinci instance başlatılıyor (http://localhost:5001)…"
& dotnet run --project (Join-Path $root 'src\PeerLearn.Api') --no-launch-profile --urls http://localhost:5001
