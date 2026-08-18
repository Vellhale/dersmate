using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Community;

// ---------------------------------------------------------------------------
// Profil metin alanları
// ---------------------------------------------------------------------------

public sealed record UpdateProfileCommand(
    Guid UserId,
    string DisplayName,
    string? Bio,
    string? University,
    string? Department) : IRequest;

public sealed class UpdateProfileHandler : IRequestHandler<UpdateProfileCommand>
{
    private readonly IAppDbContext _db;

    public UpdateProfileHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateProfileCommand request, CancellationToken ct)
    {
        var name = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length is < 2 or > 100)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Görünen ad 2-100 karakter olmalı.");
        }

        var user = await _db.Users.SingleAsync(u => u.Id == request.UserId, ct);

        user.DisplayName = name;
        user.Bio = Kirp(request.Bio, 1000);
        user.University = Kirp(request.University, 150);
        user.Department = Kirp(request.Department, 150);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Boş dize null'a çevrilir: "girilmemiş" ile "boş bırakılmış" aynı şeydir.</summary>
    private static string? Kirp(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}

// ---------------------------------------------------------------------------
// Rozet vitrini — KALDIRILDI
//
// UpdateFeaturedBadgesCommand/Handler ve PUT /api/profile/featured-badges ucu, başarı
// rozetlerinin emekliliğiyle birlikte silindi. Vitrin "çok rozet arasından üçünü seç"
// problemini çözüyordu; geriye seçilecek tek rozet (🌱 öğretmen adayı) kalınca hem
// problem hem çözüm ortadan kalktı. Uç zaten hiçbir ekrandan çağrılmıyordu.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Profil fotoğrafı
// ---------------------------------------------------------------------------

public sealed record UpdateAvatarCommand(Guid UserId, Stream Content, string ContentType, long SizeBytes)
    : IRequest<string>;

/// <remarks>
/// KIRPMA VE KÜÇÜLTME İSTEMCİDE yapılır (canvas), sunucu yalnızca doğrular.
///
/// Neden: sunucuda yeniden boyutlandırma bir görüntü kütüphanesi (ImageSharp vb.) gerektirir;
/// kırpma çerçevesini kullanıcı zaten tarayıcıda seçtiği için aynı canvas'tan hazır,
/// küçültülmüş bir kare çıkarmak hem bağımlılık eklemez hem de yükleme boyutunu düşürür.
///
/// Ama İSTEMCİYE GÜVENİLMEZ: boyut, tür ve içerik imzası burada yeniden denetlenir.
/// İstemci 20 MB'lık bir dosyayı "image/png" diyerek gönderebilir.
/// </remarks>
public sealed class UpdateAvatarHandler : IRequestHandler<UpdateAvatarCommand, string>
{
    // 2 MB: kırpılmış 512x512 bir kare çok daha küçüktür; bu sınır kötü niyete karşı.
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp"
    };

    private readonly IAppDbContext _db;
    private readonly IProofStorage _storage;

    public UpdateAvatarHandler(IAppDbContext db, IProofStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<string> Handle(UpdateAvatarCommand request, CancellationToken ct)
    {
        if (request.SizeBytes is <= 0 or > MaxAvatarBytes)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Fotoğraf boş veya 2 MB'tan büyük.");
        }

        if (!AllowedContentTypes.TryGetValue(request.ContentType, out var extension))
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Yalnızca PNG/JPEG/WebP kabul edilir.");
        }

        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (bytes.LongLength > MaxAvatarBytes)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Fotoğraf 2 MB'tan büyük.");
        }

        // İÇERİK İMZASI: Content-Type başlığı istemcinin iddiasıdır, dosyanın kendisi değil.
        // Sihirli baytlar uyuşmazsa dosya reddedilir — ".png" adıyla gönderilen bir betiğin
        // depoya girmesini engelleyen tek kontrol budur.
        if (!GorselImzasiTutuyor(bytes))
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Dosya bir görsel değil (içerik imzası uyuşmuyor).");
        }

        using var content = new MemoryStream(bytes, writable: false);
        var storageKey = await _storage.SaveAsync(content, extension, ct);

        var user = await _db.Users.SingleAsync(u => u.Id == request.UserId, ct);
        user.AvatarUrl = storageKey;
        await _db.SaveChangesAsync(ct);

        // Eski dosya bilerek SİLİNMİYOR: silme işlemi kayıt güncellemesiyle atomik değil
        // ve yarıda kalırsa profil var olmayan bir dosyaya işaret eder. Artık kullanılmayan
        // dosyaların temizliği ayrı bir bakım işidir.
        return storageKey;
    }

    private static bool GorselImzasiTutuyor(byte[] b)
    {
        if (b.Length < 12) return false;

        // PNG: 89 50 4E 47
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;

        // JPEG: FF D8 FF
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;

        // WebP: "RIFF" .... "WEBP"
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
            b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return true;

        return false;
    }
}

// ---------------------------------------------------------------------------
// Öğretmen adaylığı beyanı
// ---------------------------------------------------------------------------

public sealed record DeclareTeacherCandidateCommand(
    Guid UserId,
    string University,
    string Faculty,
    string Department,
    int? GradeYear,
    bool HasPedagogicalCertificate) : IRequest;

public sealed class DeclareTeacherCandidateHandler : IRequestHandler<DeclareTeacherCandidateCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly BadgeEngine _badges;

    public DeclareTeacherCandidateHandler(IAppDbContext db, IClock clock, BadgeEngine badges)
    {
        _db = db;
        _clock = clock;
        _badges = badges;
    }

    public async Task Handle(DeclareTeacherCandidateCommand request, CancellationToken ct)
    {
        foreach (var (ad, deger) in new[]
                 {
                     ("Üniversite", request.University),
                     ("Fakülte", request.Faculty),
                     ("Bölüm", request.Department)
                 })
        {
            var t = deger?.Trim();
            if (string.IsNullOrEmpty(t) || t.Length > 150)
            {
                throw new AppException(ErrorCodes.ValidationFailed, $"{ad} zorunludur (en fazla 150 karakter).");
            }
        }

        if (request.GradeYear is < 1 or > 6)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Sınıf 1-6 arasında olmalı.");
        }

        var profile = await _db.TeacherCandidateProfiles.SingleOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile is null)
        {
            profile = new TeacherCandidateProfile { UserId = request.UserId, DeclaredAtUtc = _clock.UtcNow };
            _db.TeacherCandidateProfiles.Add(profile);
        }
        else if (profile.VerifiedAtUtc is not null)
        {
            /*
              DOĞRULANMIŞ BEYAN DEĞİŞTİRİLEMEZ. Aksi halde kullanıcı yönetime bir bölüm
              gösterip onaylattıktan sonra alanları değiştirip "doğrulanmış" etiketini
              başka bir iddiaya taşıyabilirdi. Değişiklik gerekiyorsa yönetim doğrulamayı
              kaldırmalı (hakem panelinde "Kararı geri al").
            */
            throw new AppException(ErrorCodes.ValidationFailed,
                "Doğrulanmış beyan değiştirilemez. Güncelleme için yönetime başvurun.", statusCode: 409);
        }
        else
        {
            /*
              GÜNCELLENEN BEYAN YENİDEN İNCELEMEYE GİRER: varsa ret kalkar, beyan zamanı
              tazelenir ve kayıt kuyruğun sonuna değil, yeni tarihiyle sıraya girer.
              Reddedilen kullanıcının bilgilerini düzeltip yeniden gönderebilmesi gerekir;
              aksi halde tek bir hatalı beyan kalıcı bir cezaya dönüşürdü.

              Eski ret notu da silinir — o not artık geçerli olmayan bir iddia hakkındaydı.
              (Rozet, BadgeEngine yeniden değerlendirdiğinde geri gelir; kötüye kullanan
              kişi aynı anda kuyruğa da geri düşer, yani moderatörün görüş alanından
              çıkmaz.)
            */
            profile.RejectedAtUtc = null;
            profile.RejectedByAdminId = null;
            profile.ReviewNote = null;
            profile.DeclaredAtUtc = _clock.UtcNow;
        }

        profile.University = request.University.Trim();
        profile.Faculty = request.Faculty.Trim();
        profile.Department = request.Department.Trim();
        profile.GradeYear = request.GradeYear;
        profile.HasPedagogicalCertificate = request.HasPedagogicalCertificate;

        /*
          SIRA ÖNEMLİ — beyan ÖNCE kaydedilir, rozet SONRA değerlendirilir.

          Kural motoru veriyi DB'den okur (bkz. BadgeEngine: "olay anında değil mevcut
          veriden"). Değerlendirmeyi SaveChanges'ten önce çağırdığımda, henüz yazılmamış
          beyanı bulamıyor ve 🌱 rozeti verilmiyordu — arayüzde beyan görünüyor ama rozet
          gelmiyordu. İkisi tek transaction'da: rozet yazılamazsa beyan da geri alınır.
        */
        await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

        await _db.SaveChangesAsync(ct);
        await _badges.EvaluateAsync(request.UserId, ct);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
