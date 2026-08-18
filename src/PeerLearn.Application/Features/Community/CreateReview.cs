using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Community;

/// <summary>
/// Ders sonu değerlendirmesi.
///
/// ANTI-SPAM — üç kapı, üçü de sunucuda:
///   1. Ders TAMAMLANMIŞ olmalı (Completed). Rezerve/iptal/itirazlı derse yorum yazılamaz.
///   2. Yorumu yalnızca dersin ÖĞRENCİSİ yazabilir; eğitmen kendi dersini puanlayamaz.
///   3. Ders başına tek yorum (unique index son savunma).
/// Arayüzün modalı zorunlu göstermesi bir kolaylıktır, kural DEĞİLDİR — bu kontroller
/// istemciden bağımsız olarak burada uygulanır.
/// </summary>
public sealed record CreateReviewCommand(
    Guid SessionId,
    Guid ReviewerUserId,
    int Score,
    int TeachingScore,
    int PunctualityScore,
    IReadOnlyList<ReviewTag> Tags,
    string? Comment) : IRequest<CreateReviewResult>;

public sealed record CreateReviewResult(Guid ReviewId, decimal TutorNewAverage, int TutorReviewCount);

public sealed class CreateReviewHandler : IRequestHandler<CreateReviewCommand, CreateReviewResult>
{
    private const int MaxTags = 6;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly BadgeEngine _badges;

    public CreateReviewHandler(IAppDbContext db, IClock clock, BadgeEngine badges)
    {
        _db = db;
        _clock = clock;
        _badges = badges;
    }

    public async Task<CreateReviewResult> Handle(CreateReviewCommand request, CancellationToken ct)
    {
        foreach (var (ad, puan) in new[]
                 {
                     ("Genel deneyim", request.Score),
                     ("Anlatım", request.TeachingScore),
                     ("Zamanlama", request.PunctualityScore)
                 })
        {
            if (puan is < 1 or > 5)
            {
                throw new AppException(ErrorCodes.ValidationFailed, $"{ad} puanı 1-5 arasında olmalı.");
            }
        }

        var comment = request.Comment?.Trim();
        if (comment is { Length: > 1000 })
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Yorum en fazla 1000 karakter olabilir.");
        }

        var tags = (request.Tags ?? []).Distinct().ToList();
        if (tags.Count > MaxTags)
        {
            throw new AppException(ErrorCodes.ValidationFailed, $"En fazla {MaxTags} etiket seçilebilir.");
        }

        var session = await _db.LessonSessions.AsNoTracking()
                          .SingleOrDefaultAsync(s => s.Id == request.SessionId, ct)
                      ?? throw new AppException(ErrorCodes.SessionNotFound, "Ders bulunamadı.", statusCode: 404);

        // Kapı 1: yalnızca tamamlanmış ders.
        if (session.Status != SessionStatus.Completed)
        {
            throw new AppException(ErrorCodes.InvalidSessionState,
                "Değerlendirme yalnızca tamamlanmış dersler için yapılabilir.", statusCode: 409);
        }

        // Kapı 2: yalnızca öğrenci.
        if (session.StudentUserId != request.ReviewerUserId)
        {
            throw new AppException(ErrorCodes.NotSessionParticipant,
                "Bu dersi yalnızca dersi alan öğrenci değerlendirebilir.", statusCode: 403);
        }

        // Kapı 3: tek yorum.
        var zatenVar = await _db.SessionReviews
            .AnyAsync(r => r.SessionId == session.Id && r.ReviewerUserId == request.ReviewerUserId, ct);

        if (zatenVar)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Bu ders için değerlendirmen zaten kayıtlı.", statusCode: 409);
        }

        await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

        var review = new SessionReview
        {
            SessionId = session.Id,
            ReviewerUserId = request.ReviewerUserId,
            RevieweeUserId = session.TutorUserId,
            Score = request.Score,
            TeachingScore = request.TeachingScore,
            PunctualityScore = request.PunctualityScore,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
            // Gönüllülük artık ayrı bir bayrak; ücretten çıkarım YAPILMAZ (bkz. LessonSession).
            // Bu damga kalıcı yazıldığı için yanlış okumanın geri dönüşü yok.
            WasVolunteerSession = session.IsVolunteer,
            CreatedAtUtc = _clock.UtcNow
        };
        _db.SessionReviews.Add(review);

        foreach (var tag in tags)
        {
            _db.SessionReviewTags.Add(new SessionReviewTag { ReviewId = review.Id, Tag = tag });
        }

        /*
          ORTALAMA YENİDEN HESAPLANIR, artımlı güncellenmez.
          "yeniOrtalama = (eski*n + puan)/(n+1)" biçimi decimal yuvarlama hatasını biriktirir
          ve bir satır elle silindiğinde ortalama kalıcı olarak yanlış kalır. Kullanıcı başına
          yorum sayısı küçük; doğru sayıyı okumak ucuz.

          Bu yorum henüz SaveChanges edilmediği için sorguya AYRICA eklenir.
        */
        var mevcutPuanlar = await _db.SessionReviews.AsNoTracking()
            .Where(r => r.RevieweeUserId == session.TutorUserId)
            .Select(r => r.Score)
            .ToListAsync(ct);

        mevcutPuanlar.Add(request.Score);

        var tutor = await _db.Users.SingleAsync(u => u.Id == session.TutorUserId, ct);
        tutor.RatingCount = mevcutPuanlar.Count;
        tutor.AverageRating = Math.Round((decimal)mevcutPuanlar.Average(), 2);

        /*
          Yorum ÖNCE yazılır, rozet SONRA değerlendirilir — ikisi de aynı transaction'da.

          Kural motoru puanları DB'den okuyor; değerlendirme SaveChanges'ten önce
          çağrılsaydı yeni yorum sayıma girmez ve "5 yıldızlı anlatıcı" eşiği (5 yorum)
          tam 5. yorumda değil 6.'da açılırdı — sessiz bir off-by-one.
        */
        await _db.SaveChangesAsync(ct);
        await _badges.EvaluateAsync(session.TutorUserId, ct);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return new CreateReviewResult(review.Id, tutor.AverageRating, tutor.RatingCount);
    }
}
