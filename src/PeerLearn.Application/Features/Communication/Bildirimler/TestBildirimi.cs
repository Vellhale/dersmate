using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Communication;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <param name="Tur">mesaj | istek | onay | ders — denenecek türün kanalı buradan seçilir.</param>
public sealed record TestBildirimiCommand(Guid UserId, string? Tur) : IRequest<TestBildirimiSonucu>;

/// <param name="Cihaz">
/// İstek anında bu hesaba BAĞLI (OturumBagi) cihaz sayısı. 0 ise bildirim hiçbir cihaza
/// gitmeyecek; ekran "bu hesaba bağlı cihaz yok" diyebilsin diye döndürülüyor. Satır yine
/// yazılır: dağıtıcı onu Skipped(CihazYok) yapar ve teşhiste o da bir iz.
/// </param>
public sealed record TestBildirimiSonucu(int Cihaz);

/// <summary>
/// Çağıranın KENDİ bağlı cihazlarına, seçtiği türün kanalıyla ve gerçek veri biçimiyle tek
/// bir bildirim. Kullanıcı için "bildirim gelmiyor" teşhisi, geliştirici için emülatörde ve
/// cihazda uçtan uca yol.
/// </summary>
/// <remarks>
/// ─── NEDEN KUYRUKTAN GEÇİYOR ────────────────────────────────────────────────
/// Doğrudan Expo'ya gönderilseydi yalnızca göndericiyi sınardı. Defterden geçince bütün hat
/// (satır → sinyal → dağıtıcı → oturum bağı → yük → Expo → cihaz) sınanıyor; testin
/// geçmesi gerçek bir bildirimin de geçeceğini gösteriyor. Kategori tercihleri UYGULANMAZ
/// (kapattığı kategoriyi deneyen kullanıcı neden sessiz kaldığını anlamazdı), oturum bağı
/// uygulanır.
///
/// ─── SINIR: SON 10 DAKİKADA 3 ───────────────────────────────────────────────
/// Sayaç veritabanından (Test satırları) okunuyor; bellekte tutulsaydı iki sunucu kopyası
/// ayrı ayrı sayardı. Sayım ile yazım arasında aynı kullanıcı için advisory kilit var:
/// eşzamanlı iki istek ikisi de "2 satır var" görüp sınırı aşamasın.
/// </remarks>
public sealed class TestBildirimiHandler : IRequestHandler<TestBildirimiCommand, TestBildirimiSonucu>
{
    public const int PencereDakika = 10;
    public const int PenceredeEnFazla = 3;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IBildirimSinyali _sinyal;

    public TestBildirimiHandler(IAppDbContext db, IClock clock, IBildirimSinyali sinyal)
    {
        _db = db;
        _clock = clock;
        _sinyal = sinyal;
    }

    public async Task<TestBildirimiSonucu> Handle(TestBildirimiCommand request, CancellationToken ct)
    {
        var kanal = BildirimKanallari.TestKanali(request.Tur?.Trim())
                    ?? throw new AppException(ErrorCodes.ValidationFailed,
                        "Bilinmeyen test türü (mesaj, istek, onay ya da ders olmalı).");

        var now = _clock.UtcNow;
        int cihaz;

        await using (var tx = await _db.BeginTransactionAsync(cancellationToken: ct))
        {
            var kilit = $"push-test:{request.UserId:D}";
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({kilit}, 0))", ct);

            var esik = now.AddMinutes(-PencereDakika);
            var sonTestler = await _db.Notifications.AsNoTracking()
                .CountAsync(n => n.RecipientUserId == request.UserId
                                 && n.Type == NotificationType.Test
                                 && n.CreatedAtUtc >= esik, ct);

            if (sonTestler >= PenceredeEnFazla)
            {
                // Mobil 429'u "Biraz sonra tekrar dene." diye gösteriyor.
                throw new AppException(ErrorCodes.ValidationFailed,
                    "Çok sık test bildirimi istedin. Biraz sonra tekrar dene.", statusCode: 429);
            }

            cihaz = await _db.PushDevices.AsNoTracking()
                .Where(d => d.UserId == request.UserId)
                .Where(OturumBagi.BagliCihaz(_db.RefreshTokens, now))
                .CountAsync(ct);

            BildirimKuyrugu.Ekle(_db, BildirimKuyrugu.Test(request.UserId, kanal, now));
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Commit'ten SONRA; fırlatmaz (IBildirimSinyali sözleşmesi).
        _sinyal.Uyandir();

        return new TestBildirimiSonucu(cihaz);
    }
}
