# PeerLearn — Modül 1: Gelişmiş filtreleme ve arama testi
#
# Kapsam: arama (büyük/küçük harf duyarsızlığı), kategori hiyerarşisi, seviye ve puan
# aralıkları, beş sıralama, sayfalama kararlılığı, önbellek ve yetki.
#
# YÖNTEM NOTU: testin kendi verisini üretmesi ŞART. Paylaşılan katalog konuları birikmiş
# e2e kullanıcılarıyla kirli; "kaç sonuç döndü" gibi bir beklenti oradan kurulamaz. Bu
# yüzden koşuma özel kategori/ders/konu ağacı ve kendi eğitmenleri kuruluyor, tüm
# doğrulamalar O ağaç üzerinde yapılıyor.

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

$script:Pass = 0
$script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Sql($query) {
    if (-not $PSQL) {
        # Docker yolu: sorgu STDIN den geçer. -c ile argüman olarak geçirmek,
        # identity."Users" gibi tırnaklı adlardaki tırnakları kabuğa yedirir.
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return ($out -join '').Trim()
    }
    $file = Join-Path $env:TEMP "pl-disc-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($file, $query, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $file } finally { Remove-Item $file -Force }
}

function Post($path, $body, $token) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $json = $body | ConvertTo-Json -Depth 6
    Invoke-RestMethod -Uri "$Api$path" -Method Post -Headers $headers `
        -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}

function Get_($path, $token) { Invoke-RestMethod -Uri "$Api$path" -Headers @{ Authorization = "Bearer $token" } }
function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }

function NewUser($prefix, $stamp) {
    $hwid = NewHwid
    $email = "$prefix$stamp@test.dev"
    $reg = Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix $stamp"; hwidHash = $hwid } $null
    Post '/api/auth/verify-email' @{ token = $reg.verificationToken } $null | Out-Null
    $login = Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    return [pscustomobject]@{ Email = $email; Token = $login.accessToken; UserId = $login.userId }
}

# Aramada tek bir ilana kadar daraltabilmek için benzersiz, tahmin edilemez bir etiket.
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$tag = "Zqx$stamp"

Write-Host "PeerLearn — Modül 1 (arama ve filtreleme) testi" -ForegroundColor White
Write-Host "API: $Api   etiket: $tag"

# ---------------------------------------------------------------------------
Section 'Hazırlık: koşuma özel katalog ağacı'

# Kök kategori -> alt kategori -> ders -> iki konu. Hiyerarşi testi bu ağaçta yapılır.
Sql @"
INSERT INTO catalog."EducationCategories" ("Id","ParentCategoryId","Name","Slug","SortOrder","IsActive","CreatedAtUtc")
VALUES (gen_random_uuid(), NULL, '$tag Kok', 'zqx-$stamp-kok', 900, TRUE, now());

INSERT INTO catalog."EducationCategories" ("Id","ParentCategoryId","Name","Slug","SortOrder","IsActive","CreatedAtUtc")
SELECT gen_random_uuid(), c."Id", '$tag Alt', 'zqx-$stamp-alt', 901, TRUE, now()
FROM catalog."EducationCategories" c WHERE c."Name" = '$tag Kok';

INSERT INTO catalog."Subjects" ("Id","CategoryId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT gen_random_uuid(), c."Id", '$tag Ders', 900, TRUE, now()
FROM catalog."EducationCategories" c WHERE c."Name" = '$tag Alt';

INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT gen_random_uuid(), s."Id", '$tag Konu', 900, TRUE, now()
FROM catalog."Subjects" s WHERE s."Name" = '$tag Ders';
"@ | Out-Null

$rootId = (Sql "SELECT ""Id"" FROM catalog.""EducationCategories"" WHERE ""Name"" = '$tag Kok';").Trim()
$childId = (Sql "SELECT ""Id"" FROM catalog.""EducationCategories"" WHERE ""Name"" = '$tag Alt';").Trim()
$topicId = (Sql "SELECT ""Id"" FROM catalog.""Topics"" WHERE ""Name"" = '$tag Konu';").Trim()
OK "kategori ağacı kuruldu (kök -> alt -> ders -> konu)"

# Üç eğitmen: farklı seviye ve farklı puanla, filtreleri ayırt edebilmek için.
$t1 = NewUser 'zqxa' $stamp   # seviye 5, puan 5.0
$t2 = NewUser 'zqxb' $stamp   # seviye 3, puan 3.0
$t3 = NewUser 'zqxc' $stamp   # seviye 1, puansız
$seeker = NewUser 'zqxs' $stamp
OK 'dört kullanıcı oluşturuldu (üç eğitmen, bir arayan)'

Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5 } $t1.Token | Out-Null
Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 3 } $t2.Token | Out-Null
Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 1 } $t3.Token | Out-Null

# Puanlar denormalize kolonda tutuluyor; testte doğrudan yazmak akışı kısaltır.
Sql "UPDATE identity.""Users"" SET ""AverageRating"" = 5.00, ""RatingCount"" = 10 WHERE ""Id"" = '$($t1.UserId)';" | Out-Null
Sql "UPDATE identity.""Users"" SET ""AverageRating"" = 3.00, ""RatingCount"" = 4  WHERE ""Id"" = '$($t2.UserId)';" | Out-Null
OK 'üç ilan açıldı (seviye 5/3/1, puan 5.0/3.0/yok)'

# Önbellek TTL'i 60 sn; koşuma özel etiket sayesinde bu anahtarlar hiç ısınmamış olur.
$q = "search=$tag"

# ---------------------------------------------------------------------------
Section 'A. Arama'

$all = Get_ "/api/discovery/offers?$q&pageSize=50" $seeker.Token
if ($all.totalCount -eq 3) { OK "etikete göre tam 3 ilan bulundu" } else { Fail "beklenen 3, gelen $($all.totalCount)" }

$upper = Get_ "/api/discovery/offers?search=$($tag.ToUpper())&pageSize=50" $seeker.Token
if ($upper.totalCount -eq 3) { OK 'arama büyük/küçük harf duyarsız' } else { Fail "büyük harfle $($upper.totalCount) geldi" }

$none = Get_ "/api/discovery/offers?search=$tag-yokboyle&pageSize=50" $seeker.Token
if ($none.totalCount -eq 0 -and $none.items.Count -eq 0) { OK 'eşleşmeyen arama boş döndü' } else { Fail "boş beklenirken $($none.totalCount)" }

# Eğitmen adıyla da aranabilmeli (konu adı kadar önemli bir giriş yolu).
$byName = Get_ "/api/discovery/offers?search=zqxa&pageSize=50" $seeker.Token
if ($byName.totalCount -ge 1) { OK 'eğitmen adıyla arama çalışıyor' } else { Fail 'eğitmen adıyla sonuç yok' }

# ---------------------------------------------------------------------------
Section 'B. Kategori hiyerarşisi'

$byRoot = Get_ "/api/discovery/offers?$q&categoryId=$rootId&pageSize=50" $seeker.Token
if ($byRoot.totalCount -eq 3) { OK 'kök kategori alt dalındaki ilanları kapsıyor' } else { Fail "kök: $($byRoot.totalCount)" }

$byChild = Get_ "/api/discovery/offers?$q&categoryId=$childId&pageSize=50" $seeker.Token
if ($byChild.totalCount -eq 3) { OK 'alt kategori doğrudan eşleşiyor' } else { Fail "alt: $($byChild.totalCount)" }

$unknown = Get_ "/api/discovery/offers?$q&categoryId=00000000-0000-0000-0000-000000000001&pageSize=50" $seeker.Token
if ($unknown.totalCount -eq 0) { OK 'bilinmeyen kategori 0 döndürüyor (tüm katalog DEĞİL)' }
else { Fail "bilinmeyen kategori $($unknown.totalCount) döndürdü — sessizce filtre atlanıyor" }

# ---------------------------------------------------------------------------
Section 'C. Seviye ve puan aralıkları'

$lvl5 = Get_ "/api/discovery/offers?$q&minLevel=5&pageSize=50" $seeker.Token
if ($lvl5.totalCount -eq 1) { OK 'minLevel=5 yalnızca en yüksek seviyeyi getirdi' } else { Fail "minLevel=5: $($lvl5.totalCount)" }

$lvl13 = Get_ "/api/discovery/offers?$q&minLevel=1&maxLevel=3&pageSize=50" $seeker.Token
if ($lvl13.totalCount -eq 2) { OK 'seviye aralığı (1-3) iki ilan getirdi' } else { Fail "1-3 aralığı: $($lvl13.totalCount)" }

$rate45 = Get_ "/api/discovery/offers?$q&minRating=4.5&pageSize=50" $seeker.Token
if ($rate45.totalCount -eq 1) { OK 'minRating=4.5 yalnızca 5.0 puanlıyı getirdi' } else { Fail "minRating: $($rate45.totalCount)" }

$rateMax = Get_ "/api/discovery/offers?$q&maxRating=3.5&pageSize=50" $seeker.Token
# Puansız eğitmen AverageRating=0 taşır, o da 3.5 altındadır.
if ($rateMax.totalCount -eq 2) { OK 'maxRating=3.5 iki ilan getirdi (puansız dahil)' } else { Fail "maxRating: $($rateMax.totalCount)" }

# ---------------------------------------------------------------------------
Section 'D. Sıralama'

$best = (Get_ "/api/discovery/offers?$q&sort=RatingDesc&pageSize=50" $seeker.Token).items[0]
if ($best.tutorUserId -eq $t1.UserId) { OK 'RatingDesc en yüksek puanlıyı başa aldı' } else { Fail "RatingDesc ilk: $($best.tutorDisplayName)" }

$worst = (Get_ "/api/discovery/offers?$q&sort=RatingAsc&pageSize=50" $seeker.Token).items[0]
# Puansızlar sona atılır; en düşük PUANLANMIŞ eğitmen başa gelmeli.
if ($worst.tutorUserId -eq $t2.UserId) { OK 'RatingAsc puansızları başa doldurmuyor' } else { Fail "RatingAsc ilk: $($worst.tutorDisplayName)" }

$popular = (Get_ "/api/discovery/offers?$q&sort=Popular&pageSize=50" $seeker.Token).items[0]
if ($popular.tutorUserId -eq $t1.UserId) { OK 'Popular en çok değerlendirileni başa aldı' } else { Fail "Popular ilk: $($popular.tutorDisplayName)" }

$relevance = (Get_ "/api/discovery/offers?$q&sort=Relevance&pageSize=50" $seeker.Token).items[0]
if ($relevance.selfAssessedLevel -eq 5) { OK 'Relevance en yüksek seviyeyi başa aldı' } else { Fail "Relevance ilk seviye: $($relevance.selfAssessedLevel)" }

$newest = Get_ "/api/discovery/offers?$q&sort=Newest&pageSize=50" $seeker.Token
if ($newest.totalCount -eq 3) { OK 'Newest çalışıyor' } else { Fail "Newest: $($newest.totalCount)" }

# ---------------------------------------------------------------------------
Section 'E. Sayfalama'

$p1 = Get_ "/api/discovery/offers?$q&pageSize=2&page=1" $seeker.Token
$p2 = Get_ "/api/discovery/offers?$q&pageSize=2&page=2" $seeker.Token

if ($p1.items.Count -eq 2 -and $p2.items.Count -eq 1) { OK 'sayfa boyutu uygulandı (2 + 1)' }
else { Fail "sayfalar: $($p1.items.Count) + $($p2.items.Count)" }

if ($p1.totalPages -eq 2) { OK "toplam sayfa doğru ($($p1.totalPages))" } else { Fail "totalPages: $($p1.totalPages)" }
if ($p1.hasNextPage -and -not $p2.hasNextPage) { OK 'hasNextPage doğru' } else { Fail 'hasNextPage yanlış' }

$ids = @($p1.items.offerId) + @($p2.items.offerId)
if (($ids | Select-Object -Unique).Count -eq 3) { OK 'sayfalar arası tekrar yok (kararlı sıralama)' }
else { Fail 'aynı ilan birden fazla sayfada' }

# ---------------------------------------------------------------------------
Section 'F. Kişiselleştirme ve yetki'

# Kendi ilanı listede görünmemeli: t1 kendi aramasında kendini bulmamalı.
$selfView = Get_ "/api/discovery/offers?$q&pageSize=50" $t1.Token
if (-not ($selfView.items.tutorUserId -contains $t1.UserId)) { OK 'kullanıcı kendi ilanını görmüyor' }
else { Fail 'kendi ilanı sonuçlarda' }

if ($selfView.items.Count -eq 2) { OK 'diğer ilanlar etkilenmedi (2 kaldı)' } else { Fail "kalan: $($selfView.items.Count)" }

try { Invoke-RestMethod -Uri "$Api/api/discovery/offers?$q" | Out-Null; Fail 'kimliksiz erişim açık' }
catch { OK "kimliksiz erişim reddedildi ($([int]$_.Exception.Response.StatusCode))" }

# ---------------------------------------------------------------------------
Section 'G. Önbellek'

# İlk istek DB'ye gider, ikincisi Redis'ten dönmeli. Süre ölçümü kırılgan olduğu için
# yalnızca SONUCUN AYNI kaldığı doğrulanıyor — önbellek doğruluğu bozmamalı.
$c1 = Get_ "/api/discovery/offers?$q&minLevel=3&sort=Popular&pageSize=10" $seeker.Token
$c2 = Get_ "/api/discovery/offers?$q&minLevel=3&sort=Popular&pageSize=10" $seeker.Token
if ($c1.totalCount -eq $c2.totalCount -and
    (($c1.items.offerId) -join ',') -eq (($c2.items.offerId) -join ',')) {
    OK 'önbellekli ikinci istek aynı sonucu verdi'
} else { Fail 'önbellek farklı sonuç döndürdü' }

# Katalog ucu da pill'leri besliyor; ağacın orada göründüğü doğrulanır.
$cats = Get_ '/api/catalog/categories' $seeker.Token
$root = $cats | Where-Object { $_.categoryId -eq $rootId }
$child = $cats | Where-Object { $_.categoryId -eq $childId }
if ($root -and -not $root.parentCategoryId) { OK 'kök kategori katalog ucunda parentsiz' } else { Fail 'kök kategori hatalı' }
if ($child -and $child.parentCategoryId -eq $rootId) { OK 'alt kategori doğru köke bağlı' } else { Fail 'alt kategori bağı hatalı' }

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) {
    Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green
} else {
    Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red
    exit 1
}
Write-Host "================================" -ForegroundColor White
