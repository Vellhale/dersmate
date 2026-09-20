using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Scheduling;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Scheduling;

/// <summary>
/// "Dersi Tamamladım" (Modül 3): TIME-LOCK kontrolü + doğrulama kodu eşleşmesi + kanıt
/// (ekran görüntüsü) yüklemesi tek adımda. Başarılıysa ders öğrenci onayına düşer.
/// </summary>
public sealed record CompleteSessionCommand(
    Guid SessionId,
    Guid TutorUserId,
    string ProvidedVerificationCode,
    Stream ProofContent,
    string ProofContentType,
    long ProofSizeBytes) : IRequest<CompleteSessionResult>;

/// <remarks>
/// Tekrar-kanıt (duplicate hash) sinyali BİLİNÇLİ olarak yanıtta yer almaz: hilekâr eğitmene
/// "yakalandın" ipucu verilmez. Sinyal SessionProof.IsDuplicateHash olarak kalıcıdır ve
/// yalnızca admin panelinde görünür.
/// </remarks>
public sealed record CompleteSessionResult(Guid ProofId);

public sealed class CompleteSessionHandler : IRequestHandler<CompleteSessionCommand, CompleteSessionResult>
{
    private const long MaxProofBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp"
    };

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IProofStorage _storage;
    private readonly IGorselTemizleyici _temizleyici;

    public CompleteSessionHandler(IAppDbContext db, IClock clock, IProofStorage storage, IGorselTemizleyici temizleyici)
    {
        _db = db;
        _clock = clock;
        _storage = storage;
        _temizleyici = temizleyici;
    }

    public async Task<CompleteSessionResult> Handle(CompleteSessionCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var session = await _db.LessonSessions.SingleOrDefaultAsync(s => s.Id == request.SessionId, ct)
                      ?? throw new AppException(ErrorCodes.SessionNotFound, "Ders bulunamadı.", statusCode: 404);

        // TIME-LOCK + durum + yetki kuralları (SessionRules içinde, birim testli).
        SessionRules.EnsureCanRequestCompletion(session, request.TutorUserId, now);
        SessionRules.EnsureVerificationCodeMatches(session, request.ProvidedVerificationCode);

        if (request.ProofSizeBytes is <= 0 or > MaxProofBytes)
        {
            throw new AppException(ErrorCodes.ProofInvalid, "Kanıt dosyası boş veya 10 MB'tan büyük.");
        }

        if (!AllowedContentTypes.TryGetValue(request.ProofContentType, out var extension))
        {
            throw new AppException(ErrorCodes.ProofInvalid, "Yalnızca PNG/JPEG/WebP kabul edilir.");
        }

        // Hash'i bellekte hesapla, sonra depoya yaz (10 MB sınırı bunu güvenli kılar).
        using var buffer = new MemoryStream();
        await request.ProofContent.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (bytes.LongLength != request.ProofSizeBytes || bytes.LongLength > MaxProofBytes)
        {
            throw new AppException(ErrorCodes.ProofInvalid, "Kanıt dosyası boyutu geçersiz.");
        }

        // EXIF/GPS/metadata TEMİZLİĞİ — hash ve depoya yazmadan ÖNCE. Üç içerik tipi de
        // görsel (PNG/JPEG/WebP), hepsi temizlenir. SIRA KRİTİK: hash ve SaveAsync AYNI
        // temizlenmiş baytlardan olmalı, yoksa IsDuplicateHash depodaki dosyayla tutarsız
        // kalır. (Davranış değişikliği: eski satırların hash'i HAM bayttan; aynı görselin
        // eski ve yeni yüklemesi artık aynı hash'e düşmez — yalnızca admin-görünür sezgi.)
        if (!_temizleyici.TryTemizle(bytes, request.ProofContentType, out var temizBytes))
        {
            throw new AppException(ErrorCodes.ProofInvalid, "Kanıt görseli çözümlenemedi.");
        }
        bytes = temizBytes;

        // Yeniden kodlama nadiren de olsa boyutu büyütebilir; sınırı temizlik SONRASI yeniden uygula.
        if (bytes.LongLength > MaxProofBytes)
        {
            throw new AppException(ErrorCodes.ProofInvalid, "Kanıt dosyası boyutu geçersiz.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // Sahte kanıt sinyali: aynı görsel farklı bir derste kullanılmış mı? (Modül 4.3)
        var duplicate = await _db.SessionProofs.AnyAsync(
            p => p.Sha256Hash == hash && p.SessionId != session.Id, ct);

        using var temizAkis = new MemoryStream(bytes, writable: false);
        var storageKey = await _storage.SaveAsync(temizAkis, extension, ct);

        var proof = new SessionProof
        {
            SessionId = session.Id,
            UploadedByUserId = request.TutorUserId,
            StorageKey = storageKey,
            ContentType = request.ProofContentType.ToLowerInvariant(),
            FileSizeBytes = bytes.LongLength,
            Sha256Hash = hash,
            IsDuplicateHash = duplicate
        };
        _db.SessionProofs.Add(proof);

        session.Status = SessionStatus.AwaitingApproval;
        session.CompletionRequestedAtUtc = now;

        // xmin: eşzamanlı ikinci tamamlama isteği DbUpdateConcurrencyException ile düşer.
        await _db.SaveChangesAsync(ct);

        return new CompleteSessionResult(proof.Id);
    }
}
