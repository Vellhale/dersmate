using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Branş rozetleri üç kademeden ikiye indi: 5/20/50 saat → 8/15 saat.
    /// </summary>
    /// <remarks>
    /// ŞEMA DEĞİŞMİYOR, VERİ DEĞİŞİYOR. "Level" kolonu zaten varchar(20) ve yeni adlar
    /// ("Ogretici", "Ustad") oraya sığıyor. Sorun şemada değil, satırların ANLAMINDA:
    ///
    ///   • "Cirak" ve "Usta" enum üyeleri kaldırıldı. Enum'lar veritabanında METİN olarak
    ///     saklanıyor (HasConversion&lt;string&gt;), yani bu metni taşıyan satırlar okunma
    ///     anında patlar — hata da okuyan yerde değil, profili açan kullanıcıda görünür.
    ///   • "Ustad" adı KALDI ama eşiği 50 saatten 15 saate indi. Yani eski Ustad satırı
    ///     bugünkü kurala göre de yanlış: 50 saat anlatmış birine verilmişti, oysa artık
    ///     15 saatte hak ediliyor. Adı tanınıyor diye bırakmak, sessizce yanlış bir
    ///     kazanım tarihi taşımak olurdu.
    ///
    /// Bu yüzden TÜM satırlar siliniyor ve tamamlanmış derslerden YENİDEN hesaplanıyor.
    /// Bu tehlikeli görünüyor ama değil: rozet tablosu türetilmiş veridir, tek doğruluk
    /// kaynağı LessonSessions'tır ve motor da zaten her değerlendirmede aynı hesabı
    /// baştan yapıyor (bkz. UserSubjectBadge sınıf notu). Silinen tek şey, yeniden
    /// üretilebilen bir önbellek.
    ///
    /// KAYBEDİLEN TEK ŞEY: EarnedAtUtc'nin gerçek kazanım anı olması. Yeniden hesaplanan
    /// satırlar göç anını taşıyor. Doğrusu, ilgili dersin bitiş saatini kullanmak — bu
    /// yüzden aşağıda EarnedAtUtc, eşiğin AŞILDIĞI dersin bitiş anından türetiliyor,
    /// göç zamanından değil. Alan denetim içindir ve yalan söylememeli.
    ///
    /// KURAL BURADA BİR KEZ TEKRARLANIYOR (8 saat / 15 saat). Normalde eşiği ikinci bir
    /// yere yazmak yasak — ama göç bir ZAMAN FOTOĞRAFIDIR: bugünkü kuralla bugünkü veriyi
    /// onarır ve bir daha çalışmaz. Kural yarın değişirse bu göç değişmemeli, yeni bir
    /// göç yazılmalı. Bu yüzden C# sabitlerine bağlanmadı; bağlansaydı eski bir göç,
    /// yeni bir kuralla geçmişi yeniden yazardı.
    /// </remarks>
    public partial class SubjectBadgeTwoTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE
    silinen  int;
    eklenen  int;
    kacak    int;
BEGIN
    -- 1) ESKİ SATIRLARI TEMİZLE. Hepsi: tanınmayan adlar da, eşiği değişen ""Ustad"" da.
    DELETE FROM community.""UserSubjectBadges"";
    GET DIAGNOSTICS silinen = ROW_COUNT;

    -- 2) TAMAMLANMIŞ DERSLERDEN YENİDEN HESAPLA.
    --    YALNIZCA Completed sayılır — motorun kuralıyla birebir aynı (SubjectBadgeEngine).
    --    İtirazlı ya da onay bekleyen ders saymaz: ders ya tamamlanır ya tamamlanmaz.
    WITH sureler AS (
        SELECT ls.""TutorUserId"" AS kullanici,
               s.""Branch""       AS brans,
               SUM(ls.""DurationMinutes"")::int AS dakika
        FROM scheduling.""LessonSessions"" ls
        JOIN catalog.""Topics""   t ON t.""Id"" = ls.""TopicId""
        JOIN catalog.""Subjects"" s ON s.""Id"" = t.""SubjectId""
        WHERE ls.""Status"" = 'Completed'
        GROUP BY ls.""TutorUserId"", s.""Branch""
    ),
    kademeler AS (
        SELECT kullanici, brans, dakika, 'Ogretici' AS seviye, 480 AS esik FROM sureler WHERE dakika >= 480
        UNION ALL
        SELECT kullanici, brans, dakika, 'Ustad'    AS seviye, 900 AS esik FROM sureler WHERE dakika >= 900
    ),
    -- Kazanım anı: eşiğin AŞILDIĞI dersin bitişi. Kümülatif toplam eşiği ilk geçtiği
    -- ders bulunuyor; göç zamanını yazmak, denetim alanını yalancı yapardı.
    kazanim AS (
        SELECT k.kullanici, k.brans, k.seviye, k.dakika,
               (SELECT MIN(x.bitis) FROM (
                    SELECT ls2.""ScheduledEndUtc"" AS bitis,
                           SUM(ls2.""DurationMinutes"") OVER (
                               PARTITION BY ls2.""TutorUserId"", s2.""Branch""
                               ORDER BY ls2.""ScheduledEndUtc""
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                           ) AS birikim
                    FROM scheduling.""LessonSessions"" ls2
                    JOIN catalog.""Topics""   t2 ON t2.""Id"" = ls2.""TopicId""
                    JOIN catalog.""Subjects"" s2 ON s2.""Id"" = t2.""SubjectId""
                    WHERE ls2.""Status"" = 'Completed'
                      AND ls2.""TutorUserId"" = k.kullanici
                      AND s2.""Branch"" = k.brans
               ) x WHERE x.birikim >= k.esik) AS kazanildi
        FROM kademeler k
    )
    INSERT INTO community.""UserSubjectBadges""
        (""Id"", ""UserId"", ""Branch"", ""Level"", ""EarnedAtUtc"", ""MinutesAtAward"", ""CreatedAtUtc"")
    SELECT gen_random_uuid(), kullanici, brans, seviye,
           COALESCE(kazanildi, now()), dakika, now()
    FROM kazanim;
    GET DIAGNOSTICS eklenen = ROW_COUNT;

    -- 3) DEĞİŞMEZİ SINA. Bozuk veriyle sessizce ilerleyen bir göç, geri alınamaz hasardır.
    SELECT COUNT(*) INTO kacak
    FROM community.""UserSubjectBadges""
    WHERE ""Level"" NOT IN ('Ogretici', 'Ustad');

    IF kacak > 0 THEN
        RAISE EXCEPTION 'Rozet gocu: taninmayan seviye tasiyan % satir kaldi', kacak;
    END IF;

    RAISE NOTICE 'Rozet gocu: % satir silindi, % satir yeniden hesaplandi', silinen, eklenen;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
              GERİ ALMA ESKİ SATIRLARI DİRİLTEMEZ ve bunu gizlemiyoruz.

              Eski rozetler 5/20/50 saat kuralıyla verilmişti; o kural artık kodda yok,
              dolayısıyla buradan üretilecek her satır uydurma olurdu. Yapılabilecek tek
              dürüst şey tabloyu boşaltmak: rozetler türetilmiş veri, motor bir sonraki
              değerlendirmede kendi kuralına göre yeniden yazar.
            */
            migrationBuilder.Sql(@"DELETE FROM community.""UserSubjectBadges"";");
        }
    }
}
