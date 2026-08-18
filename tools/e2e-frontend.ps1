# Frontend uçtan uca testleri (Playwright, frontend/e2e).
#
# NEDEN SARMALAYICI — iki Drive'a özgü tuzak var, ikisi de "npm run test:e2e" yazınca
# anlaşılmaz hatalarla çıkıyor:
#
# 1. NODE_OPTIONS=--preserve-symlinks ŞART.
#    e2e klasörü çalışma alanında bir JUNCTION (kaynak Drive'da, node_modules yerel diskte).
#    Node varsayılan olarak modülleri GERÇEK yola çözer: spec dosyası G:\...\frontend\e2e
#    altında görünür, oradan yukarı çıkıp node_modules arar ve bulamaz. Hata da yanıltıcıdır:
#    "Cannot find package '@playwright/test'" — sanki paket kurulmamış gibi.
#    Junction yerine kopya kullanmak bunu çözerdi ama testler sessizce eskirdi; tek kaynak
#    Drive'da kalsın diye bayrak tercih edildi.
#
# 2. KOŞUM YERİ çalışma alanı, Drive DEĞİL. node_modules Drive'a kurulamıyor (EBADF).
#
# Ön koşul (bir kez):  cd <calisma-alani>; npm install; npx playwright install chromium
# Backend GEREKMEZ — API taklit ediliyor (bkz. frontend/e2e/README.md).

$ErrorActionPreference = 'Stop'

$dev = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\frontend-dev'
if (-not (Test-Path (Join-Path $dev 'node_modules\@playwright\test'))) {
    Write-Host "@playwright/test kurulu degil. Once:" -ForegroundColor Yellow
    Write-Host "  cd '$dev'; npm install; npx playwright install chromium" -ForegroundColor Yellow
    exit 1
}

# e2e junction'i yoksa kur: setup-dev.ps1 bunu yapar ama eski calisma alanlarinda eksik olabilir.
$link = Join-Path $dev 'e2e'
if (-not (Test-Path $link)) {
    $kaynak = Join-Path (Split-Path $PSScriptRoot -Parent) 'frontend\e2e'
    cmd /c mklink /J "$link" "$kaynak" | Out-Null
    Write-Host "e2e junction olusturuldu." -ForegroundColor DarkGray
}
Copy-Item (Join-Path (Split-Path $PSScriptRoot -Parent) 'frontend\playwright.config.js') -Destination $dev -Force

$env:NODE_OPTIONS = '--preserve-symlinks'
Push-Location $dev
try {
    & npx playwright test @args
    $kod = $LASTEXITCODE
} finally {
    Pop-Location
    Remove-Item Env:\NODE_OPTIONS -ErrorAction SilentlyContinue
}

if ($kod -eq 0) { Write-Host "`nFRONTEND E2E: TUM TESTLER GECTI" -ForegroundColor Green }
else { Write-Host "`nFRONTEND E2E: BASARISIZ (cikis kodu $kod)" -ForegroundColor Red }
exit $kod
