using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ToplulukPuanOdulu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommunityRewardedCredits",
                schema: "identity",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
              GERİ ALMA KORUMASI — burada risk "veri kaybı" değil, PARA BASMA.

              Bu kolon, kullanıcıya topluluk katkısı için BUGÜNE KADAR ödenmiş puanı
              tutuyor ve ödül işi her turda "hak edilen − ödenmiş" farkını basıyor.
              Kolon düşerse ödenmiş tutar 0'a döner; işin bir sonraki turu aynı oyları
              YENİDEN ödüllendirir ve herkesin puanı ikiye katlanır.

              Sessiz de olur: kimse hata görmez, yalnızca bakiyeler şişer ve seviyeler
              hak edilmeden yükselir. Defterde iki ayrı CommunityReward hareketi kalır
              ama ikisi de "geçerli" görünür.

              Etiketli dolar tırnağı ($koruma$) bilinçli — bkz. KayitOnayiKaydi göçü.
            */
            migrationBuilder.Sql(@"
DO $koruma$
DECLARE
    odenmis_kullanici bigint;
    odenmis_toplam bigint;
BEGIN
    SELECT count(*), COALESCE(sum(""CommunityRewardedCredits""), 0)
    INTO odenmis_kullanici, odenmis_toplam
    FROM identity.""Users""
    WHERE ""CommunityRewardedCredits"" > 0;

    IF odenmis_kullanici > 0 THEN
        RAISE EXCEPTION
            'Geri alma durduruldu: % kullaniciya toplam % puan odenmis. Bu kolon dusurulurse odul isi ayni oylari YENIDEN oduller ve puan ikiye katlanir. Once bu veriyi disari aktarin ve odul isini durdurun.',
            odenmis_kullanici, odenmis_toplam;
    END IF;
END
$koruma$;");

            migrationBuilder.DropColumn(
                name: "CommunityRewardedCredits",
                schema: "identity",
                table: "Users");
        }
    }
}
