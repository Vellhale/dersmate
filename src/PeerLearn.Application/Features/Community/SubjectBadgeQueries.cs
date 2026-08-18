using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Community;

namespace PeerLearn.Application.Features.Community;

/// <summary>Bir kullanıcının branş rozetleri — profil ekranı için.</summary>
public sealed record GetSubjectBadgesQuery(Guid UserId) : IRequest<SubjectBadgesDto>;

/// <param name="Branch">Enum'ın metin karşılığı (Matematik, Cografya…). İstemci ikon eşlemesi buna bakar.</param>
/// <param name="Subject">Kullanıcıya gösterilecek ders adı ("Coğrafya").</param>
/// <param name="Level">Cirak / Usta / Ustad.</param>
/// <param name="Title">Hazır başlık: "Matematik Çırağı". Metni istemcide kurmuyoruz ki
/// Türkçe ekler (Çırağı / Ustası / Üstadı) tek yerde kalsın.</param>
/// <param name="Hours">Rozetin gerektirdiği saat (5 / 20 / 50) — rozet altındaki etiket.</param>
public sealed record SubjectBadgeRow(
    string Branch, string Subject, string Level, string Title, int Hours, DateTime EarnedAtUtc);

/// <param name="Badges">Kazanılmış rozetler. En yüksek seviye önce, sonra en çok saat.</param>
/// <param name="Progress">
/// Henüz rozet çıkmamış branşlar dahil, o branşta anlatılan toplam saat. Profil "az kaldı"
/// göstergesi kurabilsin diye ayrı dönüyor; boş liste normaldir.
/// </param>
public sealed record SubjectBadgesDto(
    IReadOnlyList<SubjectBadgeRow> Badges, IReadOnlyList<SubjectProgressRow> Progress);

public sealed record SubjectProgressRow(string Branch, string Subject, int Hours, int Minutes);

public sealed class GetSubjectBadgesHandler : IRequestHandler<GetSubjectBadgesQuery, SubjectBadgesDto>
{
    private readonly IAppDbContext _db;
    private readonly SubjectBadgeEngine _engine;

    public GetSubjectBadgesHandler(IAppDbContext db, SubjectBadgeEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<SubjectBadgesDto> Handle(GetSubjectBadgesQuery request, CancellationToken ct)
    {
        /*
          SIRALAMA BELLEKTE — SQL'de DEĞİL, ve bu bilinçli.

          `Level` veritabanında METİN olarak saklanıyor (HasConversion<string>). Sunucuda
          `ORDER BY "Level" DESC` demek, sayısal seviyeye göre değil ALFABETİK sıralamak
          demek. Bugün tesadüfen doğru sonuç veriyor ("Ustad" > "Usta" > "Cirak"), ama bu
          bir tesadüf: enum üyesi yarın "Kidemli" diye yeniden adlandırılırsa sıralama
          sessizce bozulur ve kimse fark etmez. Bellekte enum değerine göre sıralamak,
          niyeti koda yazıyor.

          Maliyet yok: bir kullanıcının en fazla 8 branş × 3 seviye = 24 rozeti olabilir.
        */
        var ham = await _db.UserSubjectBadges.AsNoTracking()
            .Where(b => b.UserId == request.UserId)
            .Select(b => new { b.Branch, b.Level, b.MinutesAtAward, b.EarnedAtUtc })
            .ToListAsync(ct);

        var rozetler = ham
            .OrderByDescending(b => b.Level)
            .ThenByDescending(b => b.MinutesAtAward)
            .ToList();

        /*
          İLERLEME MOTORDAN OKUNUYOR, ayrı bir sorgu yazılmıyor: "bir branşta kaç dakika
          anlatıldı" tanımı tek yerde kalmalı. İki ayrı sorgu olsaydı, biri Disputed'ı
          sayıp diğeri saymadığında profil rozetle çelişen bir saat gösterirdi.
        */
        var sureler = await _engine.GetTaughtMinutesAsync(request.UserId, ct);

        var satirlar = rozetler
            .Select(b => new SubjectBadgeRow(
                b.Branch.ToString(),
                Ders(b.Branch),
                b.Level.ToString(),
                SubjectBadgeRules.Title(Ders(b.Branch), b.Level),
                SubjectBadgeRules.RequiredMinutes(b.Level) / 60,
                b.EarnedAtUtc))
            .ToList();

        var ilerleme = sureler
            .OrderByDescending(s => s.Minutes)
            .Select(s => new SubjectProgressRow(s.Branch.ToString(), Ders(s.Branch), s.Minutes / 60, s.Minutes))
            .ToList();

        return new SubjectBadgesDto(satirlar, ilerleme);
    }

    private static string Ders(SubjectBranch branch) => SubjectBranchNames.Of(branch);
}
