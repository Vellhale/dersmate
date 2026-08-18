using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;

namespace PeerLearn.Application.Features.Community;

/// <summary>
/// Öğretmen adaylığı için öğrenci belgesi yükler (PDF ya da görsel).
/// </summary>
/// <remarks>
/// NEDEN AYRI KOMUT, BEYANIN İÇİNDE DEĞİL: beyan metni (üniversite/bölüm/sınıf) küçük bir
/// JSON, belge ise 10 MB'a kadar bir dosya. Tek uçta birleştirilseydi kullanıcı bir alanı
/// düzeltmek için belgeyi her seferinde yeniden yüklemek zorunda kalırdı.
///
/// BELGE DEĞİŞTİRMEK KARARI SIFIRLAR. Yeni belge yüklenince önceki doğrulama/ret kararı
/// düşer ve beyan yeniden kuyruğa girer. Aksi halde doğrulanmış bir aday belgesini
/// değiştirip "doğrulanmış" rozetini taşımaya devam edebilirdi — doğrulamanın dayanağı
/// artık ortada olmayan bir belge olurdu.
/// </remarks>
public sealed record UploadTeacherDocumentCommand(
    Guid UserId,
    Stream Content,
    string ContentType,
    long SizeBytes) : IRequest;

public sealed class UploadTeacherDocumentHandler : IRequestHandler<UploadTeacherDocumentCommand>
{
    private const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>
    /// PDF de kabul ediliyor — öğrenci belgesi çoğu üniversitede e-Devlet PDF'i olarak
    /// indiriliyor. Ders kanıtında yalnızca görsel kabul ediliyordu; orada amaç ekran
    /// görüntüsüydü, burada resmî bir belge.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp"
    };

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IProofStorage _storage;

    public UploadTeacherDocumentHandler(IAppDbContext db, IClock clock, IProofStorage storage)
    {
        _db = db;
        _clock = clock;
        _storage = storage;
    }

    public async Task Handle(UploadTeacherDocumentCommand request, CancellationToken ct)
    {
        if (request.SizeBytes is <= 0 or > MaxBytes)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Belge boş veya 10 MB'tan büyük.");
        }

        if (!AllowedContentTypes.TryGetValue(request.ContentType, out var uzanti))
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Yalnızca PDF, PNG, JPEG veya WebP kabul edilir.");
        }

        var profil = await _db.TeacherCandidateProfiles
                         .SingleOrDefaultAsync(p => p.UserId == request.UserId, ct)
                     ?? throw new AppException(ErrorCodes.ValidationFailed,
                         "Önce öğretmen adaylığı beyanını doldur, sonra belgeyi yükle.");

        using var tampon = new MemoryStream();
        await request.Content.CopyToAsync(tampon, ct);
        if (tampon.Length != request.SizeBytes || tampon.Length > MaxBytes)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Belge boyutu geçersiz.");
        }

        tampon.Position = 0;
        var anahtar = await _storage.SaveAsync(tampon, uzanti, ct);

        profil.DocumentStorageKey = anahtar;
        profil.DocumentContentType = request.ContentType.ToLowerInvariant();
        profil.DocumentUploadedAtUtc = _clock.UtcNow;

        /*
          Karar sıfırlanır ve beyan yeniden kuyruğa girer (gerekçe sınıf notunda).
          DeclaredAtUtc de tazeleniyor: kuyruk bu alana göre sıralanıyor, eski tarih
          kalsaydı yeni belge listenin dibinde kaybolurdu.
        */
        profil.VerifiedAtUtc = null;
        profil.VerifiedByAdminId = null;
        profil.RejectedAtUtc = null;
        profil.RejectedByAdminId = null;
        profil.DeclaredAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Belgeyi okur. Yalnızca sahibi ve moderasyon çağırabilir.</summary>
public sealed record GetTeacherDocumentQuery(Guid ProfileId, Guid RequesterUserId, bool AsModerator)
    : IRequest<TeacherDocumentDto>;

public sealed record TeacherDocumentDto(byte[] Content, string ContentType);

public sealed class GetTeacherDocumentHandler
    : IRequestHandler<GetTeacherDocumentQuery, TeacherDocumentDto>
{
    private readonly IAppDbContext _db;
    private readonly IProofStorage _storage;

    public GetTeacherDocumentHandler(IAppDbContext db, IProofStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<TeacherDocumentDto> Handle(GetTeacherDocumentQuery request, CancellationToken ct)
    {
        var profil = await _db.TeacherCandidateProfiles.AsNoTracking()
                         .SingleOrDefaultAsync(p => p.Id == request.ProfileId, ct)
                     ?? throw new AppException(ErrorCodes.UserNotFound, "Beyan bulunamadı.", statusCode: 404);

        // Belge kişisel veri: sahibi ya da moderasyon dışında kimse göremez.
        if (!request.AsModerator && profil.UserId != request.RequesterUserId)
        {
            throw new AppException(ErrorCodes.NotAuthorized, "Bu belgeye erişemezsin.", statusCode: 403);
        }

        if (profil.DocumentStorageKey is null)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Bu beyana belge yüklenmemiş.", statusCode: 404);
        }

        var akis = await _storage.OpenAsync(profil.DocumentStorageKey, ct)
                   ?? throw new AppException(ErrorCodes.ValidationFailed,
                       "Belge dosyası bulunamadı.", statusCode: 404);

        using (akis)
        {
            using var tampon = new MemoryStream();
            await akis.CopyToAsync(tampon, ct);
            return new TeacherDocumentDto(tampon.ToArray(), profil.DocumentContentType ?? "application/octet-stream");
        }
    }
}
