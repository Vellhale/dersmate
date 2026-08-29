using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Kayıt onayının kaydı: kabul edilen sözleşme sürümü + zaman damgaları.
    /// </summary>
    /// <remarks>
    /// VERİ ONARIMI YOK ve olmamalı. Üç kolon da nullable ve mevcut satırlarda BOŞ
    /// kalıyor: bu kolonlar eklenmeden önce açılmış hesaplar gerçekten onay vermedi.
    /// Geriye doldurmak (ör. CreatedAtUtc'yi onay anı saymak) uydurma bir kanıt üretirdi
    /// ve denetimde "kaydımız var" denen şey sahte olurdu. Boş kalması DOĞRU cevaptır.
    /// </remarks>
    public partial class KayitOnayiKaydi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AgeConfirmedAtUtc",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAtUtc",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                schema: "identity",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
              GERİ ALMA KORUMASI.

              Bu üç kolonu düşürmek, kullanıcıların sözleşmeyi kabul ettiğine dair TEK
              kanıtı siler ve geri getirilemez — yedekten dönmek dışında. Onay kaydı
              hukuki bir belge; "geri alınabilir şema değişikliği" gibi davranılamaz.

              Bu yüzden geri alma, kayıtlı onay VARSA duruyor. Kimse onay vermemişse
              (ör. göç yeni uygulandı, henüz kayıt olmadı) sorunsuz geri alınabiliyor.

              Etiketli dolar tırnağı ($koruma$) bilinçli: gövdedeki tek tırnaklar ve
              olası $$ dizileri, etiketsiz dolar tırnağında bloğu erkenden kapatıp
              "syntax error at or near" üretiyor (bu projede bir kez ısırdı).
            */
            migrationBuilder.Sql(@"
DO $koruma$
DECLARE
    onayli_sayisi bigint;
BEGIN
    SELECT count(*) INTO onayli_sayisi
    FROM identity.""Users""
    WHERE ""TermsAcceptedAtUtc"" IS NOT NULL;

    IF onayli_sayisi > 0 THEN
        RAISE EXCEPTION
            'Geri alma durduruldu: % kullanicinin kayit onayi silinecekti. Onay kaydi hukuki bir belgedir; kolonlari dusurmeden once bu veriyi disari aktarin.',
            onayli_sayisi;
    END IF;
END
$koruma$;");

            migrationBuilder.DropColumn(
                name: "AgeConfirmedAtUtc",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAtUtc",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                schema: "identity",
                table: "Users");
        }
    }
}
