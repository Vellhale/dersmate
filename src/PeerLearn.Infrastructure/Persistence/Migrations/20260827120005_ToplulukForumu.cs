using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ToplulukForumu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CommunityCommentId",
                schema: "moderation",
                table: "Reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommunityPostId",
                schema: "moderation",
                table: "Reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Posts",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpvoteCount = table.Column<int>(type: "integer", nullable: false),
                    DownvoteCount = table.Column<int>(type: "integer", nullable: false),
                    CommentCount = table.Column<int>(type: "integer", nullable: false),
                    ReportCount = table.Column<int>(type: "integer", nullable: false),
                    ModeratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.CheckConstraint("CK_Posts_Counters", "\"UpvoteCount\" >= 0 AND \"DownvoteCount\" >= 0 AND \"CommentCount\" >= 0 AND \"ReportCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_Posts_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpvoteCount = table.Column<int>(type: "integer", nullable: false),
                    DownvoteCount = table.Column<int>(type: "integer", nullable: false),
                    ReportCount = table.Column<int>(type: "integer", nullable: false),
                    ModeratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.CheckConstraint("CK_Comments_Counters", "\"UpvoteCount\" >= 0 AND \"DownvoteCount\" >= 0 AND \"ReportCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_Comments_Posts_PostId",
                        column: x => x.PostId,
                        principalSchema: "community",
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Votes",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Value = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Votes", x => x.Id);
                    table.CheckConstraint("CK_Votes_TekHedef", "(\"PostId\" IS NOT NULL AND \"CommentId\" IS NULL) OR (\"PostId\" IS NULL AND \"CommentId\" IS NOT NULL)");
                    table.CheckConstraint("CK_Votes_Value", "\"Value\" IN (-1, 1)");
                    table.ForeignKey(
                        name: "FK_Votes_Comments_CommentId",
                        column: x => x.CommentId,
                        principalSchema: "community",
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Votes_Posts_PostId",
                        column: x => x.PostId,
                        principalSchema: "community",
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Votes_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_AuthorUserId",
                schema: "community",
                table: "Comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_GorunurGonderiTarih",
                schema: "community",
                table: "Comments",
                columns: new[] { "PostId", "CreatedAtUtc" },
                filter: "\"Status\" = 'Visible'");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_AuthorUserId_CreatedAtUtc",
                schema: "community",
                table: "Posts",
                columns: new[] { "AuthorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_GorunurEtiketTarih",
                schema: "community",
                table: "Posts",
                columns: new[] { "Tag", "CreatedAtUtc" },
                filter: "\"Status\" = 'Visible'");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_GorunurTarih",
                schema: "community",
                table: "Posts",
                column: "CreatedAtUtc",
                filter: "\"Status\" = 'Visible'");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_CommentId",
                schema: "community",
                table: "Votes",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_PostId",
                schema: "community",
                table: "Votes",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "UX_Votes_KullaniciGonderi",
                schema: "community",
                table: "Votes",
                columns: new[] { "UserId", "PostId" },
                unique: true,
                filter: "\"PostId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Votes_KullaniciYorum",
                schema: "community",
                table: "Votes",
                columns: new[] { "UserId", "CommentId" },
                unique: true,
                filter: "\"CommentId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
              ⛔ VERİ KAYBI KORUMASI — CLAUDE.md: "Bozuk veriyle sessizce ilerleyen bir
              göç, geri alınamaz hasar demektir."

              Up tamamen EKLEMELİ (üç yeni tablo + iki nullable kolon), yani onarılacak
              eski veri yok. Ama Down üç tabloyu DÜŞÜRÜYOR ve o tablolar kullanıcıların
              yazdığı gönderileri, yorumları ve oyları taşıyor. Geri alma bir dağıtım
              hatasında refleksle çalıştırılıyor ve sessizce çalışırsa forumun tamamı
              geri dönüşsüz siliniyor.

              Bu yüzden Down, içerik VARSA durur. Gerçekten silmek isteyen önce tabloları
              elle boşaltır — yani kaybı bilerek göze aldığını beyan etmiş olur.

              Şikayet kolonları koşulsuz düşmüyor: forum şikayeti kaydı varsa o da
              denetim izinin parçası.
            */
            migrationBuilder.Sql("""
                DO $koruma$
                DECLARE
                    gonderi bigint;
                    yorum   bigint;
                    oy      bigint;
                    sikayet bigint;
                BEGIN
                    SELECT count(*) INTO gonderi FROM community."Posts";
                    SELECT count(*) INTO yorum   FROM community."Comments";
                    SELECT count(*) INTO oy      FROM community."Votes";
                    SELECT count(*) INTO sikayet FROM moderation."Reports"
                     WHERE "CommunityPostId" IS NOT NULL OR "CommunityCommentId" IS NOT NULL;

                    IF gonderi > 0 OR yorum > 0 OR oy > 0 OR sikayet > 0 THEN
                        RAISE EXCEPTION
                            'Geri alma durduruldu: forumda veri var (% gonderi, % yorum, % oy, % sikayet). Bu goc geri alinirsa hepsi silinir. Gercekten silmek istiyorsaniz once tablolari elle bosaltin.',
                            gonderi, yorum, oy, sikayet;
                    END IF;
                END $koruma$;
                """);

            migrationBuilder.DropTable(
                name: "Votes",
                schema: "community");

            migrationBuilder.DropTable(
                name: "Comments",
                schema: "community");

            migrationBuilder.DropTable(
                name: "Posts",
                schema: "community");

            migrationBuilder.DropColumn(
                name: "CommunityCommentId",
                schema: "moderation",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "CommunityPostId",
                schema: "moderation",
                table: "Reports");
        }
    }
}
