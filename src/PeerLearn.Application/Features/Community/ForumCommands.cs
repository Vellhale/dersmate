using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Community;

namespace PeerLearn.Application.Features.Community;

/*
  ══════════════════════════════════════════════════════════════════════════════
  FORUM YAZMA UÇLARI: gönderi, yorum, oy.

  ⚠️ OY YOLU EKONOMİ KALIBINI İZLİYOR (CLAUDE.md): dağıtık kilit + açık transaction
  + ConcurrencyRetry. Üçü de gerekli ve farklı şeyi kapatıyor:
    • kilit       → aynı içeriğe aynı anda gelen istekleri süreç içinde serileştirir
    • transaction → oy satırı ile sayaç güncellemesinin yarım yazılmasını engeller
    • retry       → iki instance arasındaki xmin çakışmasını yeniden dener

  Burada PARA yok ama sorun aynı sınıftan: denormalize sayaç + eşzamanlı yazma.
  CreditLedgerService'ten farkı, defter değil sayaç olması; kalıp aynı.
  ══════════════════════════════════════════════════════════════════════════════
*/

public sealed record CreateForumPostCommand(
    Guid AuthorUserId, ForumTag Tag, string Title, string Body) : IRequest<Guid>;

public sealed class CreateForumPostHandler : IRequestHandler<CreateForumPostCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CreateForumPostHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateForumPostCommand request, CancellationToken ct)
    {
        var baslik = (request.Title ?? string.Empty).Trim();
        var govde = (request.Body ?? string.Empty).Trim();

        if (baslik.Length is < ForumRules.TitleMinLength or > ForumRules.TitleMaxLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Başlık {ForumRules.TitleMinLength}-{ForumRules.TitleMaxLength} karakter olmalı.");
        }

        if (govde.Length is < ForumRules.BodyMinLength or > ForumRules.BodyMaxLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Gönderi {ForumRules.BodyMinLength}-{ForumRules.BodyMaxLength} karakter olmalı.");
        }

        var yazar = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.AuthorUserId)
            .Select(u => new { u.CreatedAtUtc, u.TotalEarnedCredits })
            .SingleOrDefaultAsync(ct)
            ?? throw new AppException(ErrorCodes.UserNotFound, "Kullanıcı bulunamadı.", statusCode: 404);

        await SpamKapisiAsync(request.AuthorUserId, yazar.CreatedAtUtc, ct);
        BaglantiKapisi(govde, yazar.TotalEarnedCredits);

        var gonderi = new CommunityPost
        {
            AuthorUserId = request.AuthorUserId,
            Tag = request.Tag,
            Title = baslik,
            Body = govde,
            Status = ForumContentStatus.Visible,
            CreatedAtUtc = _clock.UtcNow
        };

        _db.CommunityPosts.Add(gonderi);
        await _db.SaveChangesAsync(ct);

        return gonderi.Id;
    }

    /// <summary>
    /// Günlük gönderi tavanı. Arayüzdeki "Yeni hesap sınırı — ilk hafta günde en fazla
    /// 3 gönderi" vaadinin gerçek karşılığı; o metin yazıldığında kodda karşılığı yoktu.
    ///
    /// Yeni hesap için sıkı, yerleşik hesap için gevşek: spam hesabı ucuz ve taze olur,
    /// yıllardır kullanan biri değil.
    /// </summary>
    private async Task SpamKapisiAsync(Guid userId, DateTime hesapAcilis, CancellationToken ct)
    {
        var simdi = _clock.UtcNow;
        var yeniHesap = (simdi - hesapAcilis).TotalDays < ForumRules.NewAccountDays;
        var tavan = yeniHesap ? ForumRules.NewAccountDailyPostLimit : ForumRules.DailyPostLimit;

        var gunBasi = simdi.AddDays(-1);
        var bugun = await _db.CommunityPosts.AsNoTracking()
            .CountAsync(p => p.AuthorUserId == userId && p.CreatedAtUtc >= gunBasi, ct);

        if (bugun >= tavan)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Günlük gönderi sınırına ulaştın ({tavan}). Yarın devam edebilirsin.",
                statusCode: 429);
        }
    }

    /// <summary>
    /// Bağlantı eşiği. Arayüzdeki "Dışarıya bağlantı paylaşımı 3. seviyeden itibaren
    /// açılıyor" vaadinin karşılığı.
    ///
    /// Seviye krediden türüyor, yani ders anlatarak kazanılıyor — bağlantı paylaşmak
    /// için önce topluluğa katkı vermek gerekiyor. Spam hesabının en ucuz kazancı
    /// (link bırakmak) böylece en pahalı hâle geliyor.
    /// </summary>
    private static void BaglantiKapisi(string govde, int toplamKredi)
    {
        var baglantiVar = govde.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                          govde.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                          govde.Contains("www.", StringComparison.OrdinalIgnoreCase);

        if (!baglantiVar)
        {
            return;
        }

        var seviye = UserLevelRules.Hesapla(toplamKredi).Level;
        if (seviye < ForumRules.LinkMinLevel)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Bağlantı paylaşmak {ForumRules.LinkMinLevel}. seviyeden itibaren açılıyor " +
                $"(şu an {seviye}. seviyedesin). Kaynağın adını yazabilirsin.");
        }
    }
}

public sealed record CreateForumCommentCommand(Guid PostId, Guid AuthorUserId, string Body)
    : IRequest<Guid>;

public sealed class CreateForumCommentHandler : IRequestHandler<CreateForumCommentCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IDistributedLockProvider _locks;

    public CreateForumCommentHandler(IAppDbContext db, IClock clock, IDistributedLockProvider locks)
    {
        _db = db;
        _clock = clock;
        _locks = locks;
    }

    public async Task<Guid> Handle(CreateForumCommentCommand request, CancellationToken ct)
    {
        var govde = (request.Body ?? string.Empty).Trim();

        if (govde.Length is < ForumRules.CommentMinLength or > ForumRules.CommentMaxLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Yorum {ForumRules.CommentMinLength}-{ForumRules.CommentMaxLength} karakter olmalı.");
        }

        /*
          KİLİT İÇERİK BAZINDA: yorum eklemek gönderinin CommentCount sayacını da
          değiştiriyor. İki kişi aynı gönderiye aynı anda yorum yazarsa, kilit olmadan
          iki sayaç artışından biri kaybolur ve yorum sayısı kalıcı olarak yanlış kalır.
        */
        await using var kilit = await _locks.AcquireAsync(
            LockKeys.ForumContent(request.PostId), TimeSpan.FromSeconds(10), ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

            var gonderi = await _db.CommunityPosts
                .SingleOrDefaultAsync(p => p.Id == request.PostId, ct)
                ?? throw new AppException(ErrorCodes.PostNotFound, "Gönderi bulunamadı.", statusCode: 404);

            if (gonderi.Status == ForumContentStatus.Removed)
            {
                throw new AppException(ErrorCodes.ValidationFailed,
                    "Bu gönderi kaldırıldı; yorum yazılamaz.", statusCode: 409);
            }

            var yorum = new CommunityComment
            {
                PostId = request.PostId,
                AuthorUserId = request.AuthorUserId,
                Body = govde,
                Status = ForumContentStatus.Visible,
                CreatedAtUtc = _clock.UtcNow
            };

            _db.CommunityComments.Add(yorum);
            gonderi.CommentCount += 1;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return yorum.Id;
        }, ct: ct);
    }
}

/// <summary>Oy yönü. 0 GÖNDERİLMEZ — geri alma, aynı yöne ikinci kez oy vermektir.</summary>
public sealed record VoteForumContentCommand(
    Guid UserId, Guid? PostId, Guid? CommentId, short Value) : IRequest<ForumVoteResult>;

public sealed record ForumVoteResult(int UpvoteCount, int DownvoteCount, int MyVote);

public sealed class VoteForumContentHandler
    : IRequestHandler<VoteForumContentCommand, ForumVoteResult>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IDistributedLockProvider _locks;

    public VoteForumContentHandler(IAppDbContext db, IClock clock, IDistributedLockProvider locks)
    {
        _db = db;
        _clock = clock;
        _locks = locks;
    }

    public async Task<ForumVoteResult> Handle(VoteForumContentCommand request, CancellationToken ct)
    {
        if (request.Value is not (1 or -1))
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Oy yalnızca +1 ya da -1 olabilir.");
        }

        // TAM OLARAK biri dolu. Veritabanında da CHECK kısıtı var (CK_Votes_TekHedef);
        // buradaki kontrol kullanıcıya anlamlı hata döndürmek için.
        var hedefSayisi = (request.PostId.HasValue ? 1 : 0) + (request.CommentId.HasValue ? 1 : 0);
        if (hedefSayisi != 1)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Oy tam olarak bir içeriğe verilmeli (gönderi ya da yorum).");
        }

        var icerikId = request.PostId ?? request.CommentId!.Value;

        await using var kilit = await _locks.AcquireAsync(
            LockKeys.ForumContent(icerikId), TimeSpan.FromSeconds(10), ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

            var mevcut = await _db.CommunityVotes.SingleOrDefaultAsync(
                v => v.UserId == request.UserId &&
                     v.PostId == request.PostId &&
                     v.CommentId == request.CommentId, ct);

            /*
              ÜÇ DURUM, TEK YER:
                yok           → oy ekle
                aynı yön      → oyu GERİ AL (satırı sil)
                ters yön      → çevir (bir taraftan düş, diğerine ekle)

              Sayaç değişimi buradan türetiliyor, ayrıca hesaplanmıyor: iki yerde
              hesaplansaydı biri düzeltilirken diğeri unutulur ve sayaçlar sessizce
              kayardı.
            */
            var artiFark = 0;
            var eksiFark = 0;
            int benimOyum;

            if (mevcut is null)
            {
                _db.CommunityVotes.Add(new CommunityVote
                {
                    UserId = request.UserId,
                    PostId = request.PostId,
                    CommentId = request.CommentId,
                    Value = request.Value,
                    CreatedAtUtc = _clock.UtcNow
                });

                if (request.Value == 1) artiFark = 1; else eksiFark = 1;
                benimOyum = request.Value;
            }
            else if (mevcut.Value == request.Value)
            {
                _db.CommunityVotes.Remove(mevcut);

                if (request.Value == 1) artiFark = -1; else eksiFark = -1;
                benimOyum = 0;
            }
            else
            {
                mevcut.Value = request.Value;

                if (request.Value == 1) { artiFark = 1; eksiFark = -1; }
                else { artiFark = -1; eksiFark = 1; }
                benimOyum = request.Value;
            }

            int arti, eksi;

            if (request.PostId is { } gonderiId)
            {
                var gonderi = await _db.CommunityPosts.SingleOrDefaultAsync(p => p.Id == gonderiId, ct)
                    ?? throw new AppException(ErrorCodes.PostNotFound, "Gönderi bulunamadı.", statusCode: 404);

                gonderi.UpvoteCount += artiFark;
                gonderi.DownvoteCount += eksiFark;
                arti = gonderi.UpvoteCount;
                eksi = gonderi.DownvoteCount;
            }
            else
            {
                var yorum = await _db.CommunityComments.SingleOrDefaultAsync(c => c.Id == request.CommentId, ct)
                    ?? throw new AppException(ErrorCodes.CommentNotFound, "Yorum bulunamadı.", statusCode: 404);

                yorum.UpvoteCount += artiFark;
                yorum.DownvoteCount += eksiFark;
                arti = yorum.UpvoteCount;
                eksi = yorum.DownvoteCount;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new ForumVoteResult(arti, eksi, benimOyum);
        }, ct: ct);
    }
}
