using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ÜNİVERSİTE AĞI — KONUSUZ EŞLEŞME (2026-08-24).
    ///
    /// Matches.RequestedTopicId artık null olabilir. NULL = ders isteği değil, üniversite
    /// ağı tanışma isteği; kabul edilince yalnızca sohbet açılır (bkz. Match.cs).
    ///
    /// VERİ ONARIMI GEREKMİYOR ve bu kasıtlı bir tespit, atlanmış bir adım değil: kolon
    /// NOT NULL'dan NULL'a GENİŞLİYOR. Mevcut hiçbir satır geçersiz hâle gelmiyor, her
    /// eski eşleşme konusunu koruyor. Göç yine de iki değişmezi sınıyor — biri yukarı,
    /// biri aşağı yönde.
    /// </summary>
    public partial class UniversiteAgiKonusuzEslesme : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
              DEĞİŞMEZ 1 — yeni tekillik indeksi çakışmadan kurulabilmeli.

              UX_Matches_PendingKonusuz, (Initiator, Responder) çiftinde konusuz ve
              bekleyen TEK bir istek olmasını zorluyor. Kolon bugüne kadar NOT NULL
              olduğu için böyle bir satır olamaz; yani bu kontrol normalde hiçbir şey
              bulmaz. Yine de duruyor çünkü göç ileride kısmen uygulanmış bir veritabanı
              üzerinde yeniden koşabilir ve o durumda CREATE UNIQUE INDEX, sebebi
              okunmayan bir hata verirdi. Burada patlarsa sebebi cümleyle yazılı olur.
            */
            migrationBuilder.Sql("""
                DO $$
                DECLARE cakisan int;
                BEGIN
                    SELECT count(*) INTO cakisan FROM (
                        SELECT "InitiatorUserId", "ResponderUserId"
                        FROM matchmaking."Matches"
                        WHERE "Status" = 'Pending' AND "RequestedTopicId" IS NULL
                        GROUP BY "InitiatorUserId", "ResponderUserId"
                        HAVING count(*) > 1
                    ) t;

                    IF cakisan > 0 THEN
                        RAISE EXCEPTION
                            'Gec iptal: % cift, konusuz ve bekleyen birden fazla istek tasiyor. UX_Matches_PendingKonusuz kurulamaz; once fazlalar temizlenmeli.', cakisan;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedTopicId",
                schema: "matchmaking",
                table: "Matches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "UX_Matches_PendingKonusuz",
                schema: "matchmaking",
                table: "Matches",
                columns: new[] { "InitiatorUserId", "ResponderUserId" },
                unique: true,
                filter: "\"Status\" = 'Pending' AND \"RequestedTopicId\" IS NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Matches_PendingKonusuz",
                schema: "matchmaking",
                table: "Matches");

            /*
              DEĞİŞMEZ 2 — GERİ ALMA, VERİ VARKEN SESSİZCE İLERLEMEZ.

              EF'in ürettiği varsayılan Down, kolonu NOT NULL'a çevirirken NULL satırlara
              `defaultValue: Guid.Empty` yazıyordu. Bu iki şekilde kötü:
                • 00000000-...-0000 diye bir Topic yok; FK_Matches_Topics_RequestedTopicId
                  (Restrict) bunu reddeder ve göç yarıda, okunmayan bir hatayla düşer.
                • Reddetmeseydi daha kötü olurdu: konusuz eşleşmeler var olmayan bir konuya
                  işaret eden BOZUK satırlara dönüşür, hata çok sonra başka bir yerde patlardı.

              Bu yüzden geri alma, veri varken bilinçli bir karar İSTİYOR: kayıtlar
              silinecek mi, yoksa bir konuya mı taşınacak? Bunu göç kendi başına seçemez.
              Sıfır konusuz kayıt varsa geri alma sorunsuz ilerler.
            */
            migrationBuilder.Sql("""
                DO $$
                DECLARE konusuz int;
                BEGIN
                    SELECT count(*) INTO konusuz
                    FROM matchmaking."Matches" WHERE "RequestedTopicId" IS NULL;

                    IF konusuz > 0 THEN
                        RAISE EXCEPTION
                            'Geri alma iptal: % adet konusuz (universite agi) eslesme var. Kolon NOT NULL yapilirsa bu satirlar var olmayan bir konuya isaret eder. Once bu kayitlar silinmeli ya da bir konuya tasinmali.', konusuz;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedTopicId",
                schema: "matchmaking",
                table: "Matches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
