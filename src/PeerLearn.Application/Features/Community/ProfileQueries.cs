using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Community;

// ---------------------------------------------------------------------------
// Değerlendirme listesi + özet (Yemeksepeti tarzı gösterim)
// ---------------------------------------------------------------------------

public sealed record GetTeacherReviewsQuery(Guid TutorUserId, int Page = 1, int PageSize = 10)
    : IRequest<TeacherReviewsDto>;

public sealed record ReviewTagCountDto(string Tag, int Count);

public sealed record ReviewItemDto(
    Guid ReviewId,
    string ReviewerDisplayName,
    string TopicName,
    int Score,
    int TeachingScore,
    int PunctualityScore,
    string? Comment,
    IReadOnlyList<string> Tags,
    bool WasVolunteerSession,
    DateTime CreatedAtUtc);

public sealed record TeacherReviewsDto(
    decimal AverageScore,
    decimal AverageTeachingScore,
    decimal AveragePunctualityScore,
    int ReviewCount,
    /// <summary>1★..5★ dağılımı — indeks 0 = 1 yıldız.</summary>
    IReadOnlyList<int> ScoreDistribution,
    IReadOnlyList<ReviewTagCountDto> PopularTags,
    PagedResult<ReviewItemDto> Reviews);

public sealed class GetTeacherReviewsHandler : IRequestHandler<GetTeacherReviewsQuery, TeacherReviewsDto>
{
    private readonly IAppDbContext _db;

    public GetTeacherReviewsHandler(IAppDbContext db) => _db = db;

    public async Task<TeacherReviewsDto> Handle(GetTeacherReviewsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var taban = _db.SessionReviews.AsNoTracking().Where(r => r.RevieweeUserId == request.TutorUserId);

        var toplam = await taban.CountAsync(ct);
        if (toplam == 0)
        {
            return new TeacherReviewsDto(0, 0, 0, 0, [0, 0, 0, 0, 0], [],
                PagedResult<ReviewItemDto>.Empty(page, pageSize));
        }

        // Üç ortalama tek turda: ayrı ayrı sorgulamak üç DB gidiş-dönüşü demek olurdu.
        var ozet = await taban
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Genel = g.Average(r => (decimal)r.Score),
                Anlatim = g.Average(r => (decimal)r.TeachingScore),
                Zamanlama = g.Average(r => (decimal)r.PunctualityScore)
            })
            .SingleAsync(ct);

        var dagilimHam = await taban
            .GroupBy(r => r.Score)
            .Select(g => new { Puan = g.Key, Adet = g.Count() })
            .ToListAsync(ct);

        var dagilim = new int[5];
        foreach (var satir in dagilimHam)
        {
            if (satir.Puan is >= 1 and <= 5)
            {
                dagilim[satir.Puan - 1] = satir.Adet;
            }
        }

        // Etiket dağılımı: ayrı tablo olduğu için doğrudan GROUP BY (bkz. SessionReviewTag notu).
        var etiketler = await (
                from tag in _db.SessionReviewTags.AsNoTracking()
                join review in taban on tag.ReviewId equals review.Id
                group tag by tag.Tag into g
                orderby g.Count() descending
                select new { Etiket = g.Key, Adet = g.Count() })
            .Take(10)
            .ToListAsync(ct);

        var sayfa = await (
                from review in taban
                join session in _db.LessonSessions.AsNoTracking() on review.SessionId equals session.Id
                join topic in _db.Topics.AsNoTracking() on session.TopicId equals topic.Id
                join reviewer in _db.Users.AsNoTracking() on review.ReviewerUserId equals reviewer.Id
                orderby review.CreatedAtUtc descending, review.Id
                select new
                {
                    review.Id,
                    reviewer.DisplayName,
                    TopicName = topic.Name,
                    review.Score,
                    review.TeachingScore,
                    review.PunctualityScore,
                    review.Comment,
                    review.WasVolunteerSession,
                    review.CreatedAtUtc
                })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Etiketler ayrı çekilip bellekte eşlenir: satır başına alt sorgu N+1 üretirdi.
        var sayfaIdleri = sayfa.Select(r => r.Id).ToList();
        var sayfaEtiketleri = await _db.SessionReviewTags.AsNoTracking()
            .Where(t => sayfaIdleri.Contains(t.ReviewId))
            .Select(t => new { t.ReviewId, t.Tag })
            .ToListAsync(ct);

        var etiketHaritasi = sayfaEtiketleri
            .GroupBy(t => t.ReviewId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Tag.ToString()).ToList());

        var kayitlar = sayfa
            .Select(r => new ReviewItemDto(
                r.Id, r.DisplayName, r.TopicName, r.Score, r.TeachingScore, r.PunctualityScore,
                r.Comment,
                etiketHaritasi.TryGetValue(r.Id, out var t) ? t : [],
                r.WasVolunteerSession, r.CreatedAtUtc))
            .ToList();

        return new TeacherReviewsDto(
            Math.Round(ozet.Genel, 2),
            Math.Round(ozet.Anlatim, 2),
            Math.Round(ozet.Zamanlama, 2),
            toplam,
            dagilim,
            etiketler.Select(e => new ReviewTagCountDto(e.Etiket.ToString(), e.Adet)).ToList(),
            new PagedResult<ReviewItemDto>(kayitlar, toplam, page, pageSize));
    }
}

// ---------------------------------------------------------------------------
// Samimi profil kartı
// ---------------------------------------------------------------------------

public sealed record GetUserProfileQuery(Guid UserId, Guid ViewerUserId) : IRequest<UserProfileDto>;

public sealed record ProfileTopicDto(Guid TopicId, string TopicName, string SubjectName, int Level, bool IsVolunteer);

public sealed record TeacherCandidateDto(
    string University,
    string Faculty,
    string Department,
    int? GradeYear,

    /// <summary>Düzenleme formu mevcut değeri geri yükleyebilsin diye döner.</summary>
    bool HasPedagogicalCertificate,

    bool IsVerified,

    /// <summary>Pending | Verified | Rejected. Başkasının profilinde ret ASLA görünmez.</summary>
    string ReviewStatus,

    /// <summary>Hakemin gerekçesi — yalnızca beyanın sahibine döner.</summary>
    string? ReviewNote,

    /// <summary>
    /// Öğrenci belgesi yüklü mü. Yalnızca BAYRAK döner — dosya yolu/anahtarı asla DTO'ya
    /// girmez. Başkasının profilinde de false görünür: birinin belge yükleyip yüklemediği
    /// o kişinin özel bilgisidir, doğrulama sonucu zaten ReviewStatus'ta.
    /// </summary>
    bool HasDocument);

public sealed record UserProfileDto(
    Guid UserId,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string? University,
    string? Department,
    DateTime JoinedAtUtc,
    decimal AverageRating,
    int RatingCount,
    int TaughtSessionCount,
    int TaughtMinutes,

    /// <summary>Ders anlatarak biriktirilen toplam puan — seviyenin dayanağı.</summary>
    int TotalEarnedCredits,

    /// <summary>Krediden türeyen seviye (1..10). Ad ve emoji YOK: rozet artık rakam gösteriyor.</summary>
    int Level,

    /// <summary>Bu seviyenin başladığı puan. İlerleme çubuğunun sol ucu.</summary>
    int LevelMinCredits,

    /// <summary>
    /// Bir sonraki seviyenin başladığı puan; en üst seviyede <c>null</c>.
    /// SUNUCUDAN GELİYOR ki eşikler değiştiğinde arayüzdeki ilerleme satırı yalan söylemesin —
    /// eşikleri istemciye kopyalamak, bu projede daha önce fiyat formülünde yaşanan
    /// sapmanın aynısını üretirdi.
    /// </summary>
    int? NextLevelAt,

    /// <summary>
    /// Forumda aldığı TOPLAM yukarı oy (gönderi + yorum) — topluluk rozetlerinin
    /// dayanağı (100 bronz / 500 gümüş / 1000 altın).
    ///
    /// OKUMA ANINDA SAYILIYOR, saklanan bir sayaç yok. Seviye hesabındaki kararla aynı
    /// gerekçe: saklanan bir toplam, oy geri alındığında ya da içerik kaldırıldığında
    /// güncellenmeyi unutur ve sessizce şişer. Kullanıcı başına iki toplama sorgusu,
    /// profil ucunda kabul edilebilir bir maliyet.
    ///
    /// KALDIRILMIŞ İÇERİĞİN OYU SAYILMIYOR: moderasyonun kaldırdığı bir gönderinin
    /// topladığı oy, rozet kazandırmaya devam etseydi kural ihlali ödüllendirilirdi.
    /// </summary>
    int CommunityUpvotes,

    bool IsSelf,
    TeacherCandidateDto? TeacherCandidate,
    IReadOnlyList<ProfileTopicDto> CanTeach,
    IReadOnlyList<ProfileTopicDto> WantsToLearn);

public sealed class GetUserProfileHandler : IRequestHandler<GetUserProfileQuery, UserProfileDto>
{
    private readonly IAppDbContext _db;

    public GetUserProfileHandler(IAppDbContext db) => _db = db;

    public async Task<UserProfileDto> Handle(GetUserProfileQuery request, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
                       .SingleOrDefaultAsync(u => u.Id == request.UserId, ct)
                   ?? throw new AppException(ErrorCodes.NotAuthorized, "Kullanıcı bulunamadı.", statusCode: 404);

        /*
          UNVAN OKUMA ANINDA HESAPLANIYOR — saklanan bir satır yok.

          Alternatif, unvanı kazanıldığı anda yazmaktı. O yol sessiz bir gecikme üretirdi:
          unvanı yükseltecek tek olay ders onayı, ama rozet/unvan motorunu tetikleyen
          akışlar bambaşka yerlerdeydi (yorum, eşleşme yanıtı, profil güncellemesi).
          Kullanıcı 500. puanını kazandığı anda değil, günler sonra rastgele bir eylemde
          Öğretici olurdu — hiçbir hata olmadan.

          Hesap saf bir fonksiyon ve User satırı zaten okunmuş durumda; ek sorgu yok.
          Eşikler değişirse geçmişe dönük kendiliğinden düzelir.
        */
        var seviye = UserLevelRules.Hesapla(user.TotalEarnedCredits);

        var portfolio = await (
                from entry in _db.PortfolioEntries.AsNoTracking()
                join topic in _db.Topics.AsNoTracking() on entry.TopicId equals topic.Id
                join subject in _db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                where entry.UserId == user.Id && entry.IsActive
                select new
                {
                    entry.Direction,
                    entry.TopicId,
                    TopicName = topic.Name,
                    SubjectName = subject.Name,
                    entry.SelfAssessedLevel,
                    entry.IsVolunteer
                })
            .ToListAsync(ct);

        var kendisi = user.Id == request.ViewerUserId;

        /*
          FORUM OYLARI — topluluk rozetlerinin (100/500/1000) dayanağı.

          İki ayrı toplama çünkü gönderi ve yorum ayrı tablolar. `Sum` boş kümede
          hata verirdi, bu yüzden `int?` üzerinden toplanıp null 0'a düşürülüyor.

          Yalnızca `Visible`: perdeli (UnderReview) ya da kaldırılmış (Removed) içeriğin
          oyu rozete girmiyor. Kaldırılan içerikte gerekçe açık — kural ihlali rozet
          kazandırmamalı. Perdeli içerikte de aynı yönde davranıyoruz: henüz karar
          verilmemiş bir içeriğin oyunu şimdiden saymak, karar "kaldır" çıkarsa rozeti
          geri almayı gerektirirdi ve kazanılmış görünen bir rozetin geri alınması,
          hiç verilmemesinden daha kötü.
        */
        var gonderiOylari = await _db.CommunityPosts.AsNoTracking()
            .Where(p => p.AuthorUserId == user.Id && p.Status == ForumContentStatus.Visible)
            .SumAsync(p => (int?)p.UpvoteCount, ct) ?? 0;

        var yorumOylari = await _db.CommunityComments.AsNoTracking()
            .Where(c => c.AuthorUserId == user.Id && c.Status == ForumContentStatus.Visible)
            .SumAsync(c => (int?)c.UpvoteCount, ct) ?? 0;

        var forumOylari = gonderiOylari + yorumOylari;

        var adayKaydi = await _db.TeacherCandidateProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == user.Id, ct);

        /*
          RET BAŞKASINA GÖSTERİLMEZ. Reddedilmiş beyan, dışarıya doğrulanmamış bir beyanla
          aynı görünür ("Beyan"); gerekçe metni yalnızca kişinin kendisine döner.

          Neden: ret, kullanıcı ile moderasyon arasındaki bir karardır. Profilde herkese
          "bu kişinin öğretmenlik beyanı reddedildi" yazmak, sistemin teyit edemediği bir
          konuda kişiyi teşhir etmek olurdu — üstelik kullanıcı bilgilerini düzeltip
          yeniden gönderebiliyorken.
        */
        var aday = adayKaydi is null
            ? null
            : new TeacherCandidateDto(
                adayKaydi.University,
                adayKaydi.Faculty,
                adayKaydi.Department,
                adayKaydi.GradeYear,
                adayKaydi.HasPedagogicalCertificate,
                adayKaydi.IsVerified,
                kendisi || adayKaydi.IsVerified
                    ? adayKaydi.ReviewStatus.ToString()
                    : TeacherCandidateReviewStatus.Pending.ToString(),
                kendisi ? adayKaydi.ReviewNote : null,
                kendisi && adayKaydi.HasDocument);

        return new UserProfileDto(
            user.Id,
            user.DisplayName,
            user.Bio,
            user.AvatarUrl,
            user.University,
            user.Department,
            user.CreatedAtUtc,
            user.AverageRating,
            user.RatingCount,
            user.TaughtSessionCount,
            user.TaughtMinutes,
            user.TotalEarnedCredits,
            seviye.Level,
            seviye.MinCredits,
            seviye.NextLevelAt,
            forumOylari,
            IsSelf: kendisi,
            aday,
            portfolio.Where(e => e.Direction == PortfolioDirection.Offer).Select(e =>
                new ProfileTopicDto(e.TopicId, e.TopicName, e.SubjectName, e.SelfAssessedLevel, e.IsVolunteer)).ToList(),
            portfolio.Where(e => e.Direction == PortfolioDirection.Seek).Select(e =>
                new ProfileTopicDto(e.TopicId, e.TopicName, e.SubjectName, e.SelfAssessedLevel, false)).ToList());
    }
}
