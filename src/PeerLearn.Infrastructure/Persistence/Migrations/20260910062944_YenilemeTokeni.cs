using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Yenileme token'ı tablosu + "her yerden çıkış" damgası.
    /// </summary>
    /// <remarks>
    /// ─── VERİ ONARIMI YOK — VE BU BİLİNÇLİ ───────────────────────────────────────
    /// CLAUDE.md "veri onarımı şemadan ÖNCE gelir" diyor; bu, HER göçte onarım SQL'i
    /// olması gerektiği anlamına gelmiyor. Bu göç saf EKLEME yapıyor: yeni bir tablo ve
    /// yeni bir nullable kolon. Onarılacak bozuk veri yok, çünkü dokunulan hiçbir mevcut
    /// satır yok.
    ///
    /// <c>Users.TokensValidFromUtc</c> mevcut satırlarda NULL kalıyor ve bu DOĞRU anlam
    /// taşıyor: "bu hesap için hiç toplu çıkış yapılmadı". Geriye doldurulsaydı (ör.
    /// göç anı yazılsaydı) üretimdeki bütün oturumlar sebepsiz düşerdi.
    ///
    /// ─── DOWN'DA RAISE EXCEPTION KORUMASI YOK — VE BU DA BİLİNÇLİ ────────────────
    /// Forum ve kayıt onayı göçleri, veri varken geri alınmayı REDDEDİYOR; çünkü orada
    /// kaybedilen şey geri getirilemez (kullanıcı içeriği, yasal onay kaydı). Burada
    /// kaybedilen şey token'lar — tamamen yeniden üretilebilir veri. Geri alımın bedeli
    /// yalnızca şu: herkesin bir kez daha giriş yapması.
    ///
    /// Bedeli olan tek şey damga kolonu: geri alınırsa "şu kullanıcının oturumlarını
    /// düşürmüştük" bilgisi kaybolur ve o kullanıcıların eski erişim token'ları (ömürleri
    /// dolmadıysa, en fazla 120 dakika) yeniden geçerli olur. Kısa ve sınırlı; bir
    /// RAISE EXCEPTION ile geri alımı imkânsız kılmayı hak etmiyor.
    ///
    /// ─── INDEX'LERİN İKİSİ DE BİLEREK FARKLI ────────────────────────────────────
    /// • <c>IX_RefreshTokens_TokenHash</c> FİLTRESİZ ve tekil. Kısmi yapılıp iptal
    ///   edilmiş satırlar dışarıda bırakılsaydı, YENİDEN KULLANIM TESPİTİ çalışmazdı:
    ///   o mekanizma tam olarak "iptal edilmiş bir token yeniden sunuldu mu" sorusuna
    ///   dayanıyor ve satırı bulabilmeyi gerektiriyor. Hırsızlık sessizce "geçersiz
    ///   token" olarak görünürdü.
    /// • <c>IX_RefreshTokens_AktifKullanici</c> kısmi (<c>"RevokedAtUtc" IS NULL</c>).
    ///   Süre koşulu (<c>ExpiresAtUtc > now()</c>) filtreye KONULAMAZ — <c>now()</c>
    ///   immutable değil, PostgreSQL kısmi index'te reddeder. Süre kontrolü uygulama
    ///   katmanında; sorgunun WHERE'i filtreyi birebir tekrarlamazsa index sessizce
    ///   devreden çıkar.
    /// </remarks>
    public partial class YenilemeTokeni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TokensValidFromUtc",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceHwidHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.CheckConstraint("CK_RefreshTokens_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_AktifKullanici",
                schema: "identity",
                table: "RefreshTokens",
                columns: new[] { "UserId", "ExpiresAtUtc" },
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "identity",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "TokensValidFromUtc",
                schema: "identity",
                table: "Users");
        }
    }
}
