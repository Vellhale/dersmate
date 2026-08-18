using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>Dinamik portföy girişi (Modül 1.1): "verebilirim" (Offer) / "almak istiyorum" (Seek).</summary>
public sealed record AddPortfolioEntryCommand(
    Guid UserId,
    Guid TopicId,
    PortfolioDirection Direction,
    int SelfAssessedLevel,
    string? Note,
    bool IsVolunteer = false) : IRequest<Guid>;

public sealed class AddPortfolioEntryHandler : IRequestHandler<AddPortfolioEntryCommand, Guid>
{
    private readonly IAppDbContext _db;

    public AddPortfolioEntryHandler(IAppDbContext db) => _db = db;

    public async Task<Guid> Handle(AddPortfolioEntryCommand request, CancellationToken ct)
    {
        if (request.SelfAssessedLevel is < 1 or > 5)
        {
            throw new AppException(ErrorCodes.PortfolioDuplicate, "Seviye 1-5 arasında olmalı.");
        }

        var topicExists = await _db.Topics.AnyAsync(t => t.Id == request.TopicId && t.IsActive, ct);
        if (!topicExists)
        {
            throw new AppException(ErrorCodes.PortfolioDuplicate, "Konu bulunamadı.", statusCode: 404);
        }

        /*
          MEVCUT KAYIT: aynı (kullanıcı, konu, yön) üçlüsü için tek satır var — unique index
          FİLTRESİZ, yani pasif satır da yeri işgal eder.

          Bu yüzden "zaten var mı" kontrolü PASİF SATIRI DA GÖRMELİ ama onu hata sebebi
          SAYMAMALI: kaldırma işlemi satırı silmiyor, IsActive=false yapıyor (eşleşme
          geçmişinin bağlamı korunsun diye). Eskiden her ikisi de 409'a düşüyordu, yani
          portföyünden bir konuyu çıkaran kullanıcı onu bir daha EKLEYEMİYORDU: ekranda konu
          görünmüyor ama eklemeye çalışınca "zaten portföyünüzde" diyordu — kullanıcının
          kendi başına çıkamayacağı bir çıkmaz. Fikrini değiştiren herkeste tetikleniyordu.

          Doğrusu: pasif satırı yeniden CANLANDIR (yeni seviye/not/gönüllü değerleriyle).
          Yalnızca index'i kısmileştirmek yetmezdi — o zaman aynı üçlü için birden çok pasif
          satır birikir ve hangisinin canlanacağı belirsizleşirdi.
        */
        var mevcut = await _db.PortfolioEntries.SingleOrDefaultAsync(p =>
            p.UserId == request.UserId && p.TopicId == request.TopicId && p.Direction == request.Direction, ct);

        if (mevcut is { IsActive: true })
        {
            throw new AppException(ErrorCodes.PortfolioDuplicate,
                "Bu konu zaten portföyünüzde (aynı yönde).", statusCode: 409);
        }

        /*
          GÖNÜLLÜ İLAN İKİ KOŞULA BAĞLI:
            1. Yalnızca Offer olabilir — "ücretsiz ders ALMAK istiyorum" anlamsızdır,
               zaten her talep kredisi olan için ücretlidir.
            2. Öğretmen adayı beyanı gerekir. Beyan doğrulanmış olmak zorunda değil
               (bkz. TeacherCandidateProfile notu); amaç niyeti kayda geçirmek ve
               herkesin gelişigüzel "bedava" etiketiyle listeleri doldurmasını önlemek.
               Ama moderasyonun REDDETTİĞİ beyan kapıyı açmaz — reddi yalnızca rozete
               işletip ilan açmaya izin vermek, kararı sözde bırakırdı.
        */
        if (request.IsVolunteer)
        {
            if (request.Direction != PortfolioDirection.Offer)
            {
                throw new AppException(ErrorCodes.ValidationFailed,
                    "Gönüllü seçeneği yalnızca verebileceğin konular için geçerlidir.");
            }

            var beyan = await _db.TeacherCandidateProfiles.AsNoTracking()
                .Where(p => p.UserId == request.UserId)
                .Select(p => new { p.RejectedAtUtc })
                .SingleOrDefaultAsync(ct);

            if (beyan is null)
            {
                throw new AppException(ErrorCodes.ValidationFailed,
                    "Gönüllü ders açmak için önce öğretmen adaylığı bilgilerini doldurmalısın.",
                    statusCode: 403);
            }

            if (beyan.RejectedAtUtc is not null)
            {
                throw new AppException(ErrorCodes.ValidationFailed,
                    "Öğretmen adaylığı beyanın yönetim tarafından uygun bulunmadı. " +
                    "Profilinden bilgilerini güncelleyip yeniden gönderebilirsin.",
                    statusCode: 403);
            }
        }

        PortfolioEntry entry;

        if (mevcut is not null)
        {
            // Pasif satır canlandırılıyor: kimlik (Id) korunuyor, içerik yeni istekten geliyor.
            entry = mevcut;
            entry.IsActive = true;
        }
        else
        {
            entry = new PortfolioEntry
            {
                UserId = request.UserId,
                TopicId = request.TopicId,
                Direction = request.Direction
            };
            _db.PortfolioEntries.Add(entry);
        }

        entry.SelfAssessedLevel = request.SelfAssessedLevel;
        entry.Note = request.Note?.Trim();
        entry.IsVolunteer = request.IsVolunteer;

        await _db.SaveChangesAsync(ct); // Unique index eşzamanlı eklemede son savunma.

        return entry.Id;
    }
}

public sealed record RemovePortfolioEntryCommand(Guid UserId, Guid EntryId) : IRequest;

public sealed class RemovePortfolioEntryHandler : IRequestHandler<RemovePortfolioEntryCommand>
{
    private readonly IAppDbContext _db;

    public RemovePortfolioEntryHandler(IAppDbContext db) => _db = db;

    public async Task Handle(RemovePortfolioEntryCommand request, CancellationToken ct)
    {
        var entry = await _db.PortfolioEntries
                        .SingleOrDefaultAsync(p => p.Id == request.EntryId && p.UserId == request.UserId, ct)
                    ?? throw new AppException(ErrorCodes.PortfolioDuplicate, "Kayıt bulunamadı.", statusCode: 404);

        // Soft-deactivate: eşleşme geçmişinin bağlamı korunur, çapraz eşleşme sorgusu görmez.
        entry.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }
}
