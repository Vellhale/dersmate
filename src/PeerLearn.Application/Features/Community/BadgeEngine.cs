using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Community;

namespace PeerLearn.Application.Features.Community;

/// <summary>
/// Rozet kural motoru.
///
/// TASARIM: rozetler OLAY ANINDA değil, kullanıcının MEVCUT verisinden yeniden hesaplanır.
/// "Ders tamamlandı → sayacı artır → eşiği geçtiyse rozet ver" biçimindeki artımlı yaklaşım,
/// veri elle düzeltildiğinde ya da bir olay kaçırıldığında kalıcı olarak yanlış rozet
/// bırakır. Buradaki sorgular kullanıcı başına birkaç satır okur; bu maliyeti ödeyip
/// "rozet her zaman veriyle tutarlı" garantisini almak daha iyi bir takas.
///
/// İDEMPOTENT: aynı rozet iki kez verilmez (bellekte kontrol + DB'de unique index).
///
/// NE ZAMAN ÇALIŞIR: rozetler bir arka plan işiyle değil, kullanıcının verisini değiştiren
/// akışların sonunda yeniden hesaplanır (ders onayı, değerlendirme, beyan, hakem kararı).
/// </summary>
/// <remarks>
/// MOTOR TEK KURALA İNDİ — beş başarı rozeti emekli edildi (bkz. <see cref="BadgeCode"/>).
///
/// Silinen kurallarla birlikte ⚡ hızlı yanıt hesabı da gitti: gelen isteklerin yanıt
/// süresi ortancasını sağdan sansürlü gözlemleri ayıklayarak ölçen yaklaşık yüz satır.
/// Doğru çalışan bir koddu; sorun kalitesi değil, ürettiği rozetin artık hiçbir ekranda
/// görünmemesiydi. Arayüzü olmayan bir kuralı "belki lazım olur" diye tutmak, her
/// değerlendirmede bedeli ödenen ama karşılığı olmayan bir sorgu bırakırdı.
///
/// GERİ ALMA KURALI DEĞİŞMEDİ: rozet bir kez verildi mi geri alınmaz. Tek bilinçli
/// istisna moderasyonun ASILSIZ bulduğu öğretmen adaylığı beyanıdır
/// (bkz. ReviewTeacherCandidateHandler) — ki emeklilikten sonra bu, motorun ilgilendiği
/// yegâne rozetin ta kendisi.
/// </remarks>
public sealed class BadgeEngine
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public BadgeEngine(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Kullanıcının hak ettiği rozetleri hesaplar ve eksik olanları ekler.
    /// SaveChanges ÇAĞIRMAZ — çağıran handler'ın transaction'ına katılır.
    /// </summary>
    public async Task<IReadOnlyList<BadgeCode>> EvaluateAsync(Guid userId, CancellationToken ct)
    {
        var kazanilan = new List<BadgeCode>();

        /*
          Geleceğin öğretmeni: beyan YETERLİ, doğrulanmış olması gerekmez (doğrulama ayrı
          bir durum, bkz. entity notu). Ancak REDDEDİLMİŞ beyan rozet vermez: aksi halde
          moderatörün geri aldığı rozet bir sonraki değerlendirmede geri gelirdi.

          KULLANICI VARLIĞI AYRICA SORGULANMIYOR: beyan kaydı zaten var olan bir kullanıcıya
          FK ile bağlı. Eskiden buradaki ilk sorgu ders sayaçlarını okuduğu için kullanıcıyı
          da doğruluyordu; o kurallar emekli olunca sorgu da gereksizleşti.
        */
        var ogretmenAdayi = await _db.TeacherCandidateProfiles.AsNoTracking()
            .AnyAsync(p => p.UserId == userId && p.RejectedAtUtc == null, ct);

        if (ogretmenAdayi) kazanilan.Add(BadgeCode.FutureTeacher);

        return await AwardMissingAsync(userId, kazanilan, ct);
    }

    private async Task<IReadOnlyList<BadgeCode>> AwardMissingAsync(
        Guid userId, IReadOnlyList<BadgeCode> kazanilan, CancellationToken ct)
    {
        if (kazanilan.Count == 0)
        {
            return [];
        }

        var mevcut = await _db.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .Join(_db.Badges, ub => ub.BadgeId, b => b.Id, (ub, b) => b.Code)
            .ToListAsync(ct);

        var eksik = kazanilan.Distinct().Except(mevcut).ToList();
        if (eksik.Count == 0)
        {
            return [];
        }

        var katalog = await _db.Badges.AsNoTracking()
            .Where(b => eksik.Contains(b.Code) && b.IsActive)
            .ToDictionaryAsync(b => b.Code, b => b.Id, ct);

        var now = _clock.UtcNow;
        var verilen = new List<BadgeCode>();

        foreach (var code in eksik)
        {
            // Katalogda karşılığı yoksa sessizce atlanır: seed eksikse akış kırılmamalı.
            if (!katalog.TryGetValue(code, out var badgeId))
            {
                continue;
            }

            _db.UserBadges.Add(new UserBadge
            {
                UserId = userId,
                BadgeId = badgeId,
                EarnedAtUtc = now
            });
            verilen.Add(code);
        }

        return verilen;
    }
}
