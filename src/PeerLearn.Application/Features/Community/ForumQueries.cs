using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Community;

/*
  ══════════════════════════════════════════════════════════════════════════════
  FORUM OKUMA UÇLARI.

  Arayüz (frontend/src/pages/Topluluk.jsx) 2026-08-25'te yazıldı ve sabit veriyle
  çalışıyordu. Bu dosya o arayüzün beklediği sözleşmenin sunucu karşılığı —
  alan adları ve sıralama ölçütleri oradaki koda göre seçildi, tersi değil.
  ══════════════════════════════════════════════════════════════════════════════
*/

/// <summary>Akış sıralaması. Arayüzdeki SIRALAMALAR şeridiyle birebir.</summary>
public enum ForumSort
{
    Newest = 0,
    Top = 1,
    Controversial = 2
}

/// <summary>Tarih penceresi. Arayüzdeki ZAMAN_ARALIKLARI ile birebir.</summary>
public enum ForumRange
{
    All = 0,
    Day = 1,
    Week = 2,
    Month = 3
}

public sealed record ForumAuthorDto(
    Guid UserId,
    string DisplayName,

    /// <summary>
    /// Kişinin seviyesi (1..10) — kartta rozet olarak gösteriliyor.
    /// </summary>
    int Level,

    /// <summary>
    /// YÖNETİCİ/MODERATÖR İŞARETİ (ürün sahibi kararı, 2026-08-27).
    ///
    /// Bu alan forum ve Keşfet DIŞINDA hiçbir DTO'da yok: profil ucu rolü hâlâ
    /// sızdırmıyor. Gerekçe ürün tarafında: kullanıcılar forumda platformla ilgili
    /// soru soracak ve resmi cevabın hangisi olduğu ayırt edilebilmeli. Sıradan bir
    /// kullanıcının "ben yöneticiyim" demesiyle gerçek yöneticinin cevabı aynı
    /// görünürse, yanıltma en kolay saldırı olur.
    ///
    /// Sunucudan geliyor, istemci hesaplamıyor: rol istemcide türetilseydi, tarayıcı
    /// tarafında değiştirilerek sahte "yönetici" rozeti üretilebilirdi.
    /// </summary>
    bool IsStaff);

public sealed record ForumPostDto(
    Guid PostId,
    ForumTag Tag,
    string Title,
    string Body,
    ForumAuthorDto Author,
    DateTime CreatedAtUtc,
    int UpvoteCount,
    int DownvoteCount,
    int CommentCount,

    /// <summary>İsteği yapanın oyu: 1, -1 ya da 0 (oy vermemiş).</summary>
    int MyVote,

    /// <summary>
    /// Şikayet eşiğini geçtiği için perdelenmiş mi. Arayüz bunu "yine de göster"
    /// düğmesiyle açıyor; içerik SİLİNMİYOR (bkz. Domain/Community/Forum.cs).
    /// </summary>
    bool UnderReview,

    int ReportCount);

public sealed record ForumCommentDto(
    Guid CommentId,
    string Body,
    ForumAuthorDto Author,
    DateTime CreatedAtUtc,
    int UpvoteCount,
    int MyVote,
    bool UnderReview);

public sealed record GetForumFeedQuery(
    Guid CurrentUserId,
    ForumSort Sort,
    ForumRange Range,
    ForumTag? Tag,
    int Page,
    int PageSize) : IRequest<PagedResult<ForumPostDto>>;

public sealed class GetForumFeedHandler
    : IRequestHandler<GetForumFeedQuery, PagedResult<ForumPostDto>>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetForumFeedHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<ForumPostDto>> Handle(
        GetForumFeedQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        /*
          ⚠️ FİLTRE `Status == Visible` OLARAK YAZILMAK ZORUNDA.

          Kısmi index'ler (IX_Posts_GorunurTarih, IX_Posts_GorunurEtiketTarih)
          "Status = 'Visible'" filtresi taşıyor ve CLAUDE.md'nin uyardığı gibi:
          filtreli bir index, sorgunun WHERE'i o koşulu BİREBİR içermedikçe
          kullanılmaz. "Status != Removed" gibi bir yazım index'i sessizce devre
          dışı bırakır ve akış tablo taramasına düşer.

          İncelemedeki (UnderReview) gönderiler AKIŞTA GÖRÜNÜYOR ama perdeli —
          bu yüzden sorgu Visible ve UnderReview'ü birlikte alıyor. İkisini tek
          index'le karşılamak mümkün değil; UnderReview satırları azınlıkta
          olduğu için ikinci koşul ucuz.
        */
        var temel = _db.CommunityPosts.AsNoTracking()
            .Where(p => p.Status == ForumContentStatus.Visible ||
                        p.Status == ForumContentStatus.UnderReview);

        if (request.Tag is { } etiket)
        {
            temel = temel.Where(p => p.Tag == etiket);
        }

        var sinir = PencereBaslangici(request.Range, _clock.UtcNow);
        if (sinir is { } baslangic)
        {
            temel = temel.Where(p => p.CreatedAtUtc >= baslangic);
        }

        var toplam = await temel.CountAsync(ct);
        if (toplam == 0)
        {
            return PagedResult<ForumPostDto>.Empty(page, pageSize);
        }

        /*
          SIRALAMA VERİTABANINDA — belleğe çekip sıralamak, sayfalamayı anlamsız
          kılardı (ikinci sayfayı verebilmek için tüm gönderileri çekmek gerekirdi).

          TARTIŞMALI FORMÜLÜ: (artı + eksi) * min/max. Arayüzdeki tartismaPuani
          fonksiyonunun birebir karşılığı. Fark (artı − eksi) DEĞİL: 184/6 ile
          95/89 benzer farkı verir ama biri fikir birliği, diğeri kavga. Tek yönlü
          gönderiler (bir taraf sıfır) 0 alıyor — kimse karşı çıkmadan tartışmalı
          olunmaz.

          Sıfıra bölme koruması: max(1, ...). SQL tarafında 0'a bölme, sorgunun
          tamamını hataya düşürürdü.
        */
        var sirali = request.Sort switch
        {
            ForumSort.Top => temel.OrderByDescending(p => p.UpvoteCount - p.DownvoteCount)
                                  .ThenByDescending(p => p.CreatedAtUtc),

            ForumSort.Controversial => temel
                .OrderByDescending(p =>
                    (p.UpvoteCount == 0 || p.DownvoteCount == 0)
                        ? 0d
                        : (p.UpvoteCount + p.DownvoteCount) *
                          ((double)Math.Min(p.UpvoteCount, p.DownvoteCount) /
                           Math.Max(1, Math.Max(p.UpvoteCount, p.DownvoteCount))))
                .ThenByDescending(p => p.CreatedAtUtc),

            _ => temel.OrderByDescending(p => p.CreatedAtUtc)
        };

        var sayfa = await sirali
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(_db.Users.AsNoTracking(), p => p.AuthorUserId, u => u.Id, (p, u) => new { p, u })
            .Select(x => new
            {
                x.p,
                x.u.DisplayName,
                x.u.TotalEarnedCredits,
                x.u.Role
            })
            .ToListAsync(ct);

        /*
          KENDİ OYUM tek sorguda toplanıyor, gönderi başına bir sorgu ile DEĞİL:
          20 gönderilik bir sayfa 20 ek gidiş gelişe (N+1) dönerdi.
        */
        var idler = sayfa.Select(x => x.p.Id).ToList();
        var oylarim = await _db.CommunityVotes.AsNoTracking()
            .Where(v => v.UserId == request.CurrentUserId && v.PostId != null && idler.Contains(v.PostId!.Value))
            .ToDictionaryAsync(v => v.PostId!.Value, v => (int)v.Value, ct);

        var ogeler = sayfa.Select(x => new ForumPostDto(
            x.p.Id,
            x.p.Tag,
            x.p.Title,
            x.p.Body,
            new ForumAuthorDto(
                x.p.AuthorUserId,
                x.DisplayName,
                UserLevelRules.Hesapla(x.TotalEarnedCredits).Level,
                x.Role is UserRole.Admin or UserRole.Moderator),
            x.p.CreatedAtUtc,
            x.p.UpvoteCount,
            x.p.DownvoteCount,
            x.p.CommentCount,
            oylarim.TryGetValue(x.p.Id, out var oy) ? oy : 0,
            x.p.Status == ForumContentStatus.UnderReview,
            x.p.ReportCount)).ToList();

        return new PagedResult<ForumPostDto>(ogeler, toplam, page, pageSize);
    }

    /// <summary>Pencerenin başlangıcı; All ise null (filtre uygulanmaz).</summary>
    private static DateTime? PencereBaslangici(ForumRange aralik, DateTime simdi) => aralik switch
    {
        ForumRange.Day => simdi.AddDays(-1),
        ForumRange.Week => simdi.AddDays(-7),
        ForumRange.Month => simdi.AddDays(-30),
        _ => null
    };
}

public sealed record GetForumCommentsQuery(Guid PostId, Guid CurrentUserId)
    : IRequest<IReadOnlyList<ForumCommentDto>>;

/// <summary>
/// Bir gönderinin yorumları. Sayfalama YOK ve bu bilinçli: yorumlar gönderinin
/// içinde açılıyor (ayrı sayfa yok) ve bir gönderideki yorum sayısı doğal olarak
/// sınırlı. Sayfalama gerekirse akıştaki kalıp buraya taşınır.
/// </summary>
public sealed class GetForumCommentsHandler
    : IRequestHandler<GetForumCommentsQuery, IReadOnlyList<ForumCommentDto>>
{
    private readonly IAppDbContext _db;

    public GetForumCommentsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ForumCommentDto>> Handle(
        GetForumCommentsQuery request, CancellationToken ct)
    {
        var satirlar = await _db.CommunityComments.AsNoTracking()
            .Where(c => c.PostId == request.PostId &&
                        (c.Status == ForumContentStatus.Visible ||
                         c.Status == ForumContentStatus.UnderReview))
            .OrderBy(c => c.CreatedAtUtc)
            .Join(_db.Users.AsNoTracking(), c => c.AuthorUserId, u => u.Id, (c, u) => new { c, u })
            .Select(x => new
            {
                x.c,
                x.u.DisplayName,
                x.u.TotalEarnedCredits,
                x.u.Role
            })
            .ToListAsync(ct);

        var idler = satirlar.Select(x => x.c.Id).ToList();
        var oylarim = await _db.CommunityVotes.AsNoTracking()
            .Where(v => v.UserId == request.CurrentUserId && v.CommentId != null && idler.Contains(v.CommentId!.Value))
            .ToDictionaryAsync(v => v.CommentId!.Value, v => (int)v.Value, ct);

        return satirlar.Select(x => new ForumCommentDto(
            x.c.Id,
            x.c.Body,
            new ForumAuthorDto(
                x.c.AuthorUserId,
                x.DisplayName,
                UserLevelRules.Hesapla(x.TotalEarnedCredits).Level,
                x.Role is UserRole.Admin or UserRole.Moderator),
            x.c.CreatedAtUtc,
            x.c.UpvoteCount,
            oylarim.TryGetValue(x.c.Id, out var oy) ? oy : 0,
            x.c.Status == ForumContentStatus.UnderReview)).ToList();
    }
}
