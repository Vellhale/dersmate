# Redis (Memurai) kurulumunu tamamlar ve çok instance'lı testi hazırlar.
#
# NEDEN YÖNETİCİ GEREKİYOR:
# Memurai MSI'ı bir Windows servisi kaydeder; bu adım yükseltme (UAC) ister.
# Yükseltilmemiş oturumda kurulum UAC onayı beklerken takılır ve diğer tüm MSI
# işlemlerini 1618 (ERROR_INSTALL_ALREADY_RUNNING) ile bloke eder.
#
# KULLANIM: Bu betiği YÖNETİCİ PowerShell'de çalıştırın.
#   Başlat > "PowerShell" > sağ tık > "Yönetici olarak çalıştır"
#   powershell -ExecutionPolicy Bypass -File tools\setup-redis.ps1

$ErrorActionPreference = 'Stop'

# KENDİNİ YÜKSELTİR: normal kabukta çalıştırıldığında sessizce başarısız olmak yerine
# UAC penceresi açar. (Önceki sürüm yalnızca uyarıp çıkıyordu; mesaj gözden kaçınca
# "kurdum" sanılıp hiçbir şey kurulmamış oluyordu.)
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Yonetici hakki gerekiyor — UAC penceresi acilacak, 'Evet' deyin." -ForegroundColor Yellow
    try {
        $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
        )
        if ($p.ExitCode -ne 0) { Write-Host "Yukseltilmis kurulum basarisiz (cikis $($p.ExitCode))." -ForegroundColor Red }
        exit $p.ExitCode
    } catch {
        Write-Host "UAC reddedildi ya da acilamadi: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Elle: Baslat > PowerShell > sag tik > 'Yonetici olarak calistir'"
        exit 1
    }
}

Write-Host "Yonetici olarak calisiyor." -ForegroundColor Green

# Önceki denemeden kalan TAKILI MSI oturumu varsa temizle. Bu şart: UAC onayı almadan
# asılı kalan bir msiexec, sonraki tüm kurulumları 1618 (ERROR_INSTALL_ALREADY_RUNNING)
# ile reddettirir ve normal kullanıcı onu sonlandıramaz.
$stuck = Get-Process msiexec -ErrorAction SilentlyContinue
if ($stuck) {
    Write-Host "Takili $($stuck.Count) msiexec sureci temizleniyor..."
    $stuck | ForEach-Object { try { Stop-Process -Id $_.Id -Force -Confirm:$false } catch {} }
    Start-Sleep -Seconds 3
}

Write-Host "Memurai Developer kuruluyor (Redis protokolu uyumlu)..."
winget install --id Memurai.MemuraiDeveloper --silent --accept-package-agreements --accept-source-agreements
if ($LASTEXITCODE -ne 0) { throw "winget kurulumu basarisiz (cikis $LASTEXITCODE)" }

$svc = Get-Service | Where-Object { $_.Name -like '*memurai*' } | Select-Object -First 1
if ($svc) {
    if ($svc.Status -ne 'Running') { Start-Service $svc.Name }
    Write-Host "Servis: $($svc.Name) = $((Get-Service $svc.Name).Status)" -ForegroundColor Green
}

Start-Sleep -Seconds 3
$listening = (Test-NetConnection -ComputerName localhost -Port 6379 -WarningAction SilentlyContinue).TcpTestSucceeded
if (-not $listening) { throw "6379 portu dinlemiyor — servisi kontrol edin." }

Write-Host "`nRedis 6379 dinliyor." -ForegroundColor Green
Write-Host "Simdi appsettings.json icindeki ConnectionStrings:Redis degerini doldurun:" -ForegroundColor Cyan
Write-Host '  "Redis": "localhost:6379"' -ForegroundColor Cyan
Write-Host "`nArdindan (yonetici OLMAYAN kabukta):" -ForegroundColor Cyan
Write-Host "  1) Iki instance'i baslatin (5000 ve 5001)"
Write-Host "  2) node tools\signalr-backplane-probe.js      # backplane testi"
Write-Host "  3) powershell -File tools\e2e-concurrency.ps1 # dagitik kilit testi"
