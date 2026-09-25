using System.Runtime.CompilerServices;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/*
  BİLDİRİM TERCİHLERİ (comms.NotificationPreferences, kullanıcı başına tek satır).

  Mevcut GET /api/v1/preferences ve Preferences.cs'e DOKUNULMADI: web o DTO'yu okuyor ve
  tercihler ayrı tabloda (gerekçe NotificationPreference sınıf yorumunda: tek sütunluk
  yazımlar rıza satırının xmin'ini oynatmasın).

  ⛔ YAZIMLAR TAM GÖVDELİ DEĞİL, TEK SÜTUNLUK UPSERT. Tam gövdeli bir PUT'ta, art arda iki
  anahtar dokunuşunun istekleri sıra dışı varırsa geç gelen eski gövde ilk değişikliği
  geri yazardı; iyimser kilitle tekrar denemek de aynı eski değeri yeniden gönderirdi.
  Tek sütunluk yazımda farklı anahtarlara giden eşzamanlı istekler birbirine dokunmaz;
  aynı anahtara hızlı art arda dokunuşun sırasını mobildeki tek uçuşlu kuyruk korur.
*/

// ---------------------------------------------------------------------------
// GET /api/v1/push/preferences
// ---------------------------------------------------------------------------

public sealed record BildirimTercihleriQuery(Guid UserId) : IRequest<BildirimTercihleriDto>;

/// <param name="AydinlatmaAtUtc">Aydınlatma ekranında "Aç/Devam" denen İLK an; null ise hiç denmedi.</param>
/// <param name="SoruErtelemeSayisi">
/// Aydınlatma sorusunun kaç kez ertelendiği. Mobil geri çekilmeyi bundan hesaplıyor
/// (1 → 14 gün, 2 → 60 gün, 3+ → otomatik soru yok); cihazda tutulmuyor.
/// </param>
/// <param name="Alici">Bildirim verisindeki "alici" etiketi (BildirimEtiketi.Alici).</param>
public sealed record BildirimTercihleriDto(
    bool Mesajlar,
    bool Istekler,
    bool DersOnayi,
    bool DersPlani,
    DateTime? AydinlatmaAtUtc,
    int SoruErtelemeSayisi,
    DateTime? SoruErtelendiAtUtc,
    string Alici);

/// <summary>Satır yoksa tembel varsayılan: dördü açık, damga yok, erteleme 0.</summary>
public sealed class BildirimTercihleriHandler : IRequestHandler<BildirimTercihleriQuery, BildirimTercihleriDto>
{
    private readonly IAppDbContext _db;
    private readonly BildirimEtiketi _etiket;

    public BildirimTercihleriHandler(IAppDbContext db, BildirimEtiketi etiket)
    {
        _db = db;
        _etiket = etiket;
    }

    public async Task<BildirimTercihleriDto> Handle(BildirimTercihleriQuery request, CancellationToken ct)
    {
        var alici = _etiket.Alici(request.UserId);

        var p = await _db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == request.UserId, ct);

        return p is null
            ? new BildirimTercihleriDto(true, true, true, true, null, 0, null, alici)
            : new BildirimTercihleriDto(
                p.Messages,
                p.Requests,
                p.LessonApproval,
                p.LessonPlan,
                p.DisclosureShownAtUtc,
                p.PromptDeferCount,
                p.PromptDeferredAtUtc,
                alici);
    }
}

// ---------------------------------------------------------------------------
// PUT /api/v1/push/preferences/{kategori}
// ---------------------------------------------------------------------------

/// <param name="Kategori">mesajlar | istekler | ders-onayi | ders-plani (kanal kimliğiyle aynı).</param>
/// <param name="Acik">Zorunlu; eksik alan "kapat" sayılmasın diye null kabul edilip reddediliyor.</param>
public sealed record BildirimTercihiDegistirCommand(Guid UserId, string? Kategori, bool? Acik) : IRequest;

/// <summary>
/// Tek bir kategorinin anahtarı. Etki GÖNDERİM ANINDA: kapatılan kategoride kuyrukta
/// bekleyen bildirim de gitmez (dağıtıcı tercihi her satırda yeniden okur).
/// </summary>
public sealed class BildirimTercihiDegistirHandler : IRequestHandler<BildirimTercihiDegistirCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public BildirimTercihiDegistirHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task Handle(BildirimTercihiDegistirCommand request, CancellationToken ct)
    {
        /* ⛔ Kolon adı SQL'e METİN olarak giriyor (tanımlayıcı parametre olamaz). Bu yüzden
           YALNIZCA sabit beyaz listeden (TercihSutunu) gelir; istekteki dizge hiçbir koşulda
           SQL'e konmaz — bilinmeyen kategori burada 404'e düşer. */
        var sutun = BildirimKanallari.TercihSutunu(request.Kategori?.Trim())
                    ?? throw new AppException(ErrorCodes.ValidationFailed, "Bilinmeyen bildirim kategorisi.", statusCode: 404);

        if (request.Acik is not { } acik)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Tercihin açık ya da kapalı olduğu belirtilmeli.");
        }

        var now = _clock.UtcNow;

        /* Satır ilk kez yaratılıyorsa diğer üç anahtar DB varsayılanından (true) gelir — bu
           yüzden o varsayılanlar süs değil (NotificationConfigurations). Format dizgesindeki
           {0}…{3} parametre yeri; {{sutun}} ise beyaz listeden gelen kolon adı. */
        var sql = FormattableStringFactory.Create(
            $$"""
            INSERT INTO comms."NotificationPreferences" ("Id", "UserId", "CreatedAtUtc", "UpdatedAtUtc", "{{sutun}}")
            VALUES ({0}, {1}, {2}, {2}, {3})
            ON CONFLICT ("UserId") DO UPDATE SET
                "{{sutun}}" = EXCLUDED."{{sutun}}",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """,
            Guid.NewGuid(), request.UserId, now, acik);

        await _db.Database.ExecuteSqlInterpolatedAsync(sql, ct);
    }
}

// ---------------------------------------------------------------------------
// PUT /api/v1/push/prompt — aydınlatma sorusunun sonucu
// ---------------------------------------------------------------------------

/// <summary>Aydınlatma ekranındaki karar.</summary>
public enum PushIzinKarari
{
    /// <summary>"Aç/Devam": aydınlatma görüldü. Sistem isteminin sonucu ayrı (cihazda).</summary>
    Acildi = 0,

    /// <summary>"Şimdi değil" ya da ekran başka yoldan kapandı: erteleme sayılır.</summary>
    Ertelendi = 1
}

/// <param name="Karar">Zorunlu; eksikse 400.</param>
public sealed record PushIzinKarariCommand(Guid UserId, PushIzinKarari? Karar) : IRequest;

/// <summary>
/// Aydınlatma damgası ve erteleme sayacı. Durum cihazda TUTULMAZ: tutulsaydı yeni bir
/// cihaz saklaması, yani izin kategorisi ve IZIN_SURUMU artışı gerekirdi; ayrıca cihaz
/// değişince soru baştan sorulurdu.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Acildi: DisclosureShownAtUtc = COALESCE(mevcut, now). İLK aydınlatma anı korunur;
/// kanıt değeri olan o an, sonraki her "Aç"ta ezilmesin.</item>
/// <item>Ertelendi: PromptDeferCount + 1, PromptDeferredAtUtc = now. Artış SQL'de yapılıyor
/// (okuyup yazmak iki eşzamanlı kapanışta bir ertelemeyi kaybederdi).</item>
/// </list>
/// Tek ifadelik upsert: satır yoksa oluşturur, varsa yalnızca ilgili sütunlara dokunur.
/// </remarks>
public sealed class PushIzinKarariHandler : IRequestHandler<PushIzinKarariCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public PushIzinKarariHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task Handle(PushIzinKarariCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var id = Guid.NewGuid();

        switch (request.Karar)
        {
            case PushIzinKarari.Acildi:
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO comms."NotificationPreferences" ("Id", "UserId", "CreatedAtUtc", "UpdatedAtUtc", "DisclosureShownAtUtc")
                    VALUES ({id}, {request.UserId}, {now}, {now}, {now})
                    ON CONFLICT ("UserId") DO UPDATE SET
                        "DisclosureShownAtUtc" = COALESCE("NotificationPreferences"."DisclosureShownAtUtc", EXCLUDED."DisclosureShownAtUtc"),
                        "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
                    """, ct);
                break;

            case PushIzinKarari.Ertelendi:
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO comms."NotificationPreferences" ("Id", "UserId", "CreatedAtUtc", "UpdatedAtUtc", "PromptDeferCount", "PromptDeferredAtUtc")
                    VALUES ({id}, {request.UserId}, {now}, {now}, 1, {now})
                    ON CONFLICT ("UserId") DO UPDATE SET
                        "PromptDeferCount" = "NotificationPreferences"."PromptDeferCount" + 1,
                        "PromptDeferredAtUtc" = EXCLUDED."PromptDeferredAtUtc",
                        "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
                    """, ct);
                break;

            default:
                throw new AppException(ErrorCodes.ValidationFailed, "Karar zorunludur (Acildi ya da Ertelendi).");
        }
    }
}
