using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <summary>
/// Engelleme sorgusunun TEK KAYNAĞI. Her çağıran kendi <c>Where</c>'ini yazsaydı biri
/// er ya da geç tek yönlü yazar ve engelleme o yolda sessizce delinirdi.
/// </summary>
public static class EngelSorgusu
{
    /// <summary>
    /// İki kullanıcı arasında HERHANGİ BİR YÖNDE engel var mı?
    /// </summary>
    /// <remarks>
    /// ⚠️ ÇİFT YÖNLÜ OLMAK ZORUNDA. Yalnızca "A, B'yi engelledi mi" diye sorulsaydı,
    /// rahatsız eden taraf istek göndermeye devam edebilirdi — engelleme, kendisinden
    /// korunmak için kullanılan kişiye karşı işlevsiz kalırdı.
    /// </remarks>
    public static Task<bool> VarMiAsync(IAppDbContext db, Guid a, Guid b, CancellationToken ct) =>
        db.UserBlocks.AnyAsync(
            x => (x.BlockerUserId == a && x.BlockedUserId == b) ||
                 (x.BlockerUserId == b && x.BlockedUserId == a), ct);
}

public sealed record BlockUserCommand(Guid BlockerUserId, Guid BlockedUserId, string? Note)
    : IRequest<Unit>;

/// <summary>
/// Kullanıcıyı engeller. İdempotent: zaten engelliyse sessizce başarılı döner.
/// </summary>
/// <remarks>
/// İDEMPOTENS BİLİNÇLİ, <c>CloseMatch</c> ile aynı gerekçe: kullanıcı düğmeye iki kez
/// bastığında ya da iki sekmeden aynı anda engellediğinde hata görmesinin hiçbir faydası
/// yok — istediği sonuç zaten gerçekleşmiş durumda.
///
/// ⚠️ BEKLEYEN İSTEKLER DE KAPATILIYOR. Yalnızca satır yazmak yetmezdi: engellemeden
/// önce gönderilmiş bekleyen bir istek kabul edilebilir durumda kalır, kabul edilince
/// sohbet açılır ve engel daha kurulduğu gün delinirdi.
///
/// ⚠️ KABUL EDİLMİŞ EŞLEŞMEYE DOKUNULMUYOR — ve bu bir eksiklik değil, ölçülmüş bir
/// sınır. Eşleşmeyi kapatmak CloseMatch'in muhafızını (sonuçlanmamış ders varsa
/// kapatma) atlardı ve ortada duran bir puan/onay/itiraz işlemini sahipsiz bırakırdı.
/// Açık eşleşmenin iletişimi başka yerden kesiliyor:
///   • yazma  → SendMessageHandler (engel varsa 403)
///   • yeni ders → BookSessionHandler (engel varsa 409)
/// Üçü BİRLİKTE "engelleme iletişimi keser" sözünü tutuyor; biri kaldırılırsa engel delinir.
/// </remarks>
public sealed class BlockUserHandler : IRequestHandler<BlockUserCommand, Unit>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public BlockUserHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(BlockUserCommand request, CancellationToken ct)
    {
        if (request.BlockerUserId == request.BlockedUserId)
        {
            throw new AppException(ErrorCodes.SelfMatch, "Kendini engelleyemezsin.");
        }

        var hedef = await _db.Users
            .AnyAsync(u => u.Id == request.BlockedUserId && u.Status != UserStatus.Deleted, ct);

        if (!hedef)
        {
            throw new AppException(ErrorCodes.UserNotFound, "Kullanıcı bulunamadı.", statusCode: 404);
        }

        var zatenVar = await _db.UserBlocks.AnyAsync(
            x => x.BlockerUserId == request.BlockerUserId &&
                 x.BlockedUserId == request.BlockedUserId, ct);

        if (!zatenVar)
        {
            _db.UserBlocks.Add(new UserBlock
            {
                BlockerUserId = request.BlockerUserId,
                BlockedUserId = request.BlockedUserId,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            });
        }

        /*
          ⛔ BEKLEYEN İSTEKLERİ KAPAT — engellemenin ayrılmaz parçası.

          İki yön de kapatılıyor: hem engellenenin gönderdiği hem engelleyenin gönderdiği
          bekleyen istekler. Tek yön kapatılsaydı, engelleyen kişinin daha önce gönderdiği
          istek karşı tarafta durmaya devam eder ve kabul edilince sohbet açılırdı.

          Durum Declined yazılıyor, satır SİLİNMİYOR: geçmiş kayıt kalmalı ve
          MatchStatus'ta "engellendi" diye bir üye açmak, enum'ları metin olarak saklayan
          bu şemada gereksiz bir göç riski olurdu (CLAUDE.md — üye eklemek göç
          gerektirmiyor ama okuma anında patlayan bir kaldırma riski doğuyor).
        */
        var bekleyenler = await _db.Matches
            .Where(m => m.Status == Domain.Matchmaking.MatchStatus.Pending &&
                        ((m.InitiatorUserId == request.BlockerUserId && m.ResponderUserId == request.BlockedUserId) ||
                         (m.InitiatorUserId == request.BlockedUserId && m.ResponderUserId == request.BlockerUserId)))
            .ToListAsync(ct);

        foreach (var m in bekleyenler)
        {
            m.Status = Domain.Matchmaking.MatchStatus.Declined;
            m.RespondedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public sealed record UnblockUserCommand(Guid BlockerUserId, Guid BlockedUserId) : IRequest<Unit>;

/// <summary>Engeli kaldırır. İdempotent — engel yoksa da başarılı döner.</summary>
/// <remarks>
/// ⚠️ ENGEL KALDIRMAK, KAPATILAN İSTEKLERİ GERİ GETİRMİYOR. Reddedilmiş bir istek
/// reddedilmiş kalıyor; taraflar isterlerse yeniden istek gönderir. Otomatik geri
/// getirme, kullanıcının bir zamanlar reddettiği bir isteği haberi olmadan yeniden
/// açardı.
/// </remarks>
public sealed class UnblockUserHandler : IRequestHandler<UnblockUserCommand, Unit>
{
    private readonly IAppDbContext _db;

    public UnblockUserHandler(IAppDbContext db) => _db = db;

    public async Task<Unit> Handle(UnblockUserCommand request, CancellationToken ct)
    {
        var kayit = await _db.UserBlocks.SingleOrDefaultAsync(
            x => x.BlockerUserId == request.BlockerUserId &&
                 x.BlockedUserId == request.BlockedUserId, ct);

        if (kayit is not null)
        {
            _db.UserBlocks.Remove(kayit);
            await _db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}

/// <param name="UserId">Engellenen kullanıcı.</param>
public sealed record BlockedUserDto(Guid UserId, string DisplayName, string? Note, DateTime BlockedAtUtc);

public sealed record GetMyBlocksQuery(Guid UserId) : IRequest<IReadOnlyList<BlockedUserDto>>;

/// <summary>
/// Kullanıcının engellediklerinin listesi — YALNIZCA kendi engelledikleri.
/// </summary>
/// <remarks>
/// "Beni kimler engelledi" diye bir sorgu BİLEREK YOK ve eklenmemeli: o liste,
/// engellemeyi misillemeye çevirirdi.
/// </remarks>
public sealed class GetMyBlocksHandler : IRequestHandler<GetMyBlocksQuery, IReadOnlyList<BlockedUserDto>>
{
    private readonly IAppDbContext _db;

    public GetMyBlocksHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<BlockedUserDto>> Handle(GetMyBlocksQuery request, CancellationToken ct)
        => await _db.UserBlocks
            .Where(x => x.BlockerUserId == request.UserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Join(_db.Users, x => x.BlockedUserId, u => u.Id,
                (x, u) => new BlockedUserDto(u.Id, u.DisplayName, x.Note, x.CreatedAtUtc))
            .ToListAsync(ct);
}
