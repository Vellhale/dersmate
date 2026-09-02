# PeerLearn — Sosyal profil, değerlendirme ve gönüllü ders testi
#
# Kapsam:
#   • Değerlendirme kuralları (yalnızca tamamlanmış ders, yalnızca öğrenci, tek kez)
#   • Puan ortalamasının yeniden hesaplanması, etiket dağılımı
#   • Rozet motoru: yalnızca 🌱 öğretmen adayı + BAŞARI ROZETLERİNİN EMEKLİLİĞİ
#   • GÖNÜLLÜ DERS (IsVolunteer bayrağı): eğitmene HİÇ puan basılmaz
#   • ÜCRETLİ DERS karşıtlığı: onayda eğitmene puan BASILIR (60 dk -> 100) ve
#     TotalEarnedCredits aynı miktarda artar; öğrencinin bakiyesi HİÇ oynamaz
#   • Profil kartı alanları ve SEVİYE eşikleri (level / levelMinCredits / nextLevelAt)
#
# BASIM MODELİNE GEÇİŞ — bu dosyada değişen varsayımlar:
#   • Öğrenci ders için ödeme YAPMIYOR. "Kredi bloke edildi mi / transfer edildi mi"
#     kontrolleri, "bakiye HİÇ oynamadı + hold satırı hiç yazılmadı + eğitmene doğru
#     miktar BASILDI" kontrollerine dönüştürüldü.
#   • Gönüllülük artık CreditCost=0 çıkarımından değil, LessonSessions.IsVolunteer
#     bayrağından okunuyor; tüm gönüllü kontrolleri bayrağa bakıyor.
#   • Profil DTO'su artık `badges` döndürmüyor. Rozet motorunun hâlâ doğru çalıştığı
#     yalnızca community."UserBadges" satırlarından gözlenebiliyor; profilin GÖSTERİM
#     tarafı ise unvan alanlarıyla (G bölümü) sınanıyor.
#
# KALDIRILAN KAPSAM (karşılığı olmadığı için):
#   • Rozet vitrini (PUT /api/profile/featured-badges) — UÇ TAMAMEN KALDIRILDI. Vitrin,
#     "çok rozet arasından üçünü seç" problemini çözüyordu; başarı rozetleri emekli
#     edilince seçilecek tek rozet kaldı ve hem problem hem çözüm ortadan kalktı.
#   • ⚡ Hızlı yanıt (FastResponder) senaryoları — kural motordan silindi. Onun yerine
#     H bölümünde EMEKLİLİĞİN KENDİSİ sınanıyor: emekli rozetler bir daha verilmemeli.
#   • "Yetersiz kredi" ve escrow senaryoları — bu dosyada zaten yoktu, eklenmedi:
#     rezervasyon artık bakiyeye hiç bakmıyor, INSUFFICIENT_CREDITS üreten yol yok.

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

<#
  psql'e üç yoldan erişilebilir; ilk bulunan kullanılır:
    1. Windows kurulumu   — geliştiricinin makinesinde tipik yol.
    2. PATH üzerinde psql — Linux/macOS ve CI koşucuları (postgresql-client).
    3. docker compose     — makinede psql yok ama compose yığını ayakta.

  Üçüncü yol OLMADAN bu paket, docs/GELISTIRME-ORTAMI.md'nin tarif ettiği Docker
  kurulumunda hiç koşamıyordu: yalnızca yerel PostgreSQL 17 kurulumu varsayılıyor ve
  betik "psql bulunamadi" ile ortasında düşüyordu. Testin koşamaması, testin
  başarısız olmasından daha sinsi — özet onu KIRMIZI değil, hiç görünmemiş sayıyor.
#>
if (-not (Test-Path $Psql)) {
    # PS 5.1 UYUMU: `?.` null-koşullu operatörü PowerShell 7 ile geldi. Paketler
    # CLAUDE.md gereği 5.1 altında koşuyor ve orada bu bir SÖZDİZİMİ hatası —
    # betik hiç başlamaz, yani "psql yok" durumunu ele alacak kod hiç çalışmaz.
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $Psql = $bulunan } else { $Psql = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'

$script:Pass = 0; $script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Sql($q) {
    if (-not $Psql) {
        # Docker yolu: sorgu STDIN'den geçer. `-c` ile argüman olarak geçirmek,
        # identity."Users" gibi tırnaklı adlardaki tırnakları kabuğa yedirir.
        $out = $q | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return $out
    }
    $f = Join-Path $env:TEMP "pl-soc-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $f } finally { Remove-Item $f -Force }
}
function Send($method, $path, $body, $token) {
    $h = @{}; if ($token) { $h['Authorization'] = "Bearer $token" }
    $json = $body | ConvertTo-Json -Depth 8
    Invoke-RestMethod -Uri "$Api$path" -Method $method -Headers $h -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}
function Get_($path, $token) { Invoke-RestMethod -Uri "$Api$path" -Headers @{ Authorization = "Bearer $token" } }
function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }
function NewUser($prefix, $stamp) {
    $hwid = NewHwid; $email = "$prefix$stamp@test.dev"
    $r = Send Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix $stamp"; termsVersion = '2026-08-27'; ageConfirmed = $true; hwidHash = $hwid } $null
    Send Post '/api/auth/verify-email' @{ email = $email; code = $r.verificationToken } $null | Out-Null
    $l = Send Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    # Hwid saklanır: rol değişikliği token'a ancak YENİDEN GİRİŞLE yansır, giriş ise
    # aynı cihaz kimliğini ister.
    [pscustomobject]@{ Email = $email; Token = $l.accessToken; UserId = $l.userId; Hwid = $hwid }
}
function HataKodu($e) { [int]$e.Exception.Response.StatusCode }

# ROZET GÖZLEMİ ARTIK VERİTABANINDAN. Profil DTO'su rozet listesi döndürmediği için
# motorun çıktısını görmenin başka yolu kalmadı.
#
# KATALOGLA BİRLEŞTİRİLİYOR: UserBadges satırında yalnızca BadgeId (bir UUID) var,
# rozetin KODU katalog tablosunda (Badges.Code) duruyor. Satırın ham metnini taramak
# bu yüzden hiçbir zaman eşleşmez — UUID'de "FirstLesson" geçmez. İlk yazımda tam olarak
# bu yapıldığı için motorun çalıştığı hâlde tüm rozet kontrolleri kırmızı veriyordu.
function RozetKodlari($userId) {
    Sql "SELECT c.""Code"" FROM community.""UserBadges"" ub JOIN community.""Badges"" c ON c.""Id"" = ub.""BadgeId"" WHERE ub.""UserId"" = '$userId';"
}
# @() şart: tek satır dönerse Where-Object sonucunun .Count'u boş gelir (PS 5.1).
function RozetVarMi($userId, $kod) {
    @(RozetKodlari $userId | Where-Object { $_.Trim() -eq $kod }).Count -gt 0
}
function RozetDokumu($userId) {
    $k = @(RozetKodlari $userId | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($k.Count -eq 0) { '(hiç rozet yok)' } else { $k -join ', ' }
}

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
Write-Host "PeerLearn — sosyal profil / değerlendirme / gönüllü ders" -ForegroundColor White
Write-Host "koşum: $stamp"

Section 'Hazırlık'
Sql @"
INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT gen_random_uuid(), s."Id", 'Soc Konu $stamp', 950, TRUE, now()
FROM catalog."Subjects" s ORDER BY s."Name" LIMIT 1;
"@ | Out-Null
$topicId = (Sql "SELECT ""Id"" FROM catalog.""Topics"" WHERE ""Name"" = 'Soc Konu $stamp';").Trim()

$tutor = NewUser 'soct' $stamp
$student = NewUser 'socs' $stamp
OK 'kullanıcılar ve konu hazır'

# Öğretmen adayı beyanı -> gönüllü ilan açabilmesi ve 🌱 rozeti için.
Sql @"
INSERT INTO identity."TeacherCandidateProfiles"
  ("Id","UserId","University","Faculty","Department","GradeYear","HasPedagogicalCertificate","DeclaredAtUtc","CreatedAtUtc")
VALUES (gen_random_uuid(), '$($tutor.UserId)', 'Test Üniversitesi', 'Eğitim Fakültesi', 'Matematik Öğretmenliği', 3, FALSE, now(), now());
"@ | Out-Null
OK 'öğretmen adayı beyanı kaydedildi'

# GÖNÜLLÜ ilan.
Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5 } $tutor.Token | Out-Null
Sql "UPDATE matchmaking.""PortfolioEntries"" SET ""IsVolunteer"" = TRUE WHERE ""UserId"" = '$($tutor.UserId)' AND ""TopicId"" = '$topicId';" | Out-Null
Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek'; selfAssessedLevel = 1 } $student.Token | Out-Null
OK 'gönüllü ders ilanı açıldı'

Section 'A. Gönüllü ders: bakiyeye dokunmadan rezervasyon'

# Rezervasyon artık bir ÖDEME DEĞİL. Bu yüzden ölçülen şey "bakiye ne kadar düştü" değil,
# "bakiye HİÇ oynamadı mı" (değişmez 1) ve "hold hiç yazılmadı mı" (değişmez 2).
$ogrenciOnce = Get_ '/api/wallet' $student.Token
# Cüzdan tablosunun kullanıcı kolonu adına bağımlı kalmamak için KÜRESEL toplamlar
# alınıyor; koşum sırasında başka bir yazar olmadığı için toplamın sabit kalması,
# tek cüzdanın sabit kaldığından daha güçlü bir iddia.
$kilitOnce = (Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='Wallets' AND column_name='LockedBalance';").Trim()
$serbestOnce = (Sql 'SELECT COALESCE(SUM("AvailableBalance"),0) FROM economy."Wallets";').Trim()

$matchId = Send Post '/api/matches' @{ responderUserId = $tutor.UserId; requestedTopicId = $topicId } $student.Token
Send Post "/api/matches/$matchId/respond" @{ accept = $true } $tutor.Token | Out-Null

$start = [DateTime]::UtcNow.AddHours(2).ToString('o')

# Süre kümesi artık YALNIZCA 30 ve 60. Geçersiz süre ÖNCE deneniyor: hemen ardından aynı
# eşleşmeyle 60 dk'nın sorunsuz açılması, retin "süre" yüzünden olduğunu kanıtlıyor
# (yoksa eşleşme zaten rezerve edilemez durumda olsaydı da test yeşil geçerdi).
try {
    Send Post '/api/sessions' @{ matchId = $matchId; topicId = $topicId; scheduledStartUtc = $start; durationMinutes = 45 } $student.Token | Out-Null
    Fail '45 dk ders açılabildi (süre kümesi 30/60 olmalı)'
} catch { OK "45 dk reddedildi — INVALID_BOOKING ($(HataKodu $_))" }

# MintGuard: aynı eğitmen-öğrenci çifti 24 saatte en fazla 2 ders kurabiliyor (429).
# Bu çift bu koşumda TEK ders kuruyor; ücretli karşılaştırma dersi (B2) bilerek AYRI bir
# çiftle açılıyor.
$session = Send Post '/api/sessions' @{ matchId = $matchId; topicId = $topicId; scheduledStartUtc = $start; durationMinutes = 60 } $student.Token

# Gönüllülük artık CreditCost=0 çıkarımıyla DEĞİL, açık bayrakla taşınıyor.
if ($session.isVolunteer) { OK 'ders gönüllü olarak işaretlendi (isVolunteer)' }
else { Fail "isVolunteer: $($session.isVolunteer)" }
if ($session.mintAmount -eq 0) { OK 'gönüllü derste basılacak puan 0' }
else { Fail "basılacak puan: $($session.mintAmount)" }

$bayrak = (Sql "SELECT ""IsVolunteer"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$($session.sessionId)';").Trim()
if ($bayrak -eq 't') { OK 'bayrak veritabanına da yazıldı (IsVolunteer)' } else { Fail "IsVolunteer sütunu: $bayrak" }

$ogrenciSonra = Get_ '/api/wallet' $student.Token
if ($ogrenciSonra.currentBalance -eq $ogrenciOnce.currentBalance) {
    OK "rezervasyon öğrencinin bakiyesine dokunmadı ($($ogrenciSonra.currentBalance))"
} else { Fail "bakiye değişti: $($ogrenciOnce.currentBalance) -> $($ogrenciSonra.currentBalance)" }

$kilitSonra = (Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='Wallets' AND column_name='LockedBalance';").Trim()
$serbestSonra = (Sql 'SELECT COALESCE(SUM("AvailableBalance"),0) FROM economy."Wallets";').Trim()
if ($kilitSonra -eq $kilitOnce -and $serbestSonra -eq $serbestOnce) {
    OK "rezervasyonda hiçbir bakiye düşmedi/kilitlenmedi (kilit toplamı $kilitSonra)"
} else { Fail "cüzdan toplamları değişti: $serbestOnce/$kilitOnce -> $serbestSonra/$kilitSonra" }

# Kaldırılan alanların YOKLUĞU da sınanıyor: eski istemci $null okuyup "bakiyen 0" demesin.
$cuzdanAlanlari = $ogrenciSonra.PSObject.Properties.Name
$kalanEski = @(@('availableBalance', 'lockedBalance', 'pendingExpirySweep') | Where-Object { $cuzdanAlanlari -contains $_ })
if ($kalanEski.Count -eq 0) { OK 'escrow dönemi cüzdan alanları yanıttan kalkmış' }
else { Fail "kaldırılması gereken alan duruyor: $($kalanEski -join ',')" }
$eksikYeni = @(@('totalEarnedCredits', 'currentBalance', 'level', 'levelMinCredits', 'nextLevelAt', 'activeLots') | Where-Object { $cuzdanAlanlari -notcontains $_ })
if ($eksikYeni.Count -eq 0) { OK 'cüzdan yeni alanları döndürüyor (kazanç + seviye + lotlar)' }
else { Fail "eksik cüzdan alanı: $($eksikYeni -join ',')" }

$holdSayisi = (Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';").Trim()
if ($holdSayisi -eq '0') { OK 'escrow/hold kaydı hiç oluşmadı' } else { Fail "hold sayısı: $holdSayisi" }

Section 'B. Tamamlama ve onay: gönüllü derste BASIM YOK'

$egitmenKazancOnce = [int](Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($tutor.UserId)';").Trim()

Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '3 hours', ""ScheduledEndUtc"" = now() - interval '2 hours' WHERE ""Id"" = '$($session.sessionId)';" | Out-Null

# Kanıt yükleme multipart; testte doğrudan AwaitingApproval'a alınıyor (kanıt yolu e2e-smoke'ta kapsanıyor).
Sql "UPDATE scheduling.""LessonSessions"" SET ""Status"" = 'AwaitingApproval', ""CompletionRequestedAtUtc"" = now() WHERE ""Id"" = '$($session.sessionId)';" | Out-Null

$onay = Send Post "/api/sessions/$($session.sessionId)/approve" @{} $student.Token
if ($onay.creditsMinted -eq 0) { OK 'onayda hiç puan basılmadı (gönüllü)' } else { Fail "basılan: $($onay.creditsMinted)" }

# Basımın tek kanıtı yanıt alanı değil: birikimli sayaç da kıpırdamamalı.
$egitmenKazancSonra = [int](Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($tutor.UserId)';").Trim()
if ($egitmenKazancSonra -eq $egitmenKazancOnce) { OK "gönüllü ders TotalEarnedCredits'i artırmadı ($egitmenKazancSonra)" }
else { Fail "kazanç sayacı arttı: $egitmenKazancOnce -> $egitmenKazancSonra" }

$hareket = (Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$($session.sessionId)';").Trim()
if ($hareket -eq '0') { OK 'deftere hiç hareket yazılmadı' } else { Fail "hareket sayısı: $hareket" }

$profil = Get_ "/api/users/$($tutor.UserId)/profile" $student.Token
if ($profil.taughtSessionCount -eq 1) { OK 'anlatılan ders sayacı arttı (gönüllü ders de sayılıyor)' } else { Fail "sayaç: $($profil.taughtSessionCount)" }
if ($profil.taughtMinutes -eq 60) { OK "deneyim süresi arttı ($($profil.taughtMinutes) dk)" } else { Fail "dakika: $($profil.taughtMinutes)" }

Section 'B2. Ücretli ders: onayda eğitmene puan BASILIYOR'

# Gönüllü dersin "0 basım" iddiası ancak KARŞITIYLA anlamlı: aynı akış ücretli derste
# gerçekten basım yapmalı. Karşılaştırma AYRI bir çiftle kuruluyor çünkü (1) $tutor'un
# ilanı gönüllü, ondan ücretli ders çıkmaz; (2) MintGuard aynı çifte 24 saatte 2 ders
# sınırı koyuyor, mevcut çifti daha fazla yüklemek testi 429'a çarpardı.
$ucEgitmen = NewUser 'socpe' $stamp
$ucOgrenci = NewUser 'socpo' $stamp
Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5 } $ucEgitmen.Token | Out-Null
Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek'; selfAssessedLevel = 1 } $ucOgrenci.Token | Out-Null

$ucMatch = Send Post '/api/matches' @{ responderUserId = $ucEgitmen.UserId; requestedTopicId = $topicId } $ucOgrenci.Token
Send Post "/api/matches/$ucMatch/respond" @{ accept = $true } $ucEgitmen.Token | Out-Null

# Karşılaştırmalar mutlak değil FARK üzerinden: yeni kullanıcı hoş geldin kredisiyle
# başlıyor olabilir, bu testin ölçtüğü şey değil.
$egCuzdanOnce = Get_ '/api/wallet' $ucEgitmen.Token
$ogrBakiyeOnce = (Get_ '/api/wallet' $ucOgrenci.Token).currentBalance

$ucSession = Send Post '/api/sessions' @{ matchId = $ucMatch; topicId = $topicId; scheduledStartUtc = [DateTime]::UtcNow.AddHours(3).ToString('o'); durationMinutes = 60 } $ucOgrenci.Token
if ($ucSession.mintAmount -eq 100) { OK 'ödül ölçeği: 60 dk -> 100 puan (her 30 dk için 50)' }
else { Fail "basılacak puan: $($ucSession.mintAmount)" }
# Alanın VARLIĞI ayrıca sınanıyor: alan hiç dönmezse "-not $null" da doğru çıkar ve
# bayrak sessizce kaybolmuş olurdu.
if ($ucSession.PSObject.Properties.Name -contains 'isVolunteer' -and -not $ucSession.isVolunteer) {
    OK 'ücretli ders gönüllü işaretlenmedi (alan var, değeri false)'
} else { Fail "isVolunteer: $($ucSession.isVolunteer)" }

$ogrBakiyeAra = (Get_ '/api/wallet' $ucOgrenci.Token).currentBalance
if ($ogrBakiyeAra -eq $ogrBakiyeOnce) { OK "ücretli rezervasyon da öğrencinin bakiyesine dokunmadı ($ogrBakiyeAra)" }
else { Fail "bakiye: $ogrBakiyeOnce -> $ogrBakiyeAra" }
$ucHold = (Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';").Trim()
if ($ucHold -eq '0') { OK 'ücretli derste de hold yazılmadı' } else { Fail "hold: $ucHold" }

Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '3 hours', ""ScheduledEndUtc"" = now() - interval '2 hours', ""Status"" = 'AwaitingApproval', ""CompletionRequestedAtUtc"" = now() WHERE ""Id"" = '$($ucSession.sessionId)';" | Out-Null

$ucOnay = Send Post "/api/sessions/$($ucSession.sessionId)/approve" @{} $ucOgrenci.Token
if ($ucOnay.creditsMinted -eq 100) { OK 'onayda 100 puan basıldı' } else { Fail "basılan: $($ucOnay.creditsMinted)" }

$ucKazanc = [int](Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($ucEgitmen.UserId)';").Trim()
$ucCuzdan = Get_ '/api/wallet' $ucEgitmen.Token
if (($ucKazanc - $egCuzdanOnce.totalEarnedCredits) -eq 100) { OK 'TotalEarnedCredits basılan miktar kadar arttı (birikimli sayaç)' }
else { Fail "kazanç sayacı: $($egCuzdanOnce.totalEarnedCredits) -> $ucKazanc" }
if (($ucCuzdan.currentBalance - $egCuzdanOnce.currentBalance) -eq 100) { OK 'basılan puan harcanabilir bakiyeye geçti' }
else { Fail "bakiye: $($egCuzdanOnce.currentBalance) -> $($ucCuzdan.currentBalance)" }

# Yeni kazançlar SÜRESİZ: lot'un son kullanma tarihi NULL olabiliyor (eskiden zorunluydu).
$suresizLot = @($ucCuzdan.activeLots | Where-Object { $null -eq $_.expiresAtUtc })
if ($suresizLot.Count -ge 1) { OK 'basılan puan süresiz lot olarak yazıldı (expiresAtUtc NULL)' }
else { Fail "süresiz lot yok (toplam lot: $(@($ucCuzdan.activeLots).Count))" }

# SIFIR TOPLAM DEĞİL: eğitmen kazanırken öğrenciden hiçbir şey eksilmemeli.
$ogrBakiyeSon = (Get_ '/api/wallet' $ucOgrenci.Token).currentBalance
if ($ogrBakiyeSon -eq $ogrBakiyeOnce) { OK 'onaydan sonra da öğrencinin bakiyesi aynı (basım, transfer değil)' }
else { Fail "öğrenci bakiyesi: $ogrBakiyeOnce -> $ogrBakiyeSon" }

# Ders başına TEK basım: ikinci onayın reddedilmesi de sessizce yutulması da kabul,
# ölçülen tek şey İKİNCİ BASIMIN OLMAMASI.
try {
    Send Post "/api/sessions/$($ucSession.sessionId)/approve" @{} $ucOgrenci.Token | Out-Null
    Write-Host '  (ikinci onay hata vermedi; basım sayısına bakılıyor)'
} catch { Write-Host "  (ikinci onay reddedildi: $(HataKodu $_))" }
$ucKazancIkinci = [int](Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($ucEgitmen.UserId)';").Trim()
if ($ucKazancIkinci -eq $ucKazanc) { OK 'ikinci onay İKİNCİ BASIM yapmadı' } else { Fail "kazanç ikinci onayla arttı: $ucKazanc -> $ucKazancIkinci" }

Section 'C. Değerlendirme kuralları'

try {
    Send Post "/api/sessions/$($session.sessionId)/review" @{ score = 5; teachingScore = 5; punctualityScore = 5; tags = @(); comment = $null } $tutor.Token | Out-Null
    Fail 'eğitmen kendi dersini puanlayabildi'
} catch { OK "eğitmen puanlayamıyor ($([int]$_.Exception.Response.StatusCode))" }

try {
    Send Post "/api/sessions/$($session.sessionId)/review" @{ score = 9; teachingScore = 5; punctualityScore = 5; tags = @(); comment = $null } $student.Token | Out-Null
    Fail 'aralık dışı puan kabul edildi'
} catch { OK 'aralık dışı puan reddedildi' }

$rev = Send Post "/api/sessions/$($session.sessionId)/review" @{
    score = 5; teachingScore = 4; punctualityScore = 5
    tags = @('KnowsSubject', 'PatientAndClear', 'StartedOnTime')
    comment = 'Çok sabırlı anlattı, gönüllü olmasına rağmen hazırlıklıydı.'
} $student.Token
OK "değerlendirme kaydedildi (yeni ortalama: $($rev.tutorNewAverage))"

if ($rev.tutorNewAverage -eq 5.0 -and $rev.tutorReviewCount -eq 1) { OK 'ortalama ve sayaç doğru' }
else { Fail "ortalama=$($rev.tutorNewAverage) adet=$($rev.tutorReviewCount)" }

try {
    Send Post "/api/sessions/$($session.sessionId)/review" @{ score = 1; teachingScore = 1; punctualityScore = 1; tags = @(); comment = $null } $student.Token | Out-Null
    Fail 'ikinci değerlendirme kabul edildi'
} catch { OK "ders başına tek değerlendirme ($([int]$_.Exception.Response.StatusCode))" }

Section 'D. Değerlendirme gösterimi'

$yorumlar = Get_ "/api/users/$($tutor.UserId)/reviews" $student.Token
if ($yorumlar.reviewCount -eq 1) { OK 'yorum sayısı doğru' } else { Fail "sayı: $($yorumlar.reviewCount)" }
if ($yorumlar.averageScore -eq 5.0) { OK "ortalama puan: $($yorumlar.averageScore)" } else { Fail "ortalama: $($yorumlar.averageScore)" }
if ($yorumlar.averageTeachingScore -eq 4.0) { OK "anlatım ortalaması ayrı hesaplanıyor ($($yorumlar.averageTeachingScore))" } else { Fail "anlatım: $($yorumlar.averageTeachingScore)" }
if ($yorumlar.scoreDistribution[4] -eq 1) { OK 'yıldız dağılımı doğru (5★ = 1)' } else { Fail "dağılım: $($yorumlar.scoreDistribution -join ',')" }
if ($yorumlar.popularTags.Count -eq 3) { OK "etiket dağılımı üretildi ($($yorumlar.popularTags.Count) etiket)" } else { Fail "etiket: $($yorumlar.popularTags.Count)" }
$ilk = $yorumlar.reviews.items[0]
# İşaretin kaynağı artık dersin IsVolunteer bayrağı (eskiden CreditCost=0 çıkarımıydı):
# ücretli bir dersin ücreti hiç değişmediği için eski çıkarım bugün her dersi "gönüllü"
# göstermeye ya da hiçbirini göstermemeye çok yakındı.
if ($ilk.wasVolunteerSession) { OK 'yorum gönüllü ders olarak işaretlendi (IsVolunteer)' } else { Fail 'gönüllü işareti yok' }
if ($ilk.tags.Count -eq 3) { OK 'yorumun etiketleri döndü' } else { Fail "etiket sayısı: $($ilk.tags.Count)" }

Section 'E. Rozet motoru (veritabanı) ve unvan kartı (profil)'

# Beyan API üzerinden yapılırsa rozetin ANINDA gelmesi gerekir (kayıt sırası hatasına karşı).
$aday2 = NewUser 'socv' $stamp
Invoke-RestMethod -Uri "$Api/api/profile/teacher-candidate" -Method Put `
    -Headers @{ Authorization = "Bearer $($aday2.Token)" } -ContentType 'application/json' `
    -Body ([Text.Encoding]::UTF8.GetBytes((@{ university = 'X Üni'; faculty = 'Eğitim Fakültesi'; department = 'Fen Bilgisi Öğretmenliği'; gradeYear = 2; hasPedagogicalCertificate = $false } | ConvertTo-Json))) | Out-Null

if (RozetVarMi $aday2.UserId 'FutureTeacher') { OK 'beyanla 🌱 rozeti ANINDA verildi' }
else { Fail 'beyan sonrası rozet gelmedi (kayıt sırası hatası)' }


$profil2 = Get_ "/api/users/$($tutor.UserId)/profile" $student.Token

# Vitrin/rozet ucu profilden düştü. Alanın YOKLUĞU sınanıyor: arayüz boş liste okuyup
# "bu kullanıcının rozeti yok" diye göstermeye devam ederse hata sessiz kalırdı.
if ($profil2.PSObject.Properties.Name -notcontains 'badges') { OK 'profil DTO artık rozet listesi döndürmüyor' }
else { Fail 'badges alanı hâlâ dönüyor' }

<#
  EMEKLİLİK DENETİMİ.

  Bu kullanıcı emekli rozetlerin HEPSİNİ hak eden bir geçmişe sahip: ders anlattı
  (FirstLesson), gönüllü ders verdi (VolunteerTutor), değerlendirme aldı. Motor eski
  kurallarını koruyor olsaydı bu satırlar yazılırdı. Tek beklenen rozet 🌱 FutureTeacher
  ve o da bir başarı değil, beyana bağlı kimlik işareti.
#>
Send Put '/api/profile/teacher-candidate' @{ university = 'Test Üniversitesi'; faculty = 'Eğitim Fakültesi'; department = 'Matematik Öğretmenliği'; gradeYear = 3; hasPedagogicalCertificate = $false } $tutor.Token | Out-Null

if (RozetVarMi $tutor.UserId 'FutureTeacher') { OK 'rozet verildi: FutureTeacher (kimlik işareti korunuyor)' }
else { Fail "FutureTeacher eksik: $(RozetDokumu $tutor.UserId)" }

foreach ($emekli in @('FirstLesson', 'FiveStarTeacher', 'FastResponder', 'TenHoursTraded', 'VolunteerTutor')) {
    if (-not (RozetVarMi $tutor.UserId $emekli)) { OK "emekli rozet verilmiyor: $emekli" }
    else { Fail "emekli rozet hâlâ dağıtılıyor: $emekli" }
}

# Eşik kurulumu emeklilik ÖNCESİ TenHoursTraded'ı tetiklerdi; motorun bu veriye artık
# hiç bakmadığını göstermek için aynı kurulum yapılıp sonuç yeniden okunuyor.
Sql "UPDATE identity.""Users"" SET ""TaughtMinutes"" = 600 WHERE ""Id"" = '$($tutor.UserId)';" | Out-Null
Send Put '/api/profile/teacher-candidate' @{ university = 'Test Üniversitesi'; faculty = 'Eğitim Fakültesi'; department = 'Matematik Öğretmenliği'; gradeYear = 4; hasPedagogicalCertificate = $false } $tutor.Token | Out-Null
if (-not (RozetVarMi $tutor.UserId 'TenHoursTraded')) { OK '600 dakika ders anlatmak artık rozet üretmiyor' }
else { Fail 'TenHoursTraded eşiği hâlâ işliyor' }

# Katalog da temiz olmalı: emekli kodların satırı hiç kalmamalı (göç + tohumlayıcı eşitler).
$emekliKatalog = (Sql "SELECT COUNT(*) FROM community.""Badges"" WHERE ""Code"" IN ('FirstLesson','FiveStarTeacher','FastResponder','TenHoursTraded','VolunteerTutor');").Trim()
if ($emekliKatalog -eq '0') { OK 'rozet kataloğunda emekli kod kalmadı' } else { Fail "katalogda emekli kod: $emekliKatalog" }

# Tekilleştirme: motor bu koşumda birkaç kez tetiklendi (yanıtlama, beyan güncelleme x2).
# Hak edilen rozet tek; satır sayısı 1'i aşıyorsa aynı rozet tekrar yazılmış demektir.
$rozetSatir = [int](Sql "SELECT COUNT(*) FROM community.""UserBadges"" WHERE ""UserId"" = '$($tutor.UserId)';").Trim()
if ($rozetSatir -eq 1) { OK "rozetler tekilleştirilmiş ($rozetSatir satır)" }
else { Fail "satır=$rozetSatir (beklenen 1): $(RozetDokumu $tutor.UserId)" }

# Vitrin ucu emekli edildi: hâlâ ayaktaysa 404 DEĞİL 200 döner ve emeklilik yarım kalmış olur.
try {
    Send Put '/api/profile/featured-badges' @{ badgeCodes = @() } $tutor.Token | Out-Null
    Fail 'featured-badges ucu hâlâ yanıt veriyor'
} catch { if ((HataKodu $_) -eq 404) { OK 'featured-badges ucu kaldırılmış (404)' } else { Fail "beklenmeyen kod: $(HataKodu $_)" } }

# --- Profilin YENİ gösterim ekseni: unvan ---
# Gönüllü ders puan BASTIRMADIĞI için eğitmen hâlâ sıfır kazançta ve en alt unvanda.
if ($profil2.totalEarnedCredits -eq 0) { OK 'gönüllü ders birikimli kazancı artırmadı (profilde 0)' }
else { Fail "totalEarnedCredits: $($profil2.totalEarnedCredits)" }
# TÜRKÇE KARAKTER SORUNU ORTADAN KALKTI. Unvan adları diyakritikliydi ve betik BOM'suz
# UTF-8 olduğu için PS 5.1 onları güvenilir okuyamıyordu; karşılaştırma bu yüzden adın
# ASCII kuyruğuyla (-like '*rak') yapılıyordu. Seviye bir tamsayı olduğundan artık
# doğrudan ve tam eşitlikle karşılaştırılabiliyor.
if ([int]$profil2.level -eq 1) { OK "seviye kartı geldi: $($profil2.level). seviye" }
else { Fail "seviye: $($profil2.level)" }
if ($profil2.nextLevelAt -eq 100) { OK 'bir sonraki seviye eşiği bildirildi (100)' } else { Fail "nextLevelAt: $($profil2.nextLevelAt)" }

if ($profil2.teacherCandidate -and -not $profil2.teacherCandidate.isVerified) {
    OK 'öğretmen adaylığı "beyan" olarak işaretli (doğrulanmış değil)'
} else { Fail 'öğretmen adayı durumu hatalı' }

Section 'F. Öğretmen adaylığı doğrulama (hakem paneli)'

$moder = NewUser 'socm' $stamp
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Moderator' WHERE ""Id"" = '$($moder.UserId)';" | Out-Null
$moder.Token = (Send Post '/api/auth/login' @{ email = $moder.Email; password = 'Demo12345'; hwidHash = $moder.Hwid } $null).accessToken

# Kuyruk EN ESKİ beyandan başlar (sırada bekleyen önce). Bu koşumun beyanı en yenisi
# olduğu için SON sayfada aranır — sabit "ilk 100 kayıt" varsayımı, veri biriktikçe
# sessizce kırılırdı.
$ilkSayfa = Get_ '/api/admin/teacher-candidates?status=Pending&pageSize=25' $moder.Token
$sonSayfa = Get_ "/api/admin/teacher-candidates?status=Pending&pageSize=25&page=$([Math]::Max(1, $ilkSayfa.totalPages))" $moder.Token
$satir = $sonSayfa.items | Where-Object { $_.userId -eq $aday2.UserId }
if ($satir) { OK 'yeni beyan hakem kuyruğunda göründü' } else { Fail 'beyan kuyrukta yok' }
if ($satir.reviewStatus -eq 'Pending') { OK 'durum: karar bekliyor' } else { Fail "durum: $($satir.reviewStatus)" }

# Gerekçe zorunlu: sistemde belge kanalı olmadığı için kararın tek dayanağı bu not.
try {
    Send Post "/api/admin/teacher-candidates/$($satir.profileId)/review" @{ decision = 'Verify'; note = '' } $moder.Token | Out-Null
    Fail 'gerekçesiz karar kabul edildi'
} catch { OK "gerekçesiz karar reddedildi ($(HataKodu $_))" }

$sonuc = Send Post "/api/admin/teacher-candidates/$($satir.profileId)/review" `
    @{ decision = 'Verify'; note = 'Öğrenci belgesi e-posta ile teyit edildi.' } $moder.Token
if ($sonuc.reviewStatus -eq 'Verified') { OK 'beyan doğrulandı' } else { Fail "sonuç: $($sonuc.reviewStatus)" }

$baskasi = Get_ "/api/users/$($aday2.UserId)/profile" $student.Token
if ($baskasi.teacherCandidate.isVerified) { OK 'profilde "Doğrulandı" görünüyor' } else { Fail 'doğrulama profile yansımadı' }
if (-not $baskasi.teacherCandidate.reviewNote) { OK 'hakem notu BAŞKASINA gösterilmiyor' } else { Fail 'not sızdı' }

$kendi = Get_ "/api/users/$($aday2.UserId)/profile" $aday2.Token
if ($kendi.teacherCandidate.reviewNote) { OK 'hakem notu kişinin KENDİSİNE gösteriliyor' } else { Fail 'kendi notunu göremiyor' }

# Doğrulanmış beyan kilitli: aksi halde onay alındıktan sonra iddia değiştirilebilirdi.
try {
    Send Put '/api/profile/teacher-candidate' @{ university = 'Baska Uni'; faculty = 'Eğitim Fakültesi'; department = 'Tarih Öğretmenliği'; gradeYear = 4; hasPedagogicalCertificate = $false } $aday2.Token | Out-Null
    Fail 'doğrulanmış beyan değiştirilebildi'
} catch { OK "doğrulanmış beyan kilitli ($(HataKodu $_))" }

$geri = Send Post "/api/admin/teacher-candidates/$($satir.profileId)/review" `
    @{ decision = 'Revert'; note = 'Bölüm değişikliği bildirildi, yeniden inceleme.' } $moder.Token
if ($geri.reviewStatus -eq 'Pending') { OK 'karar geri alındı, beyan yeniden kuyrukta' } else { Fail "durum: $($geri.reviewStatus)" }

Send Put '/api/profile/teacher-candidate' @{ university = 'X Üni'; faculty = 'Eğitim Fakültesi'; department = 'Tarih Öğretmenliği'; gradeYear = 4; hasPedagogicalCertificate = $false } $aday2.Token | Out-Null
OK 'karar geri alınınca kullanıcı beyanını güncelleyebildi'

$ret = Send Post "/api/admin/teacher-candidates/$($satir.profileId)/review" `
    @{ decision = 'Reject'; note = 'Belge gönderilmedi.' } $moder.Token
if ($ret.reviewStatus -eq 'Rejected') { OK 'beyan reddedildi' } else { Fail "durum: $($ret.reviewStatus)" }
if ($ret.badgeRemoved) { OK '🌱 rozeti geri alındı (asılsız bulunan OLGU iddiası)' } else { Fail 'rozet duruyor' }

$retSonrasi = Get_ "/api/users/$($aday2.UserId)/profile" $aday2.Token
# Rozetin gerçekten silindiği artık yalnızca kayıttan gözlenebiliyor (profil rozet dönmüyor).
if (-not (RozetVarMi $aday2.UserId 'FutureTeacher')) { OK 'rozet kaydı silindi' } else { Fail 'rozet satırı duruyor' }
if ($retSonrasi.teacherCandidate.reviewStatus -eq 'Rejected') { OK 'kişi kendi ret durumunu görüyor' } else { Fail "durum: $($retSonrasi.teacherCandidate.reviewStatus)" }

# Ret BAŞKASINA teşhir edilmez: dışarıdan doğrulanmamış bir beyandan ayırt edilemez.
$retBaskasi = Get_ "/api/users/$($aday2.UserId)/profile" $student.Token
if ($retBaskasi.teacherCandidate.reviewStatus -eq 'Pending' -and -not $retBaskasi.teacherCandidate.reviewNote) {
    OK 'ret durumu başkasına "Beyan" olarak görünüyor'
} else { Fail "sızan durum: $($retBaskasi.teacherCandidate.reviewStatus)" }

try {
    Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 4; isVolunteer = $true } $aday2.Token | Out-Null
    Fail 'reddedilen beyanla gönüllü ilan açılabildi'
} catch { OK "reddedilen beyan gönüllü ilan açamıyor ($(HataKodu $_))" }

# Ret kalıcı ceza değil: düzeltilen beyan yeniden incelemeye girer, rozet geri gelir.
Send Put '/api/profile/teacher-candidate' @{ university = 'X Üni'; faculty = 'Eğitim Fakültesi'; department = 'Fen Bilgisi Öğretmenliği'; gradeYear = 2; hasPedagogicalCertificate = $false } $aday2.Token | Out-Null
$yeniden = Get_ "/api/users/$($aday2.UserId)/profile" $aday2.Token
if ($yeniden.teacherCandidate.reviewStatus -eq 'Pending') { OK 'güncellenen beyan yeniden incelemeye girdi' } else { Fail "durum: $($yeniden.teacherCandidate.reviewStatus)" }
if (RozetVarMi $aday2.UserId 'FutureTeacher') { OK 'rozet geri geldi' } else { Fail 'rozet gelmedi' }
if (-not $yeniden.teacherCandidate.reviewNote) { OK 'eski ret notu temizlendi' } else { Fail 'eski not duruyor' }

Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 4; isVolunteer = $true } $aday2.Token | Out-Null
OK 'yeniden beyanla gönüllü ilan açılabildi'

# "Kararı geri al" KENDİ YAN ETKİSİNİ de geri almalı: ret rozetin satırını siliyor, geri
# alma onu iade etmeli. Aksi halde moderatör hatasını düzelttiğini sanır ama kullanıcı
# hak ettiği rozeti yeniden beyan verene kadar kaybetmiş olur.
$yenidenRet = Send Post "/api/admin/teacher-candidates/$($satir.profileId)/review" `
    @{ decision = 'Reject'; note = 'İkinci inceleme: belge yine yok.' } $moder.Token
if ($yenidenRet.badgeRemoved) { OK 'ikinci retle rozet yine geri alındı' } else { Fail 'rozet silinmedi' }

$iade = Send Post "/api/admin/teacher-candidates/$($satir.profileId)/review" `
    @{ decision = 'Revert'; note = 'Ret hatalıydı, karar geri alındı.' } $moder.Token
if ($iade.badgeRestored) { OK 'kararı geri alma 🌱 rozetini İADE etti' } else { Fail 'rozet iade edilmedi' }

if (RozetVarMi $aday2.UserId 'FutureTeacher') { OK 'iade edilen rozetin kaydı geri geldi' }
else { Fail "rozet satırları: $(RozetDokumu $aday2.UserId)" }

$iz = (Get_ '/api/admin/audit-log?pageSize=50' $moder.Token).items |
    Where-Object { $_.targetId -eq $satir.profileId -and $_.action -eq 'TeacherCandidateRejected' } | Select-Object -First 1
if ($iz) { OK 'ret denetim izine yazıldı' } else { Fail 'denetim izi yok' }
if ($iz.actorRole -eq 'Moderator') { OK 'karar anındaki rol kaydedildi (Moderator)' } else { Fail "rol: $($iz.actorRole)" }

$metrik = Get_ '/api/admin/metrics' $moder.Token
if ($metrik.pendingTeacherCandidates -ge 1) { OK "bekleyen aday sayacı metriklerde ($($metrik.pendingTeacherCandidates))" }
else { Fail "sayaç: $($metrik.pendingTeacherCandidates)" }

# Doğrulanmış + reddedilmiş AYNI ANDA olamaz (DB kısıtı son savunma).
$celiski = (Sql 'SELECT COUNT(*) FROM identity."TeacherCandidateProfiles" WHERE "VerifiedAtUtc" IS NOT NULL AND "RejectedAtUtc" IS NOT NULL;').Trim()
if ($celiski -eq '0') { OK 'çelişkili durum (hem doğrulanmış hem reddedilmiş) yok' } else { Fail "çelişkili kayıt: $celiski" }

Section 'G. Unvan eşikleri (rozet vitrininin yerine geçen gösterim)'

<#
  Rozet vitrini profil DTO'sundan düştüğü için "en fazla 3 rozet", "sahip olunmayan
  rozet 409", "vitrin sırası", "idempotentlik" iddialarının doğrulanacağı bir gözlem
  kanalı kalmadı; o bölüm kaldırıldı (gerekçesi dosya başındaki kapsam notunda).

  Profilin yeni övünme ekseni UNVAN. Eşikler ALT sınır DAHİL, ÜST sınır HARİÇ tanımlı;
  bu yüzden her eşikte İKİ nokta sınanıyor: bir altı ve tam kendisi. Yalnızca "tam
  eşik" sınanmış olsaydı, ">" yerine ">=" yazılması (unvanın bir puan erken verilmesi)
  görünmez kalırdı.

  Sayaç birikimli ve ASLA azalmadığı için değerler ARTAN sırada veriliyor: test kurulumu
  bile "geri sayma" senaryosu üretmemeli.
#>

$unvanci = NewUser 'socu' $stamp

# Seviye bir TAMSAYI olduğu için tam eşitlikle karşılaştırılıyor. Eski tablo unvan
# adlarını ASCII kuyruklarıyla (-like '*rak') yokluyordu; o yaklaşım bir kaçak bırakıyordu:
# "Çırak" ile "Karak" aynı desene uyardı. Rakamda böyle bir belirsizlik yok.
$esikler = @(
    @{ Puan = 0    ; Seviye = 1  ; Sonraki = 100 },
    @{ Puan = 99   ; Seviye = 1  ; Sonraki = 100 },
    @{ Puan = 100  ; Seviye = 2  ; Sonraki = 200 },
    @{ Puan = 199  ; Seviye = 2  ; Sonraki = 200 },
    @{ Puan = 200  ; Seviye = 3  ; Sonraki = 350 },
    @{ Puan = 349  ; Seviye = 3  ; Sonraki = 350 },
    @{ Puan = 350  ; Seviye = 4  ; Sonraki = 600 },
    @{ Puan = 600  ; Seviye = 5  ; Sonraki = 1000 },
    @{ Puan = 1000 ; Seviye = 6  ; Sonraki = 1750 },
    @{ Puan = 1750 ; Seviye = 7  ; Sonraki = 3000 },
    @{ Puan = 3000 ; Seviye = 8  ; Sonraki = 5500 },
    @{ Puan = 5500 ; Seviye = 9  ; Sonraki = 10000 },
    @{ Puan = 9999 ; Seviye = 9  ; Sonraki = 10000 },
    @{ Puan = 10000; Seviye = 10 ; Sonraki = 0 }
)

$seviyeHatasi = @()
$esikHatasi = @()
foreach ($e in $esikler) {
    Sql "UPDATE identity.""Users"" SET ""TotalEarnedCredits"" = $($e.Puan) WHERE ""Id"" = '$($unvanci.UserId)';" | Out-Null
    $p = Get_ "/api/users/$($unvanci.UserId)/profile" $student.Token
    if ([int]$p.level -ne $e.Seviye) { $seviyeHatasi += "$($e.Puan)->$($p.level) (beklenen $($e.Seviye))" }
    if ($p.totalEarnedCredits -ne $e.Puan) { $seviyeHatasi += "$($e.Puan): profil kazancı $($p.totalEarnedCredits)" }
    if ($e.Sonraki -eq 0) {
        # En üst seviyede gidilecek eşik yok: alan boş/0 dönmeli.
        if ($p.nextLevelAt) { $esikHatasi += "$($e.Puan): en üstte nextLevelAt=$($p.nextLevelAt)" }
    } elseif ($p.nextLevelAt -ne $e.Sonraki) {
        $esikHatasi += "$($e.Puan): nextLevelAt=$($p.nextLevelAt) (beklenen $($e.Sonraki))"
    }
}
if ($seviyeHatasi.Count -eq 0) { OK "seviye eşikleri doğru ($($esikler.Count) nokta; alt sınır dahil, üst sınır hariç)" }
else { Fail "yanlış seviye: $($seviyeHatasi -join ' | ')" }
if ($esikHatasi.Count -eq 0) { OK 'her seviyede bir sonraki eşik doğru bildirildi' }
else { Fail "yanlış eşik: $($esikHatasi -join ' | ')" }

# Aynı kullanıcı için cüzdan ve profil AYNI seviyeyi söylemeli (iki ayrı DTO, tek kaynak).
$unvanciCuzdan = Get_ '/api/wallet' $unvanci.Token
if ([int]$unvanciCuzdan.level -eq 10 -and $unvanciCuzdan.totalEarnedCredits -eq 10000) {
    OK "cüzdan ve profil aynı seviyeyi bildiriyor ($($unvanciCuzdan.level). seviye)"
} else { Fail "cüzdan seviyesi: $($unvanciCuzdan.level) / $($unvanciCuzdan.totalEarnedCredits)" }

# Seviye HARCANABİLİR bakiyeden değil, BİRİKİMLİ kazançtan hesaplanmalı: bu kullanıcı hiç
# ders vermeden 10.000 kazanç sayacına sahip ama cüzdanı boş sayılır; seviye yine de en üst.
if ($unvanciCuzdan.currentBalance -ne $unvanciCuzdan.totalEarnedCredits) {
    OK "seviye, harcanabilir bakiyeden bağımsız (bakiye $($unvanciCuzdan.currentBalance), kazanç $($unvanciCuzdan.totalEarnedCredits))"
} else { Fail "bakiye ve birikimli kazanç ayrışmadı: $($unvanciCuzdan.currentBalance)" }

# Birikimli sayaç ASLA azalmaz: eğitmenin puanı harcansa bile seviyesi geri gitmemeli.
# (Harcama ucu bu pakette yok; sayacın bakiyeden bağımsızlığı yukarıda gösterildi.)
$unvanciSatir = (Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($unvanci.UserId)';").Trim()
if ($unvanciSatir -eq '10000') { OK 'birikimli kazanç sütunu profil ile tutarlı' } else { Fail "sütun: $unvanciSatir" }

<#
  KURULUM GERİ ALINIYOR — bu adım atlanırsa BAŞKA BİR PAKET kırılır.

  Yukarıdaki eşik taraması sayacı elle 10.000'e kadar yazdı; bu değerin defterde
  karşılığı YOK (hiç ders anlatılmadı, hiç LessonEarning hareketi yazılmadı).
  e2e-jobs.ps1 ise küresel bir eşitlik ölçüyor: her kullanıcının TotalEarnedCredits'i
  defterdeki LessonEarning toplamına eşit olmalı. Sayaç bırakıldığı gibi kalırsa o
  kontrol HER koşumda kırmızı verir ve hata, sebebiyle alakasız bir pakette görünür —
  teşhis edilmesi en zor test kirlenmesi türü budur.

  Sıfıra çekiliyor çünkü bu kullanıcı hiç ders anlatmadı; defterle tutarlı tek değer 0.
#>
Sql "UPDATE identity.""Users"" SET ""TotalEarnedCredits"" = 0 WHERE ""Id"" = '$($unvanci.UserId)';" | Out-Null
$unvanciTemiz = (Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($unvanci.UserId)';").Trim()
if ($unvanciTemiz -eq '0') { OK 'eşik kurulumu geri alındı (sayaç defterle tutarlı)' }
else { Fail "kurulum geri alınamadı, sayaç: $unvanciTemiz" }


Section 'H. Eşleşme yanıt damgası'

<#
  ⚡ HIZLI YANIT SENARYOLARI KALDIRILDI (10 senaryo, ~160 satır).

  Ölçtükleri kural motordan silindi: rozet artık verilmediği için "eşiğin iki yakası",
  "ortanca vs ortalama", "taze bekleyen istek kurtarmamalı" gibi iddiaların gözlenecek
  bir çıktısı yok. Emekliliğin kendisi G bölümünde sınanıyor.

  AMA AŞAĞIDAKİ İKİ İDDİA KALDI ve kasıtlı: bunlar rozete değil, eşleşme verisinin
  bütünlüğüne ait. RespondedAtUtc yanlış dolduruluyorsa bu, rozetten bağımsız olarak
  "yanıtlandı mı" sorusunu soran her yeri (gelen kutusu, süpürücü, panel) yanıltır.
  Rozetle birlikte silinselerdi, kuralın gittiğini fark etmeyen bir alan sessizce
  korumasız kalırdı.
#>

# Yanıt damgası: kabul/ret edilen her eşleşmede DOLU olmalı.
$zamansiz = (Sql "SELECT COUNT(*) FROM matchmaking.""Matches"" WHERE ""Status"" IN ('Accepted','Declined') AND ""RespondedAtUtc"" IS NULL;").Trim()
if ($zamansiz -eq '0') { OK 'kabul/ret edilen her eşleşmede RespondedAtUtc dolu' } else { Fail "zamansız kayıt: $zamansiz" }

# ...ve tersi: süresi dolan istek bir YANIT DEĞİLDİR, damga ASLA atılmamalı.
$yanlisDamga = (Sql "SELECT COUNT(*) FROM matchmaking.""Matches"" WHERE ""Status"" = 'Expired' AND ""RespondedAtUtc"" IS NOT NULL;").Trim()
if ($yanlisDamga -eq '0') { OK 'süresi dolan istek yanıt sayılmıyor (damga yok)' } else { Fail "damgalı süresi dolmuş kayıt: $yanlisDamga" }

Section 'I. Ekonomi değişmezleri (basım modeli)'

# Defter değişmezi ARTIK "basılan - yakılan" DEĞİL: panelde ledgerBalanced'in ölçtüğü şey
# cüzdan toplamının defterdeki hareket toplamına eşitliği. Tür filtresi kaldırıldı; basım
# da bir hareket olduğu için WelcomeBonus/Expiry ayıklaması bugün defteri YANLIŞ ölçerdi.
#
# Genişletilen here-string (@"..."@) içinde tırnak İKİLENMEZ; ikilenirse PostgreSQL
# boş tanımlayıcı görür. Tek tırnaklı here-string (@'...'@) kullanılıyor.
$defter = (Sql @'
SELECT (SELECT COALESCE(SUM("AvailableBalance"),0) FROM economy."Wallets")
     - (SELECT COALESCE(SUM("Amount"),0) FROM economy."CreditTransactions");
'@).Trim()
if ($defter -eq '0') { OK 'defter dengeli: cüzdan toplamı = hareket toplamı' } else { Fail "defter farkı: $defter" }

# Panel de AYNI şeyi söylemeli: ledgerBalanced artık "basılan - yakılan" değil, yukarıdaki
# eşitliği ölçüyor. İki kaynağın ayrışması, panelin eski formülde kaldığını gösterir.
$defterMetrik = Get_ '/api/admin/metrics' $moder.Token
if ($defterMetrik.ledgerBalanced) { OK 'panel defteri dengeli bildiriyor (ledgerBalanced)' }
else { Fail "panel ledgerBalanced=$($defterMetrik.ledgerBalanced) ama SQL farkı $defter" }

$negatif = (Sql "SELECT COUNT(*) FROM economy.""Wallets"" WHERE ""AvailableBalance"" < 0;").Trim()
if ($negatif -eq '0') { OK 'negatif bakiye yok' } else { Fail "negatif cüzdan: $negatif" }

# Birikimli kazanç sayacı ASLA azalmaz; negatife düşmüş bir satır, sayacın bir yerde
# "geri alma" olarak kullanıldığını gösterirdi.
$negKazanc = (Sql 'SELECT COUNT(*) FROM identity."Users" WHERE "TotalEarnedCredits" < 0;').Trim()
if ($negKazanc -eq '0') { OK 'negatif birikimli kazanç yok' } else { Fail "negatif kazanç sayacı: $negKazanc" }

# Gönüllülük ölçütü artık IsVolunteer (eskiden CreditCost = 0 çıkarımıydı).
$gonulluHold = (Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';").Trim()
if ($gonulluHold -eq '0') { OK 'gönüllü derslerin hiçbirinde escrow kaydı yok' } else { Fail "hatalı hold: $gonulluHold" }

# Yeni hold HİÇ yazılmıyor: bu koşumun açtığı iki ders (gönüllü + ücretli) de temiz olmalı.
$kosumHold = (Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';").Trim()
if ($kosumHold -eq '0') { OK 'bu koşumda hiç hold satırı yazılmadı' } else { Fail "hold: $kosumHold" }

# Süre kısıtı veri tarafında da son savunma: 30/60 dışında ders kalmamalı.
$gecersizSure = (Sql 'SELECT COUNT(*) FROM scheduling."LessonSessions" WHERE "DurationMinutes" NOT IN (30, 60);').Trim()
if ($gecersizSure -eq '0') { OK 'süre kümesi dışında ders yok (30/60)' } else { Fail "geçersiz süreli ders: $gecersizSure" }

Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green }
else { Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red; exit 1 }
Write-Host "================================" -ForegroundColor White
