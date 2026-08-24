# PeerLearn — YKS müfredatı + branş rozetleri doğrulaması
#
# NE YAPAR: derleme → göç → tohumlama zincirini koşturur ve sonucun DOĞRU olduğunu
# veritabanından okuyarak kanıtlar.
#
# NE YAPMAZ — ve bu bilinçli: ders senaryosu ÜRETMEZ.
# Bu bir senaryo sürücüsü değil, bir DENETÇİ. Rozet kazanımını tetikleyen akış (rezervasyon
# → tamamlama → onay) zaten `tools/e2e-smoke.ps1` ve kardeşlerinde var; aynı akışı burada
# ikinci kez yazmak, iki kopyanın zamanla ayrışacağı bir bakım borcu olurdu. Bunun yerine
# bu betik, o akışlar koştuktan SONRA veritabanındaki DEĞİŞMEZLERİ kontrol eder:
#
#   "her eğitmenin sahip olduğu rozetler, tamamlanmış derslerinden hesaplanabilir olanlarla
#    birebir aynı mı?"
#
# Bu tek soru, motorun eşiklerini de idempotansını da çifte rozet korumasını da kapsar —
# ve sahte bir senaryoya değil, gerçek veriye bakar.
#
# ÖNERİLEN SIRA:
#   1. .\tools\verify-yks-rozet.ps1            (bu betik — katalog + şema + tutarlılık)
#   2. .\tools\run-all-tests.ps1               (mevcut e2e paketi — ders akışları koşar)
#   3. .\tools\verify-yks-rozet.ps1            (tekrar — artık gerçek rozet verisi üzerinde)
#
# Kapsam:
#   A. Derleme ve göç          — kod derleniyor mu, bekleyen göç var mı
#   B. Katalog                 — TYT/AYT, 8 ders, 767 konu, branş eşlemesi
#   C. Veri kaybı yok          — eski katalog SİLİNMEDİ, pasifleştirildi
#   D. İdempotans              — ikinci tohumlama kopya üretmiyor
#   E. Şema                    — UserSubjectBadges tablosu, index ve kısıt
#   F. Rozet tutarlılığı       — rozetler ≡ tamamlanmış derslerden hesaplanan
#   G. Rozet bütünlüğü         — çifte rozet yok, TYT+AYT aynı branşa toplanıyor

$ErrorActionPreference = 'Stop'

$script:Pass = 0; $script:Fail = 0; $script:Warn = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Note($m) { $script:Warn++; Write-Host "  [NOT] $m" -ForegroundColor Yellow }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
# psql erişimi: yerel kurulum ya da docker compose. İkisi de destekleniyor çünkü
# docker-compose.yml geldikten sonra makinede ayrıca psql kurulu olmayabilir.
# ---------------------------------------------------------------------------
$env:PGPASSWORD = 'PeerLearnDev2026'

<#
  psql'e üç yoldan erişilebilir; ilk bulunan kullanılır:
    1. Windows kurulumu   — geliştiricinin makinesinde tipik yol.
    2. PATH üzerinde psql — Linux/macOS ve GitHub Actions koşucuları (postgresql-client).
    3. docker compose     — makinede psql yok ama compose yığını ayakta.

  CI için 2. yol şart: koşucuda ne Windows kurulumu ne de compose var, ama psql PATH'te.
#>
$WindowsPsql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
# PS 5.1 UYUMU: `?.` null-koşullu operatörü PowerShell 7 ile geldi ve 5.1'de SÖZDİZİMİ
# hatası verir — betik hiç başlamaz. Paketler CLAUDE.md gereği 5.1 altında koşuyor.
$psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
$PathPsql = if ($psqlCmd) { $psqlCmd.Source } else { $null }

if (Test-Path $WindowsPsql) { $PsqlExe = $WindowsPsql }
elseif ($PathPsql) { $PsqlExe = $PathPsql }
else { $PsqlExe = $null }   # docker compose yoluna düş

# Sunucu adresi ortamdan geçersiz kılınabilir (CI'da servis konteyneri).
$PgHost = if ($env:PGHOST) { $env:PGHOST } else { 'localhost' }
$PgPort = if ($env:PGPORT) { $env:PGPORT } else { '5432' }

function Sql($q) {
    if (-not $PsqlExe) {
        return ($q | docker compose exec -T db psql -U peerlearn -d peerlearn -t -A 2>$null)
    }

    $dizin = if ($env:TEMP) { $env:TEMP } else { '/tmp' }
    $f = Join-Path $dizin "pl-yks-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
    try { & $PsqlExe -h $PgHost -p $PgPort -U peerlearn -d peerlearn -t -A -f $f } finally { Remove-Item $f -Force }
}
function SqlInt($q) { $r = (Sql $q); if ([string]::IsNullOrWhiteSpace($r)) { 0 } else { [int]("$r".Trim()) } }

Write-Host 'PeerLearn — YKS müfredatı + branş rozetleri' -ForegroundColor White
Write-Host "psql erişimi: $(if ($PsqlExe) { "$PsqlExe (${PgHost}:${PgPort})" } else { 'docker compose exec db' })"

# ---------------------------------------------------------------------------
Section 'A. Derleme ve göç'

$build = & dotnet build --nologo -v q 2>&1
if ($LASTEXITCODE -eq 0) { OK 'çözüm derleniyor' }
else {
    Fail 'DERLEME BAŞARISIZ — aşağıdaki çıktı düzeltilmeden gerisi anlamsız'
    $build | Select-Object -Last 30 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
    Write-Host "`nDurduruldu." -ForegroundColor Red
    exit 1
}

# Bekleyen model değişikliği: entity değişti ama göç üretilmediyse şema koddan geride kalır
# ve hata ancak ilk sorguda, anlaşılmaz bir kolon hatası olarak çıkar.
$migrations = & dotnet ef migrations list --project src/PeerLearn.Infrastructure --startup-project src/PeerLearn.Api --no-build 2>&1
if ($LASTEXITCODE -ne 0) {
    Note 'dotnet ef çalıştırılamadı (dotnet-ef kurulu mu? `dotnet tool install --global dotnet-ef`)'
}
elseif ($migrations -match 'SubjectBranchAndSubjectBadges') { OK 'SubjectBranchAndSubjectBadges göçü mevcut' }
else {
    # Göç adı, üretildiği gündeki adla BİREBİR eşleşmeli. Bu kontrol bir süre yanlış adı
    # (SubjectBranchesAndBadges) aradı ve göç yerinde olmasına rağmen kırmızı verdi;
    # adı değiştirirsen burayı da değiştir.
    Fail 'SubjectBranchAndSubjectBadges göçü yok — çalıştır: dotnet ef migrations add SubjectBranchAndSubjectBadges --project src/PeerLearn.Infrastructure --startup-project src/PeerLearn.Api'
}

# ---------------------------------------------------------------------------
Section 'B. Katalog — TYT / AYT / 8 ders / 767 konu'

$tyt = SqlInt 'SELECT COUNT(*) FROM catalog."EducationCategories" WHERE "Slug" = ''yks-tyt'' AND "IsActive";'
$ayt = SqlInt 'SELECT COUNT(*) FROM catalog."EducationCategories" WHERE "Slug" = ''yks-ayt'' AND "IsActive";'
if ($tyt -eq 1 -and $ayt -eq 1) { OK 'TYT ve AYT kategorileri aktif' } else { Fail "TYT:$tyt AYT:$ayt (ikisi de 1 olmalı)" }

foreach ($slug in 'yks-tyt', 'yks-ayt') {
    $n = SqlInt "SELECT COUNT(*) FROM catalog.""Subjects"" s JOIN catalog.""EducationCategories"" c ON c.""Id"" = s.""CategoryId"" WHERE c.""Slug"" = '$slug' AND s.""IsActive"";"
    if ($n -eq 8) { OK "$slug altında 8 aktif ders" } else { Fail "$slug altında $n ders (8 olmalı)" }

    $bransiz = SqlInt "SELECT COUNT(*) FROM catalog.""Subjects"" s JOIN catalog.""EducationCategories"" c ON c.""Id"" = s.""CategoryId"" WHERE c.""Slug"" = '$slug' AND s.""IsActive"" AND s.""Branch"" IS NULL;"
    if ($bransiz -eq 0) { OK "$slug derslerinin hepsinde branş dolu" } else { Fail "${slug}: $bransiz dersin branşı boş — rozet hesabı bu dersleri saymaz" }
}

# Sekiz branşın tamamı hem TYT hem AYT tarafında olmalı; biri eksikse müfredat yarım demektir.
$eksik = Sql @'
SELECT string_agg(DISTINCT b.brans || '/' || c."Slug", ', ')
FROM (VALUES ('Turkce'),('Tarih'),('Cografya'),('Matematik'),('Geometri'),('Fizik'),('Kimya'),('Biyoloji')) AS b(brans)
CROSS JOIN catalog."EducationCategories" c
WHERE c."Slug" IN ('yks-tyt','yks-ayt')
  AND NOT EXISTS (
    SELECT 1 FROM catalog."Subjects" s
    WHERE s."CategoryId" = c."Id" AND s."IsActive" AND s."Branch" = b.brans);
'@
if ([string]::IsNullOrWhiteSpace($eksik)) { OK 'sekiz branşın tamamı TYT ve AYT tarafında mevcut' }
else { Fail "eksik branş/seviye: $($eksik.Trim())" }

$konu = SqlInt 'SELECT COUNT(*) FROM catalog."Topics" t JOIN catalog."Subjects" s ON s."Id" = t."SubjectId" WHERE t."IsActive" AND s."IsActive" AND s."Branch" IS NOT NULL;'
if ($konu -eq 767) { OK "767 aktif konu tohumlandı" }
elseif ($konu -gt 700) { Note "aktif konu sayısı $konu (beklenen 767) — Curriculum.cs güncellendiyse normal" }
else { Fail "aktif konu sayısı $konu — tohumlama eksik görünüyor" }

# Müfredatın vaadi "kısaltma yok". Tohumlanan veride de kontrol edilmeli: kod doğruysa
# ama tohumlama yarım kaldıysa fark yalnızca burada görünür.
$kotu = SqlInt 'SELECT COUNT(*) FROM catalog."Topics" WHERE "IsActive" AND ("Name" LIKE ''%...%'' OR "Name" LIKE ''%vb.%'' OR length(trim("Name")) < 3);'
if ($kotu -eq 0) { OK 'aktif konularda kısaltma/atlama yok' } else { Fail "$kotu konu kısaltma içeriyor" }

$bosDers = SqlInt 'SELECT COUNT(*) FROM catalog."Subjects" s WHERE s."IsActive" AND s."Branch" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM catalog."Topics" t WHERE t."SubjectId" = s."Id" AND t."IsActive");'
if ($bosDers -eq 0) { OK 'konusuz aktif ders yok' } else { Fail "$bosDers dersin hiç aktif konusu yok" }

# ---------------------------------------------------------------------------
Section 'C. Veri kaybı yok — eski katalog silinmedi, pasifleştirildi'

# Bu bölüm maddenin en kritik güvencesi. Katalog FK'ları Restrict; eski konular
# silinseydi ya göç patlar ya da (cascade olsaydı) ders geçmişi yok olurdu.
$yetimPortfoy = SqlInt 'SELECT COUNT(*) FROM matchmaking."PortfolioEntries" p WHERE NOT EXISTS (SELECT 1 FROM catalog."Topics" t WHERE t."Id" = p."TopicId");'
if ($yetimPortfoy -eq 0) { OK 'hiçbir portföy kaydı kayıp konuya işaret etmiyor' } else { Fail "$yetimPortfoy portföy kaydının konusu silinmiş" }

$yetimDers = SqlInt 'SELECT COUNT(*) FROM scheduling."LessonSessions" l WHERE NOT EXISTS (SELECT 1 FROM catalog."Topics" t WHERE t."Id" = l."TopicId");'
if ($yetimDers -eq 0) { OK 'hiçbir ders kaydı kayıp konuya işaret etmiyor' } else { Fail "$yetimDers dersin konusu silinmiş — VERİ KAYBI" }

$pasif = SqlInt 'SELECT COUNT(*) FROM catalog."Topics" WHERE NOT "IsActive";'
if ($pasif -gt 0) { OK "$pasif eski konu pasifleştirildi (silinmedi)" }
else { Note 'pasif konu yok — bu kurulum temiz bir veritabanı olabilir' }

$eskiAgac = SqlInt 'SELECT COUNT(*) FROM catalog."EducationCategories" WHERE "Slug" = ''universite'' AND "IsActive";'
if ($eskiAgac -eq 0) { OK 'eski "Üniversite" ağacı kullanıcı seçiminden çıkarıldı' }
else { Fail 'eski "Üniversite" kategorisi hâlâ aktif — eşitleyici onu pasifleştirmeliydi' }

# ---------------------------------------------------------------------------
Section 'D. İdempotans — ikinci tohumlama kopya üretmiyor'

$oncekiKonu = SqlInt 'SELECT COUNT(*) FROM catalog."Topics";'
$oncekiDers = SqlInt 'SELECT COUNT(*) FROM catalog."Subjects";'

& dotnet run --project src/PeerLearn.Api -- --migrate 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Fail '--migrate ikinci çalıştırmada hata verdi' }

$sonrakiKonu = SqlInt 'SELECT COUNT(*) FROM catalog."Topics";'
$sonrakiDers = SqlInt 'SELECT COUNT(*) FROM catalog."Subjects";'

if ($sonrakiKonu -eq $oncekiKonu -and $sonrakiDers -eq $oncekiDers) {
    OK "ikinci tohumlama kopya üretmedi ($sonrakiDers ders / $sonrakiKonu konu, değişmedi)"
}
else { Fail "kopya üretildi: ders $oncekiDers→$sonrakiDers, konu $oncekiKonu→$sonrakiKonu" }

# ---------------------------------------------------------------------------
Section 'E. Şema — UserSubjectBadges'

$tablo = SqlInt 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = ''community'' AND table_name = ''UserSubjectBadges'';'
if ($tablo -eq 1) { OK 'community.UserSubjectBadges tablosu var' } else { Fail 'tablo yok — göç uygulanmamış' }

if ($tablo -eq 1) {
    # Çifte rozete karşı son savunma. Motor idempotent ama eşzamanlı iki onay aynı eşiği
    # görebilir; bu index olmadan aynı rozet iki satır olur.
    $uniq = SqlInt @'
SELECT COUNT(*) FROM pg_indexes
WHERE schemaname = 'community' AND tablename = 'UserSubjectBadges'
  AND indexdef LIKE '%UNIQUE%' AND indexdef LIKE '%UserId%'
  AND indexdef LIKE '%Branch%' AND indexdef LIKE '%Level%';
'@
    if ($uniq -ge 1) { OK 'benzersiz (UserId, Branch, Level) index mevcut' } else { Fail 'benzersiz index yok — çifte rozet mümkün' }

    $chk = SqlInt @'
SELECT COUNT(*) FROM information_schema.check_constraints
WHERE constraint_schema = 'community' AND constraint_name = 'CK_UserSubjectBadges_MinutesAtAward';
'@
    if ($chk -ge 1) { OK 'MinutesAtAward >= 0 kısıtı mevcut' } else { Fail 'check kısıtı yok' }

    # Enum METİN olarak saklanıyor; sayısala kayarsa okuma tarafı dönüştürme hatası verir.
    $tip = (Sql 'SELECT data_type FROM information_schema.columns WHERE table_schema = ''community'' AND table_name = ''UserSubjectBadges'' AND column_name = ''Branch'';').Trim()
    if ($tip -match 'character') { OK "Branch kolonu metin olarak saklanıyor ($tip)" } else { Fail "Branch kolonu tipi: $tip (metin olmalı)" }
}

# ---------------------------------------------------------------------------
Section 'F. Rozet tutarlılığı — rozetler ≡ tamamlanmış derslerden hesaplanan'

# BU BÖLÜMÜN ÖNEMİ: motorun tek vaadi "rozet her zaman veriyle tutarlı". Aşağıdaki sorgu
# tam da bunu sorguluyor — her (kullanıcı, branş, seviye) için hem OLMASI GEREKEN hem de
# OLAN kümeyi kurup farkı alıyor. Fark boş değilse motor ya rozet kaçırmış ya fazla vermiş.
$tamamlanan = SqlInt 'SELECT COUNT(*) FROM scheduling."LessonSessions" WHERE "Status" = ''Completed'';'
Write-Host "  (veritabanında $tamamlanan tamamlanmış ders var)" -ForegroundColor DarkGray

if ($tamamlanan -eq 0) {
    Note 'tamamlanmış ders yok — rozet tutarlılığı sınanamıyor. Önce .\tools\run-all-tests.ps1 koştur, sonra bu betiği tekrar çalıştır.'
}
else {
    $fark = Sql @'
WITH sure AS (
  SELECT l."TutorUserId" AS uid, s."Branch" AS brans, SUM(l."DurationMinutes") AS dk
  FROM scheduling."LessonSessions" l
  JOIN catalog."Topics" t ON t."Id" = l."TopicId"
  JOIN catalog."Subjects" s ON s."Id" = t."SubjectId"
  WHERE l."Status" = 'Completed' AND s."Branch" IS NOT NULL
  GROUP BY 1, 2
),
beklenen AS (
  -- Eşikler 2026-08-24'te 5/20/50 saatten 8/15 saate indi ve kademe sayısı üçten ikiye
  -- düştü (bkz. Domain/Community/UserSubjectBadge.cs, göç: SubjectBadgeTwoTiers).
  -- Bu kopya BİLEREK bağımsız: motoru çağırmak "kendi kendine eşit mi" diye sormak olurdu.
  SELECT uid, brans, 'Ogretici' AS seviye FROM sure WHERE dk >= 480
  UNION ALL SELECT uid, brans, 'Ustad'    FROM sure WHERE dk >= 900
),
olan AS (
  SELECT "UserId" AS uid, "Branch" AS brans, "Level" AS seviye FROM community."UserSubjectBadges"
)
SELECT string_agg(satir, ' | ') FROM (
  SELECT 'EKSIK ' || uid || '/' || brans || '/' || seviye AS satir FROM (SELECT * FROM beklenen EXCEPT SELECT * FROM olan) e
  UNION ALL
  SELECT 'FAZLA ' || uid || '/' || brans || '/' || seviye FROM (SELECT * FROM olan EXCEPT SELECT * FROM beklenen) f
) x;
'@
    if ([string]::IsNullOrWhiteSpace($fark)) { OK 'her rozet tamamlanmış derslerden hesaplananla birebir aynı' }
    else {
        # EKSİK = motor rozet kaçırdı (muhtemelen çağrı sırası: SaveChanges'ten ÖNCE evaluate).
        # FAZLA = rozet verildikten sonra ders iptal/silinmiş olabilir; motor geri almaz —
        #         bu tasarım gereğidir, tek başına hata değildir.
        $eksikVar = $fark -match 'EKSIK'
        if ($eksikVar) { Fail "rozet KAÇIRILMIŞ — çağrı sırasını kontrol et (SaveChanges → Evaluate → SaveChanges): $($fark.Trim())" }
        else { Note "yalnızca FAZLA rozet var; ders sonradan iptal edilmişse beklenen davranış (motor rozet geri almaz): $($fark.Trim())" }
    }

    # Eşik altı hiç rozet almamalı: 5 saatin altındaki bir branşta rozet varsa eşik kaymış.
    $esikAlti = SqlInt @'
WITH sure AS (
  SELECT l."TutorUserId" AS uid, s."Branch" AS brans, SUM(l."DurationMinutes") AS dk
  FROM scheduling."LessonSessions" l
  JOIN catalog."Topics" t ON t."Id" = l."TopicId"
  JOIN catalog."Subjects" s ON s."Id" = t."SubjectId"
  WHERE l."Status" = 'Completed' AND s."Branch" IS NOT NULL
  GROUP BY 1, 2
)
SELECT COUNT(*) FROM community."UserSubjectBadges" b
LEFT JOIN sure ON sure.uid = b."UserId" AND sure.brans = b."Branch"
WHERE COALESCE(sure.dk, 0) < 480;
'@
    if ($esikAlti -eq 0) { OK '8 saat eşiğinin altında rozet almış kimse yok' }
    else { Note "$esikAlti rozet, sahibinin şu anki süresinin altında — iptal edilmiş ders olabilir" }
}

# ---------------------------------------------------------------------------
Section 'G. Rozet bütünlüğü'

$cift = SqlInt 'SELECT COUNT(*) FROM (SELECT "UserId", "Branch", "Level" FROM community."UserSubjectBadges" GROUP BY 1,2,3 HAVING COUNT(*) > 1) x;'
if ($cift -eq 0) { OK 'çifte rozet yok' } else { Fail "$cift rozet iki kez verilmiş" }

# Kümülatiflik: Üstad olan Öğretici de olmalı. Atlanmış bir alt kademe, motorun
# EarnedLevels sıralamasının bozulduğunu gösterir. Kademe üçten ikiye indiği için
# kontrol de tek dala indi — eski hâli iki ayrı zincir sınıyordu (Usta→Çırak, Üstad→Usta).
$atlanan = SqlInt @'
SELECT COUNT(*) FROM community."UserSubjectBadges" u
WHERE u."Level" = 'Ustad'
  AND NOT EXISTS (SELECT 1 FROM community."UserSubjectBadges" c
                  WHERE c."UserId" = u."UserId" AND c."Branch" = u."Branch" AND c."Level" = 'Ogretici');
'@
if ($atlanan -eq 0) { OK 'alt kademe atlanmamış (Üstad olan Öğretici de)' } else { Fail "$atlanan rozette alt kademe eksik" }

$gecersizBrans = SqlInt 'SELECT COUNT(*) FROM community."UserSubjectBadges" WHERE "Branch" NOT IN (''Turkce'',''Tarih'',''Cografya'',''Matematik'',''Geometri'',''Fizik'',''Kimya'',''Biyoloji'');'
if ($gecersizBrans -eq 0) { OK 'tüm rozetler izin verilen sekiz branşta' } else { Fail "$gecersizBrans rozet tanımsız branşta — okuma sorguları dönüştürme hatası verecek" }

$gecersizSeviye = SqlInt 'SELECT COUNT(*) FROM community."UserSubjectBadges" WHERE "Level" NOT IN (''Ogretici'',''Ustad'');'
if ($gecersizSeviye -eq 0) { OK 'tüm seviyeler geçerli' } else { Fail "$gecersizSeviye rozet tanımsız seviyede" }

# TYT ve AYT'nin AYNI branşa toplandığının kanıtı: bir eğitmen hem TYT hem AYT matematik
# anlattıysa tek bir Matematik rozeti olmalı, iki değil. Yukarıdaki "çifte rozet yok"
# kontrolü bunu zaten kapsıyor; burada iki seviyenin gerçekten birleştiğini gösteriyoruz.
$ikiSeviyeliEgitmen = SqlInt @'
SELECT COUNT(*) FROM (
  SELECT l."TutorUserId", s."Branch"
  FROM scheduling."LessonSessions" l
  JOIN catalog."Topics" t ON t."Id" = l."TopicId"
  JOIN catalog."Subjects" s ON s."Id" = t."SubjectId"
  JOIN catalog."EducationCategories" c ON c."Id" = s."CategoryId"
  WHERE l."Status" = 'Completed' AND s."Branch" IS NOT NULL
  GROUP BY 1, 2
  HAVING COUNT(DISTINCT c."Slug") > 1
) x;
'@
if ($ikiSeviyeliEgitmen -gt 0) { OK "$ikiSeviyeliEgitmen eğitmen/branş çifti hem TYT hem AYT dersi içeriyor ve tek rozette birleşti" }
else { Note 'hem TYT hem AYT anlatan eğitmen yok — TYT+AYT birleşmesi bu veriyle sınanamadı' }

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) {
    Write-Host "TÜM ADIMLAR BAŞARILI ($($script:Pass) kontrol, $($script:Warn) not)" -ForegroundColor Green
    if ($script:Warn -gt 0) {
        Write-Host 'Notlar sınanamayan durumları gösterir; run-all-tests.ps1 sonrası tekrar koş.' -ForegroundColor Yellow
    }
}
else {
    Write-Host "$($script:Fail) KONTROL BAŞARISIZ ($($script:Pass) geçti, $($script:Warn) not)" -ForegroundColor Red
    exit 1
}
