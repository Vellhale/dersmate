# PeerLearn — Yönetim eliyle puan tanımlama/düzeltme (F3)
#
# Kapsam:
#   • Yetki: yalnızca Admin (moderatör ve normal kullanıcı reddedilir)
#   • Doğrulama: gerekçe zorunlu, sıfır tutar yok, üst sınır
#   • Ekleme: lot açılır (AdminGrant, VADESİZ), hareket yazılır (AdminAdjustment)
#   • Düşme: lotlar FIFO tüketilir, bakiyeyi aşan düşme reddedilir
#   • DEĞİŞMEZLER: cüzdan = lot toplamı, cüzdan toplamı = defter toplamı
#   • UNVAN SAYACI DEĞİŞMEZ — yönetim eliyle unvan dağıtılamaz
#   • Denetim izi: her düzeltme AdminActionLogs'a CreditAdjusted olarak düşer

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

$script:Pass = 0; $script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Sql($q) {
    if (-not $Psql) {
        # Docker yolu: sorgu STDIN den geçer. -c ile argüman olarak geçirmek,
        # identity."Users" gibi tırnaklı adlardaki tırnakları kabuğa yedirir.
        $out = $q | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return ($out -join '').Trim()
    }
    $f = Join-Path $env:TEMP "pl-adm-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $f } finally { Remove-Item $f -Force }
}
function SqlInt($q) { [int](Sql $q).Trim() }

function Send($method, $path, $body, $token, $key) {
    $h = @{}; if ($token) { $h['Authorization'] = "Bearer $token" }
    # Puan düzeltme ucu Idempotency-Key ZORUNLU kılıyor. Başlığı burada geçirmek yerine
    # her çağrı yerinde elle kurmak, bir yerde unutulduğunda testi "400 aldı" diye
    # yanıltıcı biçimde kırardı.
    if ($key) { $h['Idempotency-Key'] = $key }
    $json = $body | ConvertTo-Json -Depth 8
    Invoke-RestMethod -Uri "$Api$path" -Method $method -Headers $h `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}

# Her düzeltme kendi anahtarını alır; tekilliği bilerek zorlayan tek yer H bölümüdür.
function NewKey { [Guid]::NewGuid().ToString() }
function Get_($path, $token) { Invoke-RestMethod -Uri "$Api$path" -Headers @{ Authorization = "Bearer $token" } }
function HataKodu($e) { [int]$e.Exception.Response.StatusCode }
function HataGovde($e) {
    $r = New-Object IO.StreamReader($e.Exception.Response.GetResponseStream())
    try { ($r.ReadToEnd() | ConvertFrom-Json).title } catch { '(govde okunamadi)' }
}

function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }
function NewUser($prefix, $stamp) {
    $hwid = NewHwid; $email = "$prefix$stamp@test.dev"
    $r = Send Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix $stamp"; termsVersion = '2026-08-27'; ageConfirmed = $true; hwidHash = $hwid } $null
    Send Post '/api/auth/verify-email' @{ email = $email; code = $r.verificationToken } $null | Out-Null
    $l = Send Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    [pscustomobject]@{ Email = $email; Token = $l.accessToken; UserId = $l.userId; Hwid = $hwid }
}
# Rol DB'den atanır; token'a yansıması için YENİDEN GİRİŞ şart.
function RolVer($u, $rol) {
    Sql "UPDATE identity.""Users"" SET ""Role"" = '$rol' WHERE ""Id"" = '$($u.UserId)';" | Out-Null
    $l = Send Post '/api/auth/login' @{ email = $u.Email; password = 'Demo12345'; hwidHash = $u.Hwid } $null
    $u.Token = $l.accessToken
    $u
}

function Bakiye($userId) { SqlInt "SELECT COALESCE(""AvailableBalance"",0) FROM economy.""Wallets"" WHERE ""UserId"" = '$userId';" }
function LotToplami($userId) {
    SqlInt "SELECT COALESCE(SUM(l.""RemainingAmount""),0) FROM economy.""CreditLots"" l JOIN economy.""Wallets"" w ON w.""Id"" = l.""WalletId"" WHERE w.""UserId"" = '$userId';"
}
function Sayac($userId) { SqlInt "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$userId';" }
function DefterFarki { SqlInt 'SELECT (SELECT COALESCE(SUM("AvailableBalance"),0) FROM economy."Wallets") - (SELECT COALESCE(SUM("Amount"),0) FROM economy."CreditTransactions");' }

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
Write-Host "PeerLearn — yönetim puan düzeltmesi (F3)" -ForegroundColor White
Write-Host "koşum: $stamp"

Section 'Hazırlık'
$admin = RolVer (NewUser 'adma' $stamp) 'Admin'
$moder = RolVer (NewUser 'admm' $stamp) 'Moderator'
$hedef = NewUser 'admh' $stamp
$baslangic = Bakiye $hedef.UserId
OK "admin, moderatör ve hedef kullanıcı hazır (hedef bakiye: $baslangic)"

$defterOnce = DefterFarki
if ($defterOnce -eq 0) { OK 'başlangıçta defter dengeli' } else { Fail "defter zaten bozuk: $defterOnce" }

Section 'A. Yetki'

try { Send Post "/api/admin/users/$($hedef.UserId)/credits" @{ amount = 50; reason = 'yetkisiz deneme' } $hedef.Token (NewKey) | Out-Null
      Fail 'normal kullanıcı düzeltme yapabildi' }
catch { if ((HataKodu $_) -in 401,403) { OK "normal kullanıcı reddedildi ($(HataKodu $_))" } else { Fail "beklenmeyen kod: $(HataKodu $_)" } }

try { Send Post "/api/admin/users/$($hedef.UserId)/credits" @{ amount = 50; reason = 'moderatör denemesi' } $moder.Token (NewKey) | Out-Null
      Fail 'moderatör düzeltme yapabildi (AdminOnly olmalı)' }
catch { if ((HataKodu $_) -eq 403) { OK 'moderatör reddedildi (403) — uç AdminOnly' } else { Fail "beklenmeyen kod: $(HataKodu $_)" } }

Section 'B. Doğrulama'

foreach ($k in @(
    @{ b = @{ amount = 50; reason = 'kisa' };                    ad = 'kısa gerekçe' },
    @{ b = @{ amount = 50; reason = '   ' };                     ad = 'boşluk gerekçe' },
    @{ b = @{ amount = 0;  reason = 'sifir tutar denemesi' };    ad = 'sıfır tutar' },
    @{ b = @{ amount = 10001; reason = 'ust sinir denemesi' };   ad = 'üst sınır aşımı' },
    @{ b = @{ amount = -10001; reason = 'alt sinir denemesi' };  ad = 'negatif üst sınır aşımı' }
)) {
    try { Send Post "/api/admin/users/$($hedef.UserId)/credits" $k.b $admin.Token (NewKey) | Out-Null; Fail "$($k.ad) kabul edildi" }
    catch { if ((HataKodu $_) -eq 400) { OK "$($k.ad) reddedildi (400)" } else { Fail "$($k.ad): $(HataKodu $_)" } }
}

$degismedi = Bakiye $hedef.UserId
if ($degismedi -eq $baslangic) { OK 'reddedilen isteklerin hiçbiri bakiyeye dokunmadı' }
else { Fail "bakiye değişti: $baslangic -> $degismedi" }

Section 'C. Ekleme'

$sayacOnce = Sayac $hedef.UserId
$r = Send Post "/api/admin/users/$($hedef.UserId)/credits" @{ amount = 250; reason = 'kayip basimin telafisi' } $admin.Token (NewKey)
if ($r.newAvailableBalance -eq ($baslangic + 250)) { OK "ekleme yanıtı doğru bakiyeyi döndü ($($r.newAvailableBalance))" }
else { Fail "yanıt bakiyesi: $($r.newAvailableBalance)" }
if ((Bakiye $hedef.UserId) -eq ($baslangic + 250)) { OK 'veritabanı bakiyesi arttı' } else { Fail 'DB bakiyesi tutmuyor' }

$lot = Sql "SELECT ""Source"" || '|' || ""InitialAmount"" || '|' || COALESCE(""ExpiresAtUtc""::text,'SURESIZ') FROM economy.""CreditLots"" l JOIN economy.""Wallets"" w ON w.""Id"" = l.""WalletId"" WHERE w.""UserId"" = '$($hedef.UserId)' AND l.""Source"" = 'AdminGrant';"
if ($lot.Trim() -eq 'AdminGrant|250|SURESIZ') { OK 'AdminGrant lotu VADESİZ açıldı' } else { Fail "lot: $($lot.Trim())" }

$hareket = SqlInt "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = '$($hedef.UserId)' AND t.""Type"" = 'AdminAdjustment' AND t.""Amount"" = 250;"
if ($hareket -eq 1) { OK 'AdminAdjustment hareketi yazıldı (+250)' } else { Fail "hareket sayısı: $hareket" }

# ASIL SINAV: unvan sayacı yönetim eliyle ARTMAMALI.
if ((Sayac $hedef.UserId) -eq $sayacOnce) { OK "unvan sayacı DEĞİŞMEDİ ($sayacOnce) — yönetim unvan dağıtamaz" }
else { Fail "sayaç değişti: $sayacOnce -> $(Sayac $hedef.UserId)" }

Section 'D. Düşme'

$oncekiBakiye = Bakiye $hedef.UserId
Send Post "/api/admin/users/$($hedef.UserId)/credits" @{ amount = -100; reason = 'yanlis hesaba basilmis puan' } $admin.Token (NewKey) | Out-Null
if ((Bakiye $hedef.UserId) -eq ($oncekiBakiye - 100)) { OK 'düşme bakiyeyi azalttı' } else { Fail "bakiye: $(Bakiye $hedef.UserId)" }

$tuketim = SqlInt "SELECT COALESCE(SUM(k.""Amount""),0) FROM economy.""CreditLotConsumptions"" k JOIN economy.""CreditTransactions"" t ON t.""Id"" = k.""CreditTransactionId"" JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = '$($hedef.UserId)' AND t.""Type"" = 'AdminAdjustment';"
if ($tuketim -eq 100) { OK 'düşme lotlardan tüketildi ve izi yazıldı (100)' } else { Fail "tüketim toplamı: $tuketim" }

# Bakiyeyi aşan düşme reddedilmeli — negatif bakiye ASLA oluşamaz.
$simdiki = Bakiye $hedef.UserId
try { Send Post "/api/admin/users/$($hedef.UserId)/credits" @{ amount = -($simdiki + 1); reason = 'bakiyeyi asan dusme' } $admin.Token (NewKey) | Out-Null
      Fail 'bakiyeyi aşan düşme kabul edildi' }
catch {
    if ((HataKodu $_) -eq 409 -and (HataGovde $_) -eq 'ADJUSTMENT_EXCEEDS_BALANCE') { OK 'bakiyeyi aşan düşme reddedildi (409 ADJUSTMENT_EXCEEDS_BALANCE)' }
    else { Fail "kod: $(HataKodu $_) / $(HataGovde $_)" }
}
if ((Bakiye $hedef.UserId) -eq $simdiki) { OK 'reddedilen düşme bakiyeyi değiştirmedi' } else { Fail 'bakiye reddedilen istekte değişti' }

# Tam bakiye kadar düşme SINIRDA geçmeli (kapsayıcı sınır).
Send Post "/api/admin/users/$($hedef.UserId)/credits" @{ amount = -$simdiki; reason = 'bakiyenin tamaminin sifirlanmasi' } $admin.Token (NewKey) | Out-Null
if ((Bakiye $hedef.UserId) -eq 0) { OK 'tam bakiye kadar düşme geçti, bakiye 0' } else { Fail "bakiye: $(Bakiye $hedef.UserId)" }

Section 'E. Değişmezler'

if ((Bakiye $hedef.UserId) -eq (LotToplami $hedef.UserId)) { OK 'cüzdan bakiyesi = lot toplamı' }
else { Fail "bakiye $(Bakiye $hedef.UserId) <> lot $(LotToplami $hedef.UserId)" }

$defterSonra = DefterFarki
if ($defterSonra -eq 0) { OK 'defter dengeli: cüzdan toplamı = hareket toplamı' } else { Fail "defter farkı: $defterSonra" }

$negatif = SqlInt 'SELECT COUNT(*) FROM economy."Wallets" WHERE "AvailableBalance" < 0;'
if ($negatif -eq 0) { OK 'hiçbir cüzdanda negatif bakiye yok' } else { Fail "negatif cüzdan: $negatif" }

$bozukLot = SqlInt 'SELECT COUNT(*) FROM economy."CreditLots" WHERE "RemainingAmount" < 0 OR "RemainingAmount" > "InitialAmount";'
if ($bozukLot -eq 0) { OK 'hiçbir lotta negatif/aşırı kalan yok' } else { Fail "bozuk lot: $bozukLot" }

# Sayac ile BASIM toplami esitligi bozulmamali (duzeltmeler sayaca hic dokunmadigi icin).
#
# ⚠️ DEGISMEZ GENISLEDI (2026-08-29), ZAYIFLAMADI. Eskiden yalnizca LessonEarning
# sayiliyordu; topluluk katkisi da unvan sayacina girdigi icin (CommunityReward) toplama
# eklendi. AdminAdjustment BILEREK DISARIDA: yonetim eliyle unvan dagitilamaz, testin
# asil sinadigi sey de bu.
#
# Yeni bir basim turu eklenirse buraya da eklenmeli — aksi halde bu denetim gercek bir
# sapmayi degil, kendi eksik sorgusunu bulur.
$sayacSapma = SqlInt @'
SELECT COUNT(*) FROM (
  SELECT u."Id" FROM identity."Users" u
  LEFT JOIN economy."Wallets" w ON w."UserId" = u."Id"
  LEFT JOIN economy."CreditTransactions" t ON t."WalletId" = w."Id"
       AND t."Type" IN ('LessonEarning', 'CommunityReward')
  GROUP BY u."Id", u."TotalEarnedCredits"
  HAVING u."TotalEarnedCredits" <> COALESCE(SUM(t."Amount"),0)) z;
'@
if ($sayacSapma -eq 0) { OK 'TotalEarnedCredits = SUM(LessonEarning + CommunityReward) — duzeltmeler esitligi bozmadi' }
else { Fail "sapan kullanici: $sayacSapma" }

Section 'F. Denetim izi'

$log = SqlInt "SELECT COUNT(*) FROM moderation.""AdminActionLogs"" WHERE ""Action"" = 'CreditAdjusted' AND ""TargetId"" = '$($hedef.UserId)' AND ""ActorUserId"" = '$($admin.UserId)';"
if ($log -eq 3) { OK "üç düzeltmenin üçü de denetim izine düştü ($log)" } else { Fail "log satırı: $log (beklenen 3)" }

$gerekce = Sql "SELECT ""Summary"" FROM moderation.""AdminActionLogs"" WHERE ""Action"" = 'CreditAdjusted' AND ""TargetId"" = '$($hedef.UserId)' ORDER BY ""CreatedAtUtc"" LIMIT 1;"
if ($gerekce -match 'kayip basimin telafisi' -and $gerekce -match '\+250') { OK 'denetim izi tutarı ve gerekçeyi birlikte taşıyor' }
else { Fail "özet: $($gerekce.Trim())" }

$rol = Sql "SELECT ""ActorRole"" FROM moderation.""AdminActionLogs"" WHERE ""Action"" = 'CreditAdjusted' AND ""TargetId"" = '$($hedef.UserId)' LIMIT 1;"
if ($rol.Trim() -eq 'Admin') { OK 'karar anındaki rol kaydedildi (Admin)' } else { Fail "rol: $($rol.Trim())" }

# Reddedilen istekler iz BIRAKMAMALI: denenmiş ama olmamış bir işlem kayda geçerse
# denetim izi gerçekleşen işlemlerin listesi olmaktan çıkar.
$modLog = SqlInt "SELECT COUNT(*) FROM moderation.""AdminActionLogs"" WHERE ""Action"" = 'CreditAdjusted' AND ""ActorUserId"" = '$($moder.UserId)';"
if ($modLog -eq 0) { OK 'reddedilen moderatör denemesi iz bırakmadı' } else { Fail "moderatör izi: $modLog" }

Section 'G. Var olmayan kullanıcı'

$yok = [Guid]::NewGuid()
try { Send Post "/api/admin/users/$yok/credits" @{ amount = 10; reason = 'olmayan kullanici denemesi' } $admin.Token (NewKey) | Out-Null; Fail 'olmayan kullanıcıya düzeltme geçti' }
catch { if ((HataKodu $_) -eq 404 -and (HataGovde $_) -eq 'USER_NOT_FOUND') { OK 'olmayan kullanıcı 404 USER_NOT_FOUND' } else { Fail "kod: $(HataKodu $_) / $(HataGovde $_)" } }

Section 'H. Tekillik (Idempotency-Key)'

<#
  NEDEN AYRI HEDEF KULLANICI: yukarıdaki bölümler $hedef üzerinde tam sayılar bekliyor
  (üç düzeltme, 250'lik lot, sıfır bakiye). Tekillik denemeleri oraya karışsaydı hem bu
  bölümün hem onların ölçümü bulanırdı.

  ÖLÇÜLEN ŞEY "ikinci istek hata verdi mi" DEĞİL: "defter ikinci kez yazıldı mı".
  Bir uç, tekrar isteğine 409 dönerek de "idempotent" görünebilir; ama ağ hatası sonrası
  tekrar denemede kullanıcıya hata göstermek tam olarak kaçındığımız durumu üretir —
  yönetici üçüncü kez dener. Doğru davranış: sessizce ilk sonucu döndürmek.
#>
$hedef2 = NewUser 'admi' $stamp
$anahtar = NewKey
$govde = @{ amount = 300; reason = 'tekillik denemesi icin duzeltme' }

$ilk = Send Post "/api/admin/users/$($hedef2.UserId)/credits" $govde $admin.Token $anahtar
if ($ilk.replayed -eq $false) { OK 'ilk istek yeni düzeltme olarak uygulandı (replayed=false)' }
else { Fail "ilk isteğin replayed alanı: $($ilk.replayed)" }
$bakiyeIlk = Bakiye $hedef2.UserId

$ikinci = Send Post "/api/admin/users/$($hedef2.UserId)/credits" $govde $admin.Token $anahtar
if ($ikinci.replayed -eq $true) { OK 'aynı anahtarla ikinci istek TEKRAR OYNATILDI (replayed=true)' }
else { Fail "ikinci isteğin replayed alanı: $($ikinci.replayed) — düzeltme ikinci kez uygulanmış olabilir" }
if ($ikinci.newAvailableBalance -eq $ilk.newAvailableBalance) { OK 'tekrar oynatma ilk sonucun bakiyesini döndü' }
else { Fail "bakiye: $($ilk.newAvailableBalance) -> $($ikinci.newAvailableBalance)" }

# ASIL KANIT veritabanında: yanıt ne derse desin, defter iki kez yazılmamalı.
if ((Bakiye $hedef2.UserId) -eq $bakiyeIlk) { OK "ikinci istek bakiyeye DOKUNMADI ($bakiyeIlk)" }
else { Fail "bakiye ikinci istekte değişti: $bakiyeIlk -> $(Bakiye $hedef2.UserId)" }

$h2 = SqlInt "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = '$($hedef2.UserId)' AND t.""Type"" = 'AdminAdjustment';"
if ($h2 -eq 1) { OK 'defterde TEK AdminAdjustment hareketi var' } else { Fail "hareket sayısı: $h2 (beklenen 1)" }

$l2 = SqlInt "SELECT COUNT(*) FROM moderation.""AdminActionLogs"" WHERE ""Action"" = 'CreditAdjusted' AND ""TargetId"" = '$($hedef2.UserId)';"
if ($l2 -eq 1) { OK 'denetim izinde TEK satır var (tekrar iz bırakmadı)' } else { Fail "log satırı: $l2 (beklenen 1)" }

# Aynı anahtar FARKLI yükle: sessizce ilk sonucu döndürmek, yöneticinin düzeltmeye
# çalıştığı şeyi gizlerdi. 409 ile durmalı ve hiçbir şey yazmamalı.
try {
    Send Post "/api/admin/users/$($hedef2.UserId)/credits" @{ amount = 500; reason = 'tekillik denemesi icin duzeltme' } $admin.Token $anahtar | Out-Null
    Fail 'aynı anahtar farklı TUTARLA kabul edildi'
} catch {
    if ((HataKodu $_) -eq 409 -and (HataGovde $_) -eq 'IDEMPOTENCY_KEY_REUSED') { OK 'aynı anahtar farklı tutarla reddedildi (409 IDEMPOTENCY_KEY_REUSED)' }
    else { Fail "kod: $(HataKodu $_) / $(HataGovde $_)" }
}

try {
    Send Post "/api/admin/users/$($hedef2.UserId)/credits" @{ amount = 300; reason = 'gerekce degistirildi bu satir' } $admin.Token $anahtar | Out-Null
    Fail 'aynı anahtar farklı GEREKÇEYLE kabul edildi'
} catch {
    if ((HataKodu $_) -eq 409 -and (HataGovde $_) -eq 'IDEMPOTENCY_KEY_REUSED') { OK 'aynı anahtar farklı gerekçeyle reddedildi (409)' }
    else { Fail "kod: $(HataKodu $_) / $(HataGovde $_)" }
}

if ((Bakiye $hedef2.UserId) -eq $bakiyeIlk) { OK 'reddedilen çakışmalar bakiyeye dokunmadı' }
else { Fail "bakiye: $(Bakiye $hedef2.UserId)" }

# Anahtarsız istek: koruma isteğe bağlı olsaydı hiçbir şey korumazdı.
try {
    Send Post "/api/admin/users/$($hedef2.UserId)/credits" @{ amount = 10; reason = 'anahtarsiz istek denemesi' } $admin.Token $null | Out-Null
    Fail 'Idempotency-Key olmadan düzeltme kabul edildi'
} catch { if ((HataKodu $_) -eq 400) { OK 'anahtarsız istek reddedildi (400)' } else { Fail "kod: $(HataKodu $_)" } }

<#
  ANAHTAR YÖNETİCİYE GÖRE TEKİL. Index (ActorUserId, IdempotencyKey) üzerinde; global
  olsaydı iki yöneticinin aynı anahtarı kullanması birinin işlemini diğerinin tekrarı
  sayardı — yani B'nin düzeltmesi sessizce yutulurdu.
#>
$admin2 = RolVer (NewUser 'admb' $stamp) 'Admin'
$ikinciYonetici = Send Post "/api/admin/users/$($hedef2.UserId)/credits" $govde $admin2.Token $anahtar
if ($ikinciYonetici.replayed -eq $false) { OK 'aynı anahtar FARKLI yöneticide yeni düzeltme sayıldı' }
else { Fail 'ikinci yöneticinin isteği tekrar oynatıldı — anahtar aktöre göre tekil değil' }
if ((Bakiye $hedef2.UserId) -eq ($bakiyeIlk + 300)) { OK 'ikinci yöneticinin düzeltmesi gerçekten uygulandı' }
else { Fail "bakiye: $(Bakiye $hedef2.UserId) (beklenen $($bakiyeIlk + 300))" }

<#
  EŞZAMANLI AYNI ANAHTAR — kısmi unique index'in son savunma olarak sınandığı yer.

  Handler "anahtar var mı" diye bakıp sonra yazıyor; iki istek aynı anda bakarsa ikisi de
  boş görebilir. Cüzdan kilidi çoğu durumda serileştirir ama Redis erişilemezse etmez.
  Index olmasaydı tekillik "genelde çalışan" bir şey olurdu; burada tam olarak o ölçülüyor.
#>
Add-Type -AssemblyName System.Net.Http
[System.Net.ServicePointManager]::DefaultConnectionLimit = 100
$hedef3 = NewUser 'admj' $stamp
# Yeni kullanıcı hoş geldin kredisiyle (1 puan) açılıyor: mutlak bakiye beklemek testi
# ayarın değerine bağlar. Ölçülmesi gereken FARK.
$bakiye3Once = Bakiye $hedef3.UserId
$anahtar3 = NewKey
$client = New-Object System.Net.Http.HttpClient
$mesajlar = @()
for ($i = 0; $i -lt 4; $i++) {
    $m = New-Object System.Net.Http.HttpRequestMessage(
        (New-Object System.Net.Http.HttpMethod('POST')), "$Api/api/admin/users/$($hedef3.UserId)/credits")
    $m.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $admin.Token)
    $m.Headers.Add('Idempotency-Key', $anahtar3)
    $m.Content = New-Object System.Net.Http.StringContent(
        (@{ amount = 120; reason = 'esszamanli tekillik denemesi' } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
    $mesajlar += $m
}
$gorevler = New-Object 'System.Collections.Generic.List[System.Threading.Tasks.Task[System.Net.Http.HttpResponseMessage]]'
foreach ($m in $mesajlar) { $gorevler.Add($client.SendAsync($m)) }
try { [System.Threading.Tasks.Task]::WaitAll($gorevler.ToArray()) } catch { }
$basarili = 0; $sunucuHatasi = 0
foreach ($g in $gorevler) {
    if ($g.IsFaulted) { $sunucuHatasi++; continue }
    if ($g.Result.IsSuccessStatusCode) { $basarili++ }
    elseif ([int]$g.Result.StatusCode -ge 500) { $sunucuHatasi++ }
}
$client.Dispose()

if ($basarili -eq 4) { OK 'eşzamanlı 4 isteğin dördü de temiz yanıt aldı (hiçbiri hata görmedi)' }
else { Fail "başarılı yanıt: $basarili (beklenen 4)" }
if ($sunucuHatasi -eq 0) { OK 'eşzamanlı yarışta 5xx/taşıma hatası yok' } else { Fail "$sunucuHatasi istek 5xx ile düştü" }

$h3 = SqlInt "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = '$($hedef3.UserId)' AND t.""Type"" = 'AdminAdjustment';"
if ($h3 -eq 1) { OK 'eşzamanlı 4 istekten defterde TEK hareket kaldı' } else { Fail "hareket sayısı: $h3 (beklenen 1)" }
if ((Bakiye $hedef3.UserId) -eq ($bakiye3Once + 120)) { OK 'bakiye TEK düzeltme kadar arttı (+120)' }
else { Fail "bakiye: $(Bakiye $hedef3.UserId) (beklenen $($bakiye3Once + 120))" }

$defterSon = DefterFarki
if ($defterSon -eq 0) { OK 'tekillik denemelerinden sonra defter hâlâ dengeli' } else { Fail "defter farkı: $defterSon" }

Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green }
else { Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red; exit 1 }
