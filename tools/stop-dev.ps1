# Yerel geliştirme ortamını düzgün kapatır (start-dev.ps1'in karşılığı).
#
# NEDEN AYRI BETİK — "hepsini öldür" YETMEZ, hatta zararlıdır:
#
# 1. POSTGRESQL ÖLDÜRÜLMEZ, DURDURULUR. Stop-Process ile postmaster'ı düşürmek kirli
#    kapanma demektir; küme bir sonraki açılışta kurtarma (recovery) ile gelir ve en kötü
#    ihtimalle veri kaybı olur. 16 Ağustos'ta konsol grubuna yayılan Ctrl+C tam olarak bunu
#    yaptı ("terminated by exception 0x40010004"). Doğrusu pg_ctl stop -m fast.
#
# 2. SÜREÇLER ADA GÖRE DEĞİL KOMUT SATIRINA GÖRE seçilir. Makinede bu projeyle ilgisiz
#    node ve dotnet süreçleri var (13 node süreci sayıldı); "node'u öldür" demek
#    kullanıcının başka işini kapatmak olurdu.
#
# 3. SIRA: önce istemciler (API, Vite), sonra veritabanı. Ters sırada Postgres, açık
#    bağlantılar üstünde kapanır ve API loglarına gereksiz hata yığar.
#
# MEMURAI'YE (Redis) DOKUNULMAZ: o bir Windows servisi, otomatik başlangıçlı ve
# start-dev.ps1 tarafından da başlatılmıyor — yani bu betiğin yönettiği ortamın parçası
# değil, makinenin kalıcı bir bileşeni. Kapatmak istersen: Stop-Service Memurai (yönetici).
#
# Kullanım: powershell -ExecutionPolicy Bypass -File .\tools\stop-dev.ps1

$ErrorActionPreference = 'Stop'

function Dinliyor($port) {
    (Test-NetConnection -ComputerName localhost -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded
}

function Durdur($pid_, $etiket) {
    try {
        Stop-Process -Id $pid_ -Force -ErrorAction Stop
        Write-Host "  durduruldu: $etiket (pid $pid_)" -ForegroundColor DarkGray
    } catch {
        Write-Host "  durdurulamadi: $etiket (pid $pid_) — $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# --- 1. API (:5000, :5001) ---------------------------------------------------
Write-Host "API instance'lari durduruluyor..." -ForegroundColor Yellow
$bulundu = $false
Get-Process -Name 'PeerLearn.Api' -ErrorAction SilentlyContinue | ForEach-Object {
    $bulundu = $true; Durdur $_.Id 'PeerLearn.Api'
}
# Onları başlatan "dotnet run" süreçleri: komut satırında proje yolu geçenler.
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine -like '*PeerLearn.Api*' -and $_.CommandLine -like '*run*' } |
    ForEach-Object { $bulundu = $true; Durdur $_.ProcessId 'dotnet run' }
if (-not $bulundu) { Write-Host "  zaten kapali" -ForegroundColor DarkGray }

# --- 2. Vite (:5173) ---------------------------------------------------------
Write-Host "`nVite durduruluyor..." -ForegroundColor Yellow
$viteBulundu = $false
Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine -like '*PeerLearnBuild\frontend-dev*' -and $_.CommandLine -like '*vite*' } |
    ForEach-Object {
        $viteBulundu = $true
        # Önce sarmalayıcı PowerShell (start-dev.ps1 onu Start-Process ile açıyor), sonra node:
        # tersi sırada sarmalayıcı npm'i yeniden doğurabiliyor.
        $ebeveyn = Get-CimInstance Win32_Process -Filter "ProcessId=$($_.ParentProcessId)" -ErrorAction SilentlyContinue
        if ($ebeveyn -and $ebeveyn.Name -in @('powershell.exe', 'cmd.exe', 'node.exe')) {
            Durdur $ebeveyn.ProcessId "$($ebeveyn.Name) (vite sarmalayici)"
        }
        Durdur $_.ProcessId 'vite (node)'
    }
if (-not $viteBulundu) { Write-Host "  zaten kapali" -ForegroundColor DarkGray }

# --- 3. PostgreSQL (:5432) — EN SON ve DÜZGÜN --------------------------------
Write-Host "`nPostgreSQL durduruluyor (pg_ctl, kirli kapanma yok)..." -ForegroundColor Yellow
if (Dinliyor 5432) {
    $pgCtl = 'C:\Program Files\PostgreSQL\17\bin\pg_ctl.exe'
    $data  = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\pg\data'
    if (-not (Test-Path $pgCtl)) { throw "pg_ctl bulunamadi: $pgCtl" }

    # -m fast: açık bağlantıları keser ama CHECKPOINT alıp temiz kapanır.
    # -m smart açık bağlantı varsa süresiz bekler; -m immediate kirli kapanmadır.
    & $pgCtl -D "$data" -m fast -w stop
    if ($LASTEXITCODE -ne 0) { Write-Host "  pg_ctl cikis kodu: $LASTEXITCODE" -ForegroundColor Yellow }
} else {
    Write-Host "  zaten kapali" -ForegroundColor DarkGray
}

# --- Son durum ---------------------------------------------------------------
Write-Host "`n--- son durum ---" -ForegroundColor White
foreach ($p in 5432, 5000, 5001, 5173) {
    $acik = Dinliyor $p
    $renk = if ($acik) { 'Red' } else { 'Green' }
    Write-Host ("  {0,-6} {1}" -f $p, $(if ($acik) { 'HALA ACIK' } else { 'kapali' })) -ForegroundColor $renk
}
$memurai = Get-Service -Name 'Memurai' -ErrorAction SilentlyContinue
if ($memurai) {
    Write-Host "  6379   $($memurai.Status) (Memurai — Windows servisi, bilerek dokunulmadi)" -ForegroundColor DarkGray
}
