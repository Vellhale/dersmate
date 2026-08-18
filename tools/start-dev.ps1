# Yerel geliştirme ortamını AYRI KONSOLLARDA başlatır (API :5000 + Vite :5173).
#
# NEDEN AYRI KONSOL — bu betiğin varlık sebebi:
# Süreçler bu kabuğun konsoluna bağlı başlatılırsa, kabuk kapandığında Windows tüm
# konsol grubuna CTRL_CLOSE/CTRL_C yayar ve süreçler birlikte ölür. 16 Ağustos'ta tam
# olarak bu oldu: bir arka plan görevinin kabuğu kapanınca postmaster
# "terminated by exception 0x40010004" (DBG_CONTROL_C) ile düştü, PostgreSQL kirli
# kapandı, API ve Vite de aynı sinyalle gitti. Start-Process her sürece KENDİ (gizli)
# konsolunu verir; bizim konsol olaylarımız onlara ulaşmaz.
#
# Kullanım: powershell -ExecutionPolicy Bypass -File .\tools\start-dev.ps1

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent

# Proje yolu KESME İŞARETİ içeriyor: "G:\Drive'ım\...". Alt sürece geçirilen komut
# metninde yol tek tırnak içine alınırsa tırnak tam orada kapanır ve komut sessizce
# bozulur (süreç hiç başlamaz, hata da görünmez). PowerShell'de tek tırnaklı dizgede
# kesme işareti ikiye katlanarak kaçırılır.
$rootEsc = $root -replace "'", "''"

$logDir = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\devlog'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

function Dinliyor($port) {
    (Test-NetConnection -ComputerName localhost -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded
}

# --- PostgreSQL ---
if (Dinliyor 5432) {
    Write-Host "PostgreSQL zaten calisiyor (5432)." -ForegroundColor Green
} else {
    Write-Host "PostgreSQL baslatiliyor..."
    $bin  = 'C:\Program Files\PostgreSQL\17\bin'
    $data = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\pg\data'
    $log  = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\pg\server.log'
    Start-Process -FilePath "$bin\pg_ctl.exe" `
        -ArgumentList @('-D', "`"$data`"", '-l', "`"$log`"", '-o', '"-p 5432"', '-w', 'start') `
        -WindowStyle Hidden -Wait
}

# --- API (:5000) ---
if (Dinliyor 5000) {
    Write-Host "API zaten calisiyor (5000)." -ForegroundColor Green
} else {
    Write-Host "API baslatiliyor (http://localhost:5000)..."
    # ASPNETCORE_ENVIRONMENT şart: --no-launch-profile ile ortam okunamazsa PRODUCTION
    # varsayılır ve ProductionGuard süreci hiç başlatmaz (bkz. start-api2.ps1).
    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir 'api.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'api.err.log') `
        -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
            "`$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project '$rootEsc\src\PeerLearn.Api' --no-launch-profile --urls http://localhost:5000"
        )
}

# --- Frontend (:5173) ---
if (Dinliyor 5173) {
    Write-Host "Frontend zaten calisiyor (5173)." -ForegroundColor Green
} else {
    Write-Host "Frontend baslatiliyor (http://localhost:5173)..."
    # Vite, Drive'daki klasörden DEĞİL yerel çalışma alanından koşar: node_modules Google
    # Drive'a kurulamıyor, bu yüzden frontend\setup-dev.ps1 yerel bir çalışma alanı açıp
    # "src"/"public" klasörlerini Drive'daki kaynağa junction'lıyor. Drive yolundan
    # çalıştırılırsa "'vite' is not recognized" hatası alınır.
    $feDev = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\frontend-dev'
    if (-not (Test-Path (Join-Path $feDev 'node_modules'))) {
        throw "Frontend calisma alani kurulmamis: $feDev  (once frontend\setup-dev.ps1 calistir)"
    }

    <#
      YAPILANDIRMA DOSYALARI HER BASLANGICTA YENIDEN KOPYALANIR.

      "src" ve "public" Drive'a junction, ama index.html / vite.config.js / tailwind.config.js
      / .env.development KOPYADIR (bkz. frontend\setup-dev.ps1). Yani Drive'da index.html'i
      duzenlemek Vite'a HIC ULASMAZ ve degisiklik sessizce yok sayilir — sayfa baslgini
      degistirip "neden olmadi" diye aramak tam olarak bu yuzden oluyor.

      Kopyalama tek yonlu: Drive kaynak, calisma alani turev.
    #>
    foreach ($cfg in 'index.html', 'package.json', 'vite.config.js', 'tailwind.config.js',
                     'postcss.config.js', '.env.development', 'playwright.config.js') {
        $kaynak = Join-Path $root "frontend\$cfg"
        if (Test-Path -LiteralPath $kaynak) { Copy-Item -LiteralPath $kaynak -Destination $feDev -Force }
    }
    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir 'vite.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'vite.err.log') `
        -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
            "Set-Location '$($feDev -replace "'", "''")'; npm run dev"
        )
}

# --- Hazır olana kadar bekle ---
foreach ($p in 5432, 5000, 5173) {
    $hazir = $false
    foreach ($i in 1..60) {
        if (Dinliyor $p) { $hazir = $true; break }
        Start-Sleep -Seconds 2
    }
    if ($hazir) { Write-Host "HAZIR: $p" -ForegroundColor Green }
    else { Write-Host "ZAMAN ASIMI: $p acilmadi" -ForegroundColor Red }
}
