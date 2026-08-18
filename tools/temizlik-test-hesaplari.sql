-- Test/bench hesaplarinin ve tum verilerinin silinmesi.
-- Korunan: demo.dev (5) + gmail.com (1) = 6 hesap.
-- Silme sonrasi ayni dort degismez sinanir; tutmazsa transaction geri alinir.
\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE s_user ON COMMIT DROP AS
  SELECT "Id" FROM identity."Users" WHERE "Email" LIKE '%@test.dev' OR "Email" LIKE '%@bench.dev';
CREATE INDEX ON s_user ("Id");

CREATE TEMP TABLE s_cuzdan ON COMMIT DROP AS
  SELECT "Id" FROM economy."Wallets" WHERE "UserId" IN (SELECT "Id" FROM s_user);
CREATE INDEX ON s_cuzdan ("Id");

-- Dersin TARAFLARINDAN biri silinen hesapsa ders de gitmeli (FK RESTRICT).
-- Bu, demo eğitmenin test öğrenciyle yaptığı 200 dersi de kapsar — öğrencisi olmayan
-- bir ders kaydı tutulamaz.
CREATE TEMP TABLE s_ders ON COMMIT DROP AS
  SELECT "Id" FROM scheduling."LessonSessions"
  WHERE "TutorUserId" IN (SELECT "Id" FROM s_user) OR "StudentUserId" IN (SELECT "Id" FROM s_user);
CREATE INDEX ON s_ders ("Id");

CREATE TEMP TABLE s_eslesme ON COMMIT DROP AS
  SELECT "Id" FROM matchmaking."Matches"
  WHERE "InitiatorUserId" IN (SELECT "Id" FROM s_user) OR "ResponderUserId" IN (SELECT "Id" FROM s_user);
CREATE INDEX ON s_eslesme ("Id");

CREATE TEMP TABLE s_konusma ON COMMIT DROP AS
  SELECT "Id" FROM comms."Conversations" WHERE "MatchId" IN (SELECT "Id" FROM s_eslesme);
CREATE INDEX ON s_konusma ("Id");

CREATE TEMP TABLE s_hareket ON COMMIT DROP AS
  SELECT "Id" FROM economy."CreditTransactions"
  WHERE "WalletId" IN (SELECT "Id" FROM s_cuzdan) OR "RelatedSessionId" IN (SELECT "Id" FROM s_ders);
CREATE INDEX ON s_hareket ("Id");

CREATE TEMP TABLE s_lot ON COMMIT DROP AS
  SELECT "Id" FROM economy."CreditLots"
  WHERE "WalletId" IN (SELECT "Id" FROM s_cuzdan) OR "SourceSessionId" IN (SELECT "Id" FROM s_ders);
CREATE INDEX ON s_lot ("Id");

CREATE TEMP TABLE s_hold ON COMMIT DROP AS
  SELECT "Id" FROM economy."CreditHolds"
  WHERE "WalletId" IN (SELECT "Id" FROM s_cuzdan) OR "SessionId" IN (SELECT "Id" FROM s_ders);
CREATE INDEX ON s_hold ("Id");

CREATE TEMP TABLE s_degerlendirme ON COMMIT DROP AS
  SELECT "Id" FROM scheduling."SessionReviews"
  WHERE "SessionId" IN (SELECT "Id" FROM s_ders)
     OR "ReviewerUserId" IN (SELECT "Id" FROM s_user)
     OR "RevieweeUserId" IN (SELECT "Id" FROM s_user);
CREATE INDEX ON s_degerlendirme ("Id");

CREATE TEMP TABLE s_tuketim ON COMMIT DROP AS
  SELECT "Id", "CreditLotId", "Amount", "IsReversal" FROM economy."CreditLotConsumptions"
  WHERE "CreditTransactionId" IN (SELECT "Id" FROM s_hareket)
     OR "CreditHoldId"        IN (SELECT "Id" FROM s_hold)
     OR "CreditLotId"         IN (SELECT "Id" FROM s_lot);

-- Hayatta kalan lotlara iade (bkz. onceki temizlik: atlanirsa kredi buharlasir).
UPDATE economy."CreditLots" l
SET "RemainingAmount" = l."RemainingAmount" + x.iade
FROM (SELECT "CreditLotId" AS lot,
             SUM(CASE WHEN "IsReversal" THEN -"Amount" ELSE "Amount" END) AS iade
      FROM s_tuketim GROUP BY "CreditLotId") x
WHERE l."Id" = x.lot AND l."Id" NOT IN (SELECT "Id" FROM s_lot);

-- --- Ekonomi ---
DELETE FROM economy."CreditLotConsumptions" WHERE "Id" IN (SELECT "Id" FROM s_tuketim);
DELETE FROM economy."CreditTransactions"    WHERE "Id" IN (SELECT "Id" FROM s_hareket);
DELETE FROM economy."CreditLots"            WHERE "Id" IN (SELECT "Id" FROM s_lot);
DELETE FROM economy."CreditHolds"           WHERE "Id" IN (SELECT "Id" FROM s_hold);

-- --- Ders ve cevresi ---
DELETE FROM moderation."Disputes"          WHERE "SessionId" IN (SELECT "Id" FROM s_ders)
                                              OR "RaisedByUserId" IN (SELECT "Id" FROM s_user);
DELETE FROM scheduling."SessionReviewTags" WHERE "ReviewId" IN (SELECT "Id" FROM s_degerlendirme);
DELETE FROM scheduling."SessionReviews"    WHERE "Id" IN (SELECT "Id" FROM s_degerlendirme);
DELETE FROM scheduling."SessionProofs"     WHERE "SessionId" IN (SELECT "Id" FROM s_ders)
                                              OR "UploadedByUserId" IN (SELECT "Id" FROM s_user);
DELETE FROM scheduling."LessonSessions"    WHERE "Id" IN (SELECT "Id" FROM s_ders);

-- --- Iletisim ve eslesme ---
DELETE FROM comms."Messages"      WHERE "ConversationId" IN (SELECT "Id" FROM s_konusma)
                                     OR "SenderUserId" IN (SELECT "Id" FROM s_user);
DELETE FROM comms."Conversations" WHERE "Id" IN (SELECT "Id" FROM s_konusma);
DELETE FROM matchmaking."Matches" WHERE "Id" IN (SELECT "Id" FROM s_eslesme);
DELETE FROM matchmaking."PortfolioEntries" WHERE "UserId" IN (SELECT "Id" FROM s_user);

-- --- Kimlik / moderasyon / topluluk ---
DELETE FROM economy."Wallets"                  WHERE "Id" IN (SELECT "Id" FROM s_cuzdan);
DELETE FROM moderation."UserSanctions"         WHERE "UserId" IN (SELECT "Id" FROM s_user);
DELETE FROM moderation."AdminActionLogs"       WHERE "ActorUserId" IN (SELECT "Id" FROM s_user);
DELETE FROM identity."UserDevices"             WHERE "UserId" IN (SELECT "Id" FROM s_user);
DELETE FROM identity."UserPreferences"         WHERE "UserId" IN (SELECT "Id" FROM s_user);
DELETE FROM identity."TeacherCandidateProfiles" WHERE "UserId" IN (SELECT "Id" FROM s_user);
DELETE FROM community."UserBadges"             WHERE "UserId" IN (SELECT "Id" FROM s_user);

/*
  HWID YASAKLARI.

  HwidBans'in Users'a FK'si YOKTUR ve bu bilinclidir: yasak, hesap silinse de yasamali,
  yoksa "hesabi sil, yeniden kaydol" ban evasion'a acik kapi olurdu. Bu yuzden asagidaki
  silme FK zorunlulugu DEGIL, ayri bir karardir.

  Karar gerekcesi: 71 yasagin 71'i de test hesaplarina ait ve yasakli hash'lerin tamami
  silinen test cihazlarina ait. Hicbir demo/gercek hesabin cihazi banli degil (olculdu).
  Birakilsalardi, var olmayan kullanicilari isaret eden ve var olmayan cihazlari engelleyen
  63 aktif yasak kalirdi. GERCEK bir yasak olsaydi burasi degistirilmeliydi.
*/
DELETE FROM moderation."HwidBans" WHERE "RelatedUserId" IN (SELECT "Id" FROM s_user);

DELETE FROM identity."Users" WHERE "Id" IN (SELECT "Id" FROM s_user);

-- --- Defteri yeniden turet ---
UPDATE economy."Wallets" w SET
  "AvailableBalance" = COALESCE((SELECT SUM(l."RemainingAmount") FROM economy."CreditLots" l WHERE l."WalletId" = w."Id"), 0),
  "LockedBalance"    = COALESCE((SELECT SUM(h."Amount") FROM economy."CreditHolds" h WHERE h."WalletId" = w."Id" AND h."Status" = 'Active'), 0),
  "UpdatedAtUtc"     = now();

UPDATE identity."Users" u SET "TotalEarnedCredits" = COALESCE((
  SELECT SUM(t."Amount") FROM economy."CreditTransactions" t
  JOIN economy."Wallets" w ON w."Id" = t."WalletId"
  WHERE w."UserId" = u."Id" AND t."Type" = 'LessonEarning'), 0);

-- --- DEGISMEZ SINAVI ---
DO $$
DECLARE c bigint; d bigint; n int;
BEGIN
  SELECT COALESCE(SUM("AvailableBalance"+"LockedBalance"),0) INTO c FROM economy."Wallets";
  SELECT COALESCE(SUM("Amount"),0) INTO d FROM economy."CreditTransactions";
  IF c <> d THEN RAISE EXCEPTION 'IPTAL: global defter % <> %', c, d; END IF;

  SELECT COUNT(*) INTO n FROM (
    SELECT w."Id" FROM economy."Wallets" w LEFT JOIN economy."CreditLots" l ON l."WalletId"=w."Id"
    GROUP BY w."Id", w."AvailableBalance"
    HAVING w."AvailableBalance" <> COALESCE(SUM(l."RemainingAmount"),0)) z;
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % cuzdanda available <> SUM(lot)', n; END IF;

  SELECT COUNT(*) INTO n FROM (
    SELECT w."Id" FROM economy."Wallets" w
    LEFT JOIN economy."CreditHolds" h ON h."WalletId"=w."Id" AND h."Status"='Active'
    GROUP BY w."Id", w."LockedBalance"
    HAVING w."LockedBalance" <> COALESCE(SUM(h."Amount"),0)) z;
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % cuzdanda locked <> SUM(aktif hold)', n; END IF;

  SELECT COUNT(*) INTO n FROM (
    SELECT u."Id" FROM identity."Users" u
    LEFT JOIN economy."Wallets" w ON w."UserId"=u."Id"
    LEFT JOIN economy."CreditTransactions" t ON t."WalletId"=w."Id" AND t."Type"='LessonEarning'
    GROUP BY u."Id", u."TotalEarnedCredits"
    HAVING u."TotalEarnedCredits" <> COALESCE(SUM(t."Amount"),0)) z;
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % kullanicida sayac <> SUM(LessonEarning)', n; END IF;

  -- Oksuz kayit taramasi
  SELECT COUNT(*) INTO n FROM economy."Wallets" w
    WHERE NOT EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id"=w."UserId");
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % oksuz cuzdan', n; END IF;

  SELECT COUNT(*) INTO n FROM scheduling."LessonSessions" s
    WHERE NOT EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id"=s."TutorUserId")
       OR NOT EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id"=s."StudentUserId");
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % oksuz ders', n; END IF;

  SELECT COUNT(*) INTO n FROM matchmaking."PortfolioEntries" p
    WHERE NOT EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id"=p."UserId");
  IF n > 0 THEN RAISE EXCEPTION 'IPTAL: % oksuz portfoy', n; END IF;
END $$;

COMMIT;
