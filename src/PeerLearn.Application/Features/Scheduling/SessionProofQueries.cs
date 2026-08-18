using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Scheduling;

/// <summary>
/// Kanıt görüntüleme (Modül 3.3'ün ön koşulu). Çift taraflı onayın anlamlı olması için
/// ONAYLAYAN ÖĞRENCİ kanıtı görebilmelidir; aksi halde "onayla" butonu körlemesine basılır.
/// Erişim: yalnızca dersin iki tarafı (adminler kendi uçlarından bakar).
/// </summary>
public sealed record GetSessionProofsQuery(Guid SessionId, Guid UserId)
    : IRequest<IReadOnlyList<SessionProofDto>>;

public sealed record SessionProofDto(
    Guid ProofId,
    Guid UploadedByUserId,
    string ContentType,
    long FileSizeBytes,
    string Sha256Hash,
    bool IsDuplicateHash,
    string Status,
    DateTime UploadedAtUtc);

public sealed class GetSessionProofsHandler
    : IRequestHandler<GetSessionProofsQuery, IReadOnlyList<SessionProofDto>>
{
    private readonly IAppDbContext _db;

    public GetSessionProofsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SessionProofDto>> Handle(GetSessionProofsQuery request, CancellationToken ct)
    {
        await SessionAccess.EnsureParticipantAsync(_db, request.SessionId, request.UserId, ct);

        var rows = await _db.SessionProofs.AsNoTracking()
            .Where(p => p.SessionId == request.SessionId)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.UploadedByUserId,
                p.ContentType,
                p.FileSizeBytes,
                p.Sha256Hash,
                p.IsDuplicateHash,
                p.Status,
                p.CreatedAtUtc
            })
            .ToListAsync(ct);

        /*
          TEKRAR-KANIT SİNYALİ YÜKLEYENE GÖSTERİLMEZ.

          CompleteSession ve SessionProof entity'si bu kararı zaten yazıyor ("hilekâr
          eğitmene 'yakalandın' ipucu verilmez") ama uç, katılımcı kontrolünden geçen HERKESE
          — yükleyenin kendisine de — dönüyordu. Sahte ekran görüntüsü yükleyen eğitmen bu
          uçtan yakalandığını anında öğrenip görseli bir piksel kırparak SHA-256 tespitini
          kalıcı olarak boşa çıkarabiliyordu; sistemdeki tek otomatik dolandırıcılık sinyali
          değersizleşiyordu.

          Karşı taraf (öğrenci) uyarıyı görmeye devam eder — onay kararını verecek olan o.
          Yönetim zaten ayrı bir uçtan okuyor (AdminController).
        */
        return rows
            .Select(p => new SessionProofDto(
                p.Id,
                p.UploadedByUserId,
                p.ContentType,
                p.FileSizeBytes,
                p.Sha256Hash,
                IsDuplicateHash: p.IsDuplicateHash && p.UploadedByUserId != request.UserId,
                p.Status.ToString(),
                p.CreatedAtUtc))
            .ToList();
    }
}

/// <summary>Kanıt görselinin kendisi. Dosya depoda yoksa 404 (exception değil, akış hatası).</summary>
public sealed record GetProofContentQuery(Guid SessionId, Guid ProofId, Guid UserId, bool AsAdmin = false)
    : IRequest<ProofContentDto>;

public sealed record ProofContentDto(Stream Content, string ContentType);

public sealed class GetProofContentHandler : IRequestHandler<GetProofContentQuery, ProofContentDto>
{
    private readonly IAppDbContext _db;
    private readonly IProofStorage _storage;

    public GetProofContentHandler(IAppDbContext db, IProofStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<ProofContentDto> Handle(GetProofContentQuery request, CancellationToken ct)
    {
        if (!request.AsAdmin)
        {
            await SessionAccess.EnsureParticipantAsync(_db, request.SessionId, request.UserId, ct);
        }

        var proof = await _db.SessionProofs.AsNoTracking()
                        .SingleOrDefaultAsync(p => p.Id == request.ProofId && p.SessionId == request.SessionId, ct)
                    ?? throw new AppException(ErrorCodes.ProofInvalid, "Kanıt bulunamadı.", statusCode: 404);

        /*
          SAKLAMA SÜRESİ DOLMUŞ KANIT, KAYIP KANIT DEĞİLDİR.

          Satır duruyor (hash sahte kanıt tespiti için saklanıyor) ama görsel silinmiş.
          Buna "bulunamadı" demek kullanıcıya bir arıza yaşadığını düşündürürdü; oysa
          olan şey planlı ve normal. Ayrı mesaj, ayrı beklenti.
        */
        if (proof.ContentDeletedAtUtc is not null)
        {
            throw new AppException(ErrorCodes.ProofInvalid,
                "Bu kanıtın saklama süresi doldu ve görsel arşivden kaldırıldı.", statusCode: 410);
        }

        var stream = await _storage.OpenAsync(proof.StorageKey, ct)
                     ?? throw new AppException(ErrorCodes.ProofInvalid,
                         "Kanıt dosyası depoda bulunamadı.", statusCode: 404);

        return new ProofContentDto(stream, proof.ContentType);
    }
}

/// <summary>Ders erişim kuralı tek yerde: çağıran, dersin eğitmeni ya da öğrencisi olmalı.</summary>
public static class SessionAccess
{
    public static async Task<LessonSession> EnsureParticipantAsync(
        IAppDbContext db, Guid sessionId, Guid userId, CancellationToken ct)
    {
        var session = await db.LessonSessions.AsNoTracking()
                          .SingleOrDefaultAsync(s => s.Id == sessionId, ct)
                      ?? throw new AppException(ErrorCodes.SessionNotFound, "Ders bulunamadı.", statusCode: 404);

        if (session.TutorUserId != userId && session.StudentUserId != userId)
        {
            throw new AppException(ErrorCodes.NotSessionParticipant,
                "Bu dersin tarafı değilsiniz.", statusCode: 403);
        }

        return session;
    }
}
