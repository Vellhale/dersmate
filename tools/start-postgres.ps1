# PostgreSQL'i başlatır (PeerLearn geliştirme cluster'ı)
#
# NEDEN BU BETİK GEREKLİ:
# PostgreSQL 17 binary'leri "C:\Program Files\PostgreSQL\17" altında kurulu, ancak EDB
# installer'ın servis kurulumu adımı başarısız oldu (yönetici gerektiriyor). Bu yüzden
# cluster kullanıcı alanında (%LOCALAPPDATA%) initdb ile oluşturuldu ve pg_ctl ile
# başlatılıyor — TAM İŞLEVLİ bir PostgreSQL, yalnızca Windows servisi değil.
#
# SONUÇ: Bilgisayar yeniden başlatıldığında PostgreSQL otomatik AÇILMAZ; bu betiği çalıştır.
#
# Kalıcı servis istersen (yönetici PowerShell'de, tek sefer):
#   & "C:\Program Files\PostgreSQL\17\bin\pg_ctl.exe" register -N peerlearn-pg `
#       -D "$env:LOCALAPPDATA\PeerLearnBuild\pg\data" -o "-p 5432"
#   Start-Service peerlearn-pg

$ErrorActionPreference = 'Stop'

$bin  = 'C:\Program Files\PostgreSQL\17\bin'
$data = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\pg\data'
$log  = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\pg\server.log'

if (-not (Test-Path $data)) {
    throw "Cluster bulunamadi: $data  (initdb calistirilmamis)"
}

$listening = (Test-NetConnection -ComputerName localhost -Port 5432 -WarningAction SilentlyContinue).TcpTestSucceeded
if ($listening) {
    Write-Host "PostgreSQL zaten calisiyor (5432)." -ForegroundColor Green
    exit 0
}

Write-Host "PostgreSQL baslatiliyor..."
& "$bin\pg_ctl.exe" -D "$data" -l "$log" -o "-p 5432" -w start

if ((Test-NetConnection -ComputerName localhost -Port 5432 -WarningAction SilentlyContinue).TcpTestSucceeded) {
    Write-Host "HAZIR: localhost:5432 / veritabani=peerlearn / kullanici=peerlearn" -ForegroundColor Green
} else {
    Write-Host "Baslatilamadi. Log: $log" -ForegroundColor Red
    Get-Content $log -Tail 15
}
