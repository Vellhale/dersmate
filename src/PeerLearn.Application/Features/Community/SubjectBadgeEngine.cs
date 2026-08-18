using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Community;

/// <summary>
/// Branş rozeti kural motoru: bir eğitmenin her derste ANLATTIĞI süreye bakar ve
/// 5 / 20 / 50 saat eşiklerini geçtiği branşlarda Çırak / Usta / Üstad rozetini verir.
///
/// TASARIM — <see cref="BadgeEngine"/> ile aynı ilke: rozetler olay anında bir sayaç
/// artırılarak değil, MEVCUT VERİDEN yeniden hesaplanır. Ürün isteği "anlatım saati
/// sayacını güncelle" diyordu; denormalize sayaç, veri elle düzeltildiğinde (hakem bir
/// dersi iptal etti, yönetici bir kaydı temizledi) ya da bir olay kaçırıldığında kalıcı
/// olarak yanlış rozet bırakır. Buradaki sorgu eğitmen başına birkaç satır okur; bu
/// maliyeti ödeyip "rozet her zaman veriyle tutarlı" garantisini almak daha iyi bir takas.
/// Tek doğruluk kaynağı LessonSessions tablosudur.
///
/// İDEMPOTENT: aynı rozet iki kez verilmez (bellekte kontrol + DB'de unique index).
///
/// NE ZAMAN ÇALIŞIR: ders onayının SONUNDA (bkz. ApproveSessionHandler). Rozet ancak
/// tamamlanmış dersten doğar, tamamlanma da tek noktadan geçer.
/// </summary>
/// <remarks>
/// ⚠️ ÇAĞRI SIRASI: <see cref="EvaluateAsync"/> mutlaka SaveChanges'ten SONRA çağrılmalı.
/// Motor veriyi DB'den okur; onaylanan ders henüz yazılmamışsa o dersin süresi hesaba
/// girmez ve eşiği tam o derste dolduran eğitmen rozetini bir ders geç alır. Desen:
/// <c>BeginTransaction → SaveChanges → EvaluateAsync → SaveChanges → Commit</c>.
/// Bu tuzağa proje boyunca dört kez düşüldü (bkz. docs/DEVAM-EDILECEK.md).
///
/// SaveChanges ÇAĞIRMAZ — çağıranın transaction'ına katılır.
///
/// GERİ ALMA: rozet bir kez verildi mi geri alınmaz, süre sonradan düşse bile. Neden:
/// eşiği geçmek geçmişte yaşanmış bir olaydır ve iptal edilen bir ders, verilmiş emeği
/// geri almaz. Motor yalnızca EKLER; sildiği hiçbir satır yoktur.
/// </remarks>
public sealed class SubjectBadgeEngine
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public SubjectBadgeEngine(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Bir branşta ölçülen toplam anlatım süresi.</summary>
    public sealed record BranchMinutes(SubjectBranch Branch, int Minutes);

    /// <summary>
    /// Eğitmenin branş bazında toplam anlatım süresini tamamlanmış derslerden hesaplar.
    /// Profil ekranı da bunu kullanır: rozetin yanında "kaç saat" göstermek için ayrı
    /// bir sorgu yazılmasın, iki yer aynı tanımdan beslensin.
    /// </summary>
    public async Task<IReadOnlyList<BranchMinutes>> GetTaughtMinutesAsync(Guid tutorUserId, CancellationToken ct)
    {
        /*
          YALNIZCA Completed SAYILIR.

          Booked/AwaitingApproval henüz yaşanmamış ya da onaylanmamış emektir; Cancelled ve
          Expired hiç yaşanmamıştır. Disputed bilerek DIŞARIDA: itiraz karara bağlanınca
          ders ya Completed olur (ve buraya girer) ya da olmaz. İtirazlıyı saymak,
          hakem daha karar vermeden rozet dağıtmak olurdu.

          GÖNÜLLÜ DERSLER SAYILIR. Rozetin ölçtüğü şey kazanılan puan değil, harcanan
          zaman — ürün isteğinin özü buydu. Gönüllü ders de anlatılmış derstir.
        */
        /*
          GRUPLAMA SUNUCUDA DEĞİL, BELLEKTE — bilinçli bir geri adım.

          Doğal yazım `group session by subject.Branch!.Value` olurdu ve SQL tarafında
          toplardı. Ama `Branch` hem NULLABLE hem de değer dönüştürücülü (enum → metin)
          bir alan; EF'in bu ikisinin birleşiminde `Nullable.Value` erişimini çevirememe
          ihtimali var ve çeviremezse çalışma zamanında istisna atar.

          Bu metot ders ONAYININ içinden, açık bir transaction'da çağrılıyor
          (bkz. ApproveSessionHandler). Orada atılan bir istisna dersi tamamlanamaz hâle
          getirir — yani çevrilemeyen bir sorgunun bedeli kozmetik değil. Düz satırları
          çekip belleğe toplamak, çevirisi garanti bir sorgu bırakıyor.

          MALİYET KABUL EDİLEBİLİR: satır sayısı, eğitmenin TAMAMLANMIŞ ders sayısı kadar.
          Bir eğitmen için bu birkaç yüzü geçmez ve sorgu iki tam sayı kolonu okur.
          Sayı gerçekten büyürse doğru çözüm sunucu tarafı gruplama değil, denormalize bir
          özet tablo olur — o da ancak ölçülmüş bir sorun varsa.
        */
        var satirlar = await (
                from session in _db.LessonSessions.AsNoTracking()
                join topic in _db.Topics.AsNoTracking() on session.TopicId equals topic.Id
                join subject in _db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                where session.TutorUserId == tutorUserId
                      && session.Status == SessionStatus.Completed
                      && subject.Branch != null
                select new { subject.Branch, session.DurationMinutes })
            .ToListAsync(ct);

        return satirlar
            .GroupBy(x => x.Branch!.Value)
            .Select(g => new BranchMinutes(g.Key, g.Sum(x => x.DurationMinutes)))
            .ToList();
    }

    /// <summary>
    /// Eğitmenin hak ettiği branş rozetlerini hesaplar ve eksik olanları ekler.
    /// SaveChanges ÇAĞIRMAZ. Dönen liste, BU çağrıda yeni verilen rozetlerdir.
    /// </summary>
    public async Task<IReadOnlyList<UserSubjectBadge>> EvaluateAsync(Guid tutorUserId, CancellationToken ct)
    {
        var sureler = await GetTaughtMinutesAsync(tutorUserId, ct);
        if (sureler.Count == 0)
        {
            return [];
        }

        var mevcut = await _db.UserSubjectBadges.AsNoTracking()
            .Where(b => b.UserId == tutorUserId)
            .Select(b => new { b.Branch, b.Level })
            .ToListAsync(ct);

        var mevcutKume = mevcut.Select(m => (m.Branch, m.Level)).ToHashSet();

        var now = _clock.UtcNow;
        var verilen = new List<UserSubjectBadge>();

        foreach (var (brans, dakika) in sureler.Select(s => (s.Branch, s.Minutes)))
        {
            foreach (var seviye in SubjectBadgeRules.EarnedLevels(dakika))
            {
                if (!mevcutKume.Add((brans, seviye)))
                {
                    continue; // zaten var
                }

                var rozet = new UserSubjectBadge
                {
                    UserId = tutorUserId,
                    Branch = brans,
                    Level = seviye,
                    EarnedAtUtc = now,

                    /*
                      Eşiğin kendisi değil, ÖLÇÜLEN süre yazılır. Aradaki fark denetimde
                      önemli: 50 saatlik eşiği 62 saatte dolduran biri için "eşik 3000"
                      yazmak, rozetin neden o anda verildiğini açıklamaz. Bu alan bir
                      sayaç değil, kazanım anının fotoğrafıdır (bkz. entity notu).
                    */
                    MinutesAtAward = dakika
                };

                _db.UserSubjectBadges.Add(rozet);
                verilen.Add(rozet);
            }
        }

        return verilen;
    }
}
