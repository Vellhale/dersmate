-- Test koşumlarının bıraktığı KATALOG artıklarını siler (kategori / ders / konu).
--
-- NEDEN AYRI BETİK: temizlik-test-hesaplari.sql yalnızca HESAPLARI ve onlara bağlı
-- kayıtları siliyor. Katalog satırlarının sahibi yok — bir kullanıcıya bağlı değiller,
-- bu yüzden hesaplar silinince ardlarında ÖKSÜZ olarak kalıyorlar. 2026-08-17'de tam
-- olarak bu oldu: hesaplar temizlendi, Keşfet filtresi hâlâ 18 "Zqx…" kategorisi
-- gösteriyordu.
--
-- İKİ ÖLÇÜT:
--   1. Adı 'Zqx' ile başlayan kategoriler (e2e-discovery'nin ürettiği ağaç)
--   2. Adında 6+ basamaklı sayı geçen konular ('E2E Organik 1786…', 'Basim 1786…')
--      — koşum damgası. Gerçek müfredatta böyle bir ad yok; kalacak listeyi görmek için
--      betiğin sonundaki çıktıya bakın.
--
-- GÜVENLİK: bağlı kaydı (portföy/eşleşme/ders) olan bir konu VARSA hiçbir şey silinmez.
-- O durumda silme hesap temizliğinden ÖNCE yapılmalıdır; sıra bozulmuşsa insan baksın.
\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE s_kat ON COMMIT DROP AS
  SELECT "Id" FROM catalog."EducationCategories" WHERE "Name" LIKE 'Zqx%';

CREATE TEMP TABLE s_ders ON COMMIT DROP AS
  SELECT "Id" FROM catalog."Subjects" WHERE "CategoryId" IN (SELECT "Id" FROM s_kat);

CREATE TEMP TABLE s_konu ON COMMIT DROP AS
  SELECT "Id" FROM catalog."Topics" WHERE "SubjectId" IN (SELECT "Id" FROM s_ders)
  UNION
  SELECT "Id" FROM catalog."Topics" WHERE "Name" ~ '[0-9]{6,}';

DO $$
DECLARE p int; m int; s int; a int;
BEGIN
  SELECT COUNT(*) INTO p FROM matchmaking."PortfolioEntries" WHERE "TopicId" IN (SELECT "Id" FROM s_konu);
  SELECT COUNT(*) INTO m FROM matchmaking."Matches"
    WHERE "RequestedTopicId" IN (SELECT "Id" FROM s_konu) OR "OfferedTopicId" IN (SELECT "Id" FROM s_konu);
  SELECT COUNT(*) INTO s FROM scheduling."LessonSessions" WHERE "TopicId" IN (SELECT "Id" FROM s_konu);
  SELECT COUNT(*) INTO a FROM catalog."Topics" WHERE "ParentTopicId" IN (SELECT "Id" FROM s_konu);

  IF p > 0 OR m > 0 OR s > 0 OR a > 0 THEN
    RAISE EXCEPTION 'IPTAL: cop konulara bagli kayit var (portfoy=%, eslesme=%, ders=%, alt konu=%). Once hesap temizligini calistirin.', p, m, s, a;
  END IF;
END $$;

DELETE FROM catalog."Topics"   WHERE "Id" IN (SELECT "Id" FROM s_konu);
DELETE FROM catalog."Subjects" WHERE "Id" IN (SELECT "Id" FROM s_ders);
-- Alt kategoriler önce: EducationCategories kendine FK ile bağlı (ParentCategoryId).
DELETE FROM catalog."EducationCategories" WHERE "Id" IN (SELECT "Id" FROM s_kat) AND "ParentCategoryId" IS NOT NULL;
DELETE FROM catalog."EducationCategories" WHERE "Id" IN (SELECT "Id" FROM s_kat);

DO $$
DECLARE n int;
BEGIN
  SELECT COUNT(*) INTO n FROM matchmaking."PortfolioEntries" p
    WHERE NOT EXISTS (SELECT 1 FROM catalog."Topics" t WHERE t."Id" = p."TopicId");
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % oksuz portfoy satiri olustu', n; END IF;

  SELECT COUNT(*) INTO n FROM catalog."Subjects" s
    WHERE NOT EXISTS (SELECT 1 FROM catalog."EducationCategories" c WHERE c."Id" = s."CategoryId");
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % oksuz ders satiri olustu', n; END IF;
END $$;

COMMIT;

\echo === KATALOG TEMIZLIGI SONRASI ===
SELECT 'kalan kok kategori : ' || COALESCE(string_agg("Name", ', ' ORDER BY "Name"), '(bos)')
  FROM catalog."EducationCategories" WHERE "ParentCategoryId" IS NULL;
SELECT 'kalan konu sayisi  : ' || COUNT(*) FROM catalog."Topics";
SELECT 'kalan konular      : ' || COALESCE(string_agg(s."Name" || '/' || t."Name", ', ' ORDER BY s."Name", t."Name"), '(bos)')
  FROM catalog."Topics" t JOIN catalog."Subjects" s ON s."Id" = t."SubjectId";
