# Her iki API instance'ını (:5000 ve :5001) durdurur, derler ve yeniden başlatır.
#
# NEDEN AYRI BETİK:
# 1. Kod değişikliği sıcak yüklenmiyor — süreçler yeniden başlatılmadan test edilen şey
#    HÂLÂ ESKİ KOD olur. Eşzamanlılık testleri bunu fark ettirmez: eski kodla "geçti"
#    diyen bir koşum en tehlikeli sonuçtur.
# 2. Süreçleri durdururken PostgreSQL'e DOKUNULMAMALI. 16 Ağustos'ta konsol grubuna yayılan
#    Ctrl+C postmaster'ı da düşürmüştü; burada yalnızca PID ile hedefli durdurma yapılır.
# 3. "dotnet run" iki süreç demektir: ana süreç (dotnet run) ve onun başlattığı
#    PeerLearn.Api. Yalnız birini öldürmek ya derleme dosyalarını kilitli bırakır ya da
#    portu boşta gösterip süreci arkada canlı tutar.
#
# Kullanım (proje kökünden):  powershell -ExecutionPolicy Bypass -File .\tools\restart-api.ps1

$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$rootEsc = $root -replace "'", "''"      # yolda kesme işareti olabilir (C:\Users\Ada'nın\...):
                                         # tek tırnaklı dizgede ikiye katlanmazsa tırnak
                                         # orada kapanır ve komut sessizce bozulur.
$proje   = Join-Path $root 'src\PeerLearn.Api'
$logDir  = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\devlog'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

function Dinliyor($port) {
    (Test-NetConnection -ComputerName localhost -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded
}

# --- 1. DURDUR -------------------------------------------------------------
Write-Host "Mevcut instance'lar durduruluyor..." -ForegroundColor Yellow

# Uygulama süreçleri: adı sabit, PostgreSQL ile karışma riski yok.
Get-Process -Name 'PeerLearn.Api' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  durduruluyor: PeerLearn.Api (pid $($_.Id))"
    Stop-Process -Id $_.Id -Force
}

# Onları başlatan "dotnet run" süreçleri: komut satırında proje yolu geçenler.
# Filtre KOMUT SATIRINA bakar — makinedeki başka dotnet süreçlerine dokunulmaz.
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine -like '*PeerLearn.Api*' -and $_.CommandLine -like '*run*' } |
    ForEach-Object {
        Write-Host "  durduruluyor: dotnet run (pid $($_.ProcessId))"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Port gerçekten boşalana kadar bekle: hemen yeniden başlatmak "address in use" verir.
foreach ($p in 5000, 5001) {
    foreach ($i in 1..20) {
        if (-not (Dinliyor $p)) { break }
        Start-Sleep -Milliseconds 500
    }
    if (Dinliyor $p) { throw "Port $p hâlâ dinlemede — süreç durmadı." }
}
Write-Host "Portlar bos (5000, 5001)." -ForegroundColor Green

# --- 2. DERLE --------------------------------------------------------------
# Başlatmadan ÖNCE ve görünür şekilde: iki instance ayrı ayrı "dotnet run" ile derlemeye
# kalkarsa aynı çıktı klasörü için yarışırlar ve hata gizli konsol loglarına gömülür.
Write-Host "`nDerleniyor..." -ForegroundColor Yellow
& dotnet build $proje -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz (cikis kodu $LASTEXITCODE) — instance'lar baslatilmadi." }
Write-Host "Derleme tamam." -ForegroundColor Green

# --- 3. BASLAT -------------------------------------------------------------
# ASPNETCORE_ENVIRONMENT şart: --no-launch-profile ile ortam okunamazsa PRODUCTION
# varsayılır ve ProductionGuard süreci hiç başlatmaz.
function Baslat($port, $etiket) {
    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir "$etiket.out.log") `
        -RedirectStandardError  (Join-Path $logDir "$etiket.err.log") `
        -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
            "`$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project '$rootEsc\src\PeerLearn.Api' --no-launch-profile --no-build --urls http://localhost:$port"
        )
}

Write-Host "`nBaslatiliyor..." -ForegroundColor Yellow
Baslat 5000 'api'
Baslat 5001 'api2'

foreach ($p in 5000, 5001) {
    $hazir = $false
    foreach ($i in 1..60) {
        if (Dinliyor $p) { $hazir = $true; break }
        Start-Sleep -Seconds 1
    }
    if ($hazir) { Write-Host "HAZIR: $p" -ForegroundColor Green }
    else { Write-Host "ZAMAN ASIMI: $p acilmadi — $logDir icindeki loglara bak" -ForegroundColor Red }
}
