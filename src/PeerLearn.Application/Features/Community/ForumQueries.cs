using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Identity;
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

    int ReportCount,

    /// <summary>
    /// Kartın altındaki ilk yorum önizlemesi (2026-09-26); yoksa null. Kurallar
    /// <see cref="ForumOnizleme"/>'de. SONA ve varsayılan değerle eklendi: konumsal kurucu
    /// kırılmıyor, alanı tanımayan eski istemci onu yok sayıyor.
    /// </summary>
    ForumCommentPreviewDto? FirstComment = null);

/// <summary>
/// Akış kartındaki ilk yorum. Oy ve perde alanı YOK: önizleme yalnızca görünür (Visible)
/// yorumdan kurulur ve etkileşim ipliğin içinde (GetForumCommentsHandler) yapılır.
/// </summary>
public sealed record ForumCommentPreviewDto(
    Guid CommentId,

    /// <summary>
    /// Tek satıra indirgenmiş gövde (satır sonu ve art arda boşluk → tek boşluk), en fazla
    /// <see cref="ForumOnizleme.EnFazlaGrafem"/> grafem; kesildiyse "…" ile biter.
    /// </summary>
    string Body,

    ForumAuthorDto Author,
    DateTime CreatedAtUtc);

/// <summary>
/// Akıştaki ilk yorum önizlemesinin kuralları.
/// </summary>
/// <remarks>
/// HANGİ YORUM: gönderinin yazılma sırasına göre İLK yorumu (iplikteki sırayla aynı ölçüt,
/// <see cref="GetForumCommentsHandler"/>), şu üçü atlanarak:
///  • İncelemedeki (UnderReview) ve kaldırılmış (Removed) yorum — sorgu koşulu BİREBİR
///    <c>Status == Visible</c>. İplikte perdeli görünen içerik akışta perdesiz görünmemeli;
///    ayrıca kısmi index IX_Comments_GorunurGonderiTarih ancak bu yazımla kullanılıyor.
///  • Bakanla arasında HERHANGİ BİR YÖNDE engel olan kişinin yorumu
///    (<see cref="EngelSorgusu.KisisiEngelsiz"/>). Akış ve iplik bugün engele göre
///    SÜZMÜYOR; önizleme ise kullanıcının önüne kendiliğinden çıkan tek yorum, engellenen
///    kişinin sözü oraya itilmemeli. Engelli yorum atlanır, sıradaki görünür yorum gelir.
///  • Perdeli (UnderReview) GÖNDERİNİN önizlemesi hiç kurulmaz (null): perdenin altındaki
///    tartışma, perde açılmadan akışta görünmemeli.
///
/// SAYI: kart "{n} yorumun tümünü gör" için gönderinin <c>CommentCount</c>'unu kullanır;
/// önizlemede atlanan yorumlar o sayıda VAR (sayaç yalnızca artıyor, ForumCommands.cs).
/// </remarks>
public static class ForumOnizleme
{
    /// <summary>Önizleme gövdesinin üst sınırı (grafem). Kart iki satır gösteriyor.</summary>
    public const int EnFazlaGrafem = 200;

    private const string UcNokta = "…";

    /// <summary>
    /// Gövdeyi tek satıra indirger ve <see cref="EnFazlaGrafem"/>'de keser. Grafem = kullanıcının
    /// tek karakter gördüğü birim; emoji ve birleşik harf ortadan bölünmez.
    /// </summary>
    public static string Metin(string govde)
    {
        var tekSatir = string.Join(' ',
            govde.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var bilgi = new StringInfo(tekSatir);
        if (bilgi.LengthInTextElements <= EnFazlaGrafem)
        {
            return tekSatir;
        }

        return bilgi.SubstringByTextElements(0, EnFazlaGrafem - 1).TrimEnd() + UcNokta;
    }
}

public sealed record ForumCommentDto(
    Guid CommentId,
    string Body,
    ForumAuthorDto Author,
    DateTime CreatedAtUtc,
    int UpvoteCount,

    /// <summary>
    /// Eksi oy sayısı. GÖNDERİLMEK ZORUNDA, "yorumlar zaten az eksi oy alır" diye
    /// atlanamaz: arayüz yorum puanını (artı − eksi) diye hesaplıyor ve oy ucu
    /// (VoteForumContentHandler) her yanıtta İKİ sayacı birden döndürüyor. Alan
    /// eksik olsaydı istemci ilk çizimde eksiyi bilmeden puan yazar, oy verilince
    /// sunucudan gelen gerçek sayıyla puan birden sıçrardı.
    /// </summary>
    int DownvoteCount,

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

        // Perdeli gönderinin önizlemesi kurulmaz (ForumOnizleme); sorguya yalnızca görünürler girer.
        var gorunurIdler = sayfa
            .Where(x => x.p.Status == ForumContentStatus.Visible)
            .Select(x => x.p.Id)
            .ToList();
        var ilkYorumlar = await IlkYorumlarAsync(gorunurIdler, request.CurrentUserId, ct);

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
            x.p.ReportCount,
            ilkYorumlar.GetValueOrDefault(x.p.Id))).ToList();

        return new PagedResult<ForumPostDto>(ogeler, toplam, page, pageSize);
    }

    /// <summary>
    /// Sayfadaki gönderilerin ilk yorumları — sayfa başına TEK sorgu (gönderi başına bir
    /// sorgu 20 kartlık sayfada N+1 olurdu). Hangi yorumun seçildiği: <see cref="ForumOnizleme"/>.
    /// </summary>
    /// <remarks>
    /// EF bu sorguyu LATERAL'e değil pencere fonksiyonuna çeviriyor: ROW_NUMBER() OVER
    /// (PARTITION BY "PostId" ORDER BY "CreatedAtUtc"), ilk satır, gönderilere LEFT JOIN.
    ///
    /// ⚠️ <c>gonderiIdler.Contains(c.PostId)</c> süzgeci ADAY YORUMLARIN kendisinde olmak
    /// ZORUNDA; ilişki koşulu (<c>c.PostId == p.Id</c>) tek başına yetmiyor. İlk yazımda
    /// yalnızca o vardı ve EF, PostId süzgeci İÇERİDE OLMAYAN bir pencere alt sorgusu
    /// üretti: her akış isteği sitedeki TÜM görünür yorumları numaralıyordu (üretilen SQL'de
    /// görüldü, 2026-09-26). PostgreSQL birleşim koşulunu pencere fonksiyonlu alt sorgunun
    /// içine itmiyor.
    ///
    /// ⚠️ Koşul <c>Status == Visible</c> olarak kalmalı: "!= Removed" gibi bir yazım hem
    /// perdeli yorumu önizlemeye sokar hem kısmi index'i (IX_Comments_GorunurGonderiTarih:
    /// PostId, CreatedAtUtc WHERE Status = 'Visible') sessizce devre dışı bırakır. O index
    /// PostId = ANY(...) ile yalnızca sayfadaki gönderilerin görünür yorumlarını okutuyor.
    ///
    /// Sıralamaya ikincil anahtar (Id) bilerek EKLENMEDİ: iplik de yalnızca CreatedAtUtc'ye
    /// göre sıralıyor (GetForumCommentsHandler) ve index o sırayı veriyor.
    /// </remarks>
    private async Task<Dictionary<Guid, ForumCommentPreviewDto>> IlkYorumlarAsync(
        List<Guid> gonderiIdler, Guid bakan, CancellationToken ct)
    {
        if (gonderiIdler.Count == 0)
        {
            return [];
        }

        var adaylar = EngelSorgusu.KisisiEngelsiz(
            _db,
            _db.CommunityComments.AsNoTracking()
                .Where(c => c.Status == ForumContentStatus.Visible && gonderiIdler.Contains(c.PostId)),
            c => c.AuthorUserId,
            bakan);

        // Gönderiden yola çıkılıyor: EF bunu "Posts LEFT JOIN (pencereli aday yorumlar)"
        // olarak TEK yorum taramasına çeviriyor. (GroupBy + First aynı sonucu iki tarama ile
        // veriyordu: grup anahtarları için bir, pencere için bir.)
        var satirlar = await _db.CommunityPosts.AsNoTracking()
            .Where(p => gonderiIdler.Contains(p.Id))
            .Select(p => new
            {
                GonderiId = p.Id,
                Yorum = adaylar
                    .Where(c => c.PostId == p.Id)
                    .Join(_db.Users.AsNoTracking(), c => c.AuthorUserId, u => u.Id, (c, u) => new
                    {
                        c.Id,
                        c.Body,
                        c.AuthorUserId,
                        c.CreatedAtUtc,
                        u.DisplayName,
                        u.TotalEarnedCredits,
                        u.Role
                    })
                    .OrderBy(x => x.CreatedAtUtc)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        // Seviye ve metin BELLEKTE: UserLevelRules.Hesapla ile ForumOnizleme.Metin C#
        // fonksiyonu, projeksiyonun içinde SQL'e çevrilemezler.
        return satirlar
            .Where(s => s.Yorum is not null)
            .ToDictionary(
                s => s.GonderiId,
                s => new ForumCommentPreviewDto(
                    s.Yorum!.Id,
                    ForumOnizleme.Metin(s.Yorum.Body),
                    new ForumAuthorDto(
                        s.Yorum.AuthorUserId,
                        s.Yorum.DisplayName,
                        UserLevelRules.Hesapla(s.Yorum.TotalEarnedCredits).Level,
                        s.Yorum.Role is UserRole.Admin or UserRole.Moderator),
                    s.Yorum.CreatedAtUtc));
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
            x.c.DownvoteCount,
            oylarim.TryGetValue(x.c.Id, out var oy) ? oy : 0,
            x.c.Status == ForumContentStatus.UnderReview)).ToList();
    }
}
