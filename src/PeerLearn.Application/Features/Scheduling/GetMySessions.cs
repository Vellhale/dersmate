using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Options;
using PeerLearn.Application.Scheduling;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Scheduling;

/// <summary>
/// "Derslerim" ekranı. Aksiyon bayrakları (CanComplete/CanApprove/...) SessionRules'un
/// KENDİSİNDEN türetilir — arayüz, sunucunun reddedeceği bir butonu asla göstermez.
/// </summary>
/// <remarks>
/// SAYFALAMA NEDEN İKİ PARÇALI?
///
/// Liste eskiden tek parçaydı ve sonuna sessiz bir <c>Take(200)</c> konmuştu. İki ayrı
/// sorun üretiyordu: 200'ünü dolduran kullanıcı eski derslerini BİR DAHA göremiyordu
/// (ve bunu ona söyleyen hiçbir şey yoktu), ayrıca kesme tarihe göre olduğu için
/// AKSİYON BEKLEYEN bir ders — 48 saatlik onay penceresindeki bir kayıt — eski
/// rezervasyonların altında kalıp listeden düşebiliyordu. Kredi kaybına giden yol budur.
///
/// Bölme bu yüzden duruma göre yapılıyor, tarihe göre değil:
///  • AKTİF (Booked/AwaitingApproval/Disputed) — kullanıcının hâlâ yapabileceği bir şey
///    olan kayıtlar. Hepsi döner.
///  • GEÇMİŞ (Completed/Cancelled/Expired) — sınırsız büyüyen tek küme. Sayfalanır.
///
/// Aktif tarafta yine de bir tavan var (<see cref="MaxActive"/>). GEREKÇESİ DEĞİŞTİ:
/// eskiden "her aktif ders escrow'da kredi tuttuğu için sayıları doğal olarak sınırlıdır"
/// varsayımına dayanıyordu. Ders almak ücretsizleşince o doğal sınır tamamen kalktı —
/// rezervasyon artık hiçbir bakiye tüketmiyor, yani aktif küme de ilkesel olarak
/// sınırsız. Tavanı asıl tutan şey bugün MintGuard'ın davranışsal sınırları; buradaki
/// kesme ise son savunma. Ve SESSİZ değil: gerçek toplam ayrıca dönülür, arayüz
/// kesildiğini söyler.
/// </remarks>
public sealed record GetMySessionsQuery(Guid UserId, int PastPage = 1, int PastPageSize = 20)
    : IRequest<MySessionsDto>;

public sealed record MySessionsDto(
    IReadOnlyList<SessionListItemDto> Active,

    /// <summary>Aktif derslerin GERÇEK sayısı; Active.Count'tan büyükse liste kesilmiştir.</summary>
    int ActiveTotal,

    PagedResult<SessionListItemDto> Past);

public sealed record SessionListItemDto(
    Guid SessionId,
    Guid MatchId,
    bool IAmTutor,
    Guid OtherUserId,
    string OtherDisplayName,
    string TopicName,
    string SubjectName,
    DateTime ScheduledStartUtc,
    DateTime ScheduledEndUtc,
    int DurationMinutes,

    /// <summary>Ders onaylandığında EĞİTMENE basılacak puan. Öğrencinin maliyeti DEĞİL.</summary>
    int MintAmount,

    /// <summary>Gönüllü ders: eğitmen puan kazanmaz.</summary>
    bool IsVolunteer,

    string Status,
    string VerificationCode,
    DateTime? CompletionRequestedAtUtc,
    /// <summary>
    /// Öğrenci bu ana kadar onay/itiraz vermezse ders sistem tarafından otomatik onaylanır.
    /// Sunucuda hesaplanır ki arayüz 48 sayısını sabit yazmasın (config değişirse yalan olurdu).
    /// </summary>
    DateTime? AutoApproveDeadlineUtc,
    bool CanComplete,
    bool CanApprove,
    bool CanCancel,
    bool CanDispute);

public sealed class GetMySessionsHandler : IRequestHandler<GetMySessionsQuery, MySessionsDto>
{
    /// <summary>Aktif listede dönülecek en fazla kayıt. Aşılırsa arayüz bunu SÖYLER.</summary>
    private const int MaxActive = 100;

    /// <summary>
    /// Kullanıcının hâlâ bir şey yapabileceği durumlar. Bu kümenin dışındaki her durum
    /// nihaidir ve "geçmiş" sayılır — yeni bir SessionStatus eklenirse buraya da bakılmalı.
    /// </summary>
    private static readonly SessionStatus[] AktifDurumlar =
    [
        SessionStatus.Booked,
        SessionStatus.AwaitingApproval,
        SessionStatus.Disputed
    ];

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly EconomyOptions _economy;

    public GetMySessionsHandler(IAppDbContext db, IClock clock, IOptions<EconomyOptions> economy)
    {
        _db = db;
        _clock = clock;
        _economy = economy.Value;
    }

    /// <summary>Ekrana çıkacak satır. Anonim tip metottan döndürülemediği için adlandırıldı.</summary>
    private sealed record Satir(
        LessonSession Session,
        string TutorName,
        string StudentName,
        string TopicName,
        string SubjectName);

    /*
      SORGU İKİYE BÖLÜNDÜ: ÖNCE SIRALA+SAYFALA, SONRA DETAY GETİR.

      Denenen ve ÇALIŞMAYAN yol: birleşimlerin sonunda tek bir projeksiyona (Satir) çevirip
      onun üzerinde OrderBy/Skip/Take yapmak. EF, projeksiyondan sonra hem Contains'i hem de
      OrderBy'ı SQL'e çeviremiyor — ikisi de derleme değil ÇALIŞMA anında patlıyor.

      Bölme ayrıca kendi başına daha iyi: sıralama ve sayfalama dar LessonSessions tablosu
      üzerinde, (TutorUserId, ScheduledStartUtc) index'i kullanılarak yapılıyor; dört tablolu
      birleşim yalnızca sayfaya giren avuç dolusu kayıt için koşuyor. Sıralamayı bellekte
      yeniden kurmak da bilinçli: EF'in JOIN sonrası ORDER BY'ı koruyacağı garanti değil,
      id listesinin sırasını izlemek ise kesin.
    */
    public async Task<MySessionsDto> Handle(GetMySessionsQuery request, CancellationToken ct)
    {
        var me = request.UserId;
        var now = _clock.UtcNow;

        var pastPage = Math.Max(1, request.PastPage);
        var pastPageSize = Math.Clamp(request.PastPageSize, 1, 100);

        var aktifKaynak = Kaynak(me, aktif: true);
        var gecmisKaynak = Kaynak(me, aktif: false);

        // Toplam AYRICA sayılır: kesme olup olmadığını arayüze söyleyen tek şey bu.
        var aktifToplam = await aktifKaynak.CountAsync(ct);

        // Aktifte sıralama ARTAN: en yakın ders başta. (Geçmişte azalan — en yeni başta.)
        var aktifIdler = await aktifKaynak
            .OrderBy(s => s.ScheduledStartUtc)
            .ThenBy(s => s.Id)
            .Take(MaxActive)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var gecmisToplam = await gecmisKaynak.CountAsync(ct);

        var gecmisIdler = await gecmisKaynak
            .OrderByDescending(s => s.ScheduledStartUtc)
            // İkincil anahtar: aynı saniyeye denk gelen iki ders arasında sıralama
            // belirsiz kalırsa aynı kayıt iki farklı sayfada görünebilir (ya da hiç görünmez).
            .ThenByDescending(s => s.Id)
            .Skip((pastPage - 1) * pastPageSize)
            .Take(pastPageSize)
            .Select(s => s.Id)
            .ToListAsync(ct);

        // Tek detay sorgusu iki listeyi birden karşılar.
        var detay = await DetaylarAsync([.. aktifIdler, .. gecmisIdler], ct);

        List<SessionListItemDto> Sirala(List<Guid> idler) => idler
            .Where(detay.ContainsKey)
            .Select(id => Olustur(detay[id], me, now))
            .ToList();

        return new MySessionsDto(
            Sirala(aktifIdler),
            aktifToplam,
            new PagedResult<SessionListItemDto>(Sirala(gecmisIdler), gecmisToplam, pastPage, pastPageSize));
    }

    /// <summary>
    /// Durum süzgeci ham <c>LessonSessions</c> üzerinde; birleşim yok, index doğrudan kullanılır.
    /// </summary>
    private IQueryable<LessonSession> Kaynak(Guid me, bool aktif)
    {
        var kaynak = _db.LessonSessions.AsNoTracking()
            .Where(s => s.TutorUserId == me || s.StudentUserId == me);

        return aktif
            ? kaynak.Where(s => AktifDurumlar.Contains(s.Status))
            : kaynak.Where(s => !AktifDurumlar.Contains(s.Status));
    }

    private async Task<Dictionary<Guid, Satir>> DetaylarAsync(List<Guid> idler, CancellationToken ct)
    {
        if (idler.Count == 0)
        {
            return [];
        }

        var rows = await (
                from s in _db.LessonSessions.AsNoTracking()
                where idler.Contains(s.Id)
                join tutor in _db.Users.AsNoTracking() on s.TutorUserId equals tutor.Id
                join student in _db.Users.AsNoTracking() on s.StudentUserId equals student.Id
                join topic in _db.Topics.AsNoTracking() on s.TopicId equals topic.Id
                join subject in _db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                select new Satir(s, tutor.DisplayName, student.DisplayName, topic.Name, subject.Name))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Session.Id);
    }

    private SessionListItemDto Olustur(Satir r, Guid me, DateTime now)
    {
        var s = r.Session;
        var iAmTutor = s.TutorUserId == me;

        return new SessionListItemDto(
            s.Id,
            s.MatchId,
            iAmTutor,
            iAmTutor ? s.StudentUserId : s.TutorUserId,
            iAmTutor ? r.StudentName : r.TutorName,
            r.TopicName,
            r.SubjectName,
            s.ScheduledStartUtc,
            s.ScheduledEndUtc,
            s.DurationMinutes,
            s.CreditCost,
            s.IsVolunteer,
            s.Status.ToString(),
            s.VerificationCode,
            s.CompletionRequestedAtUtc,
            AutoApproveDeadlineUtc: s.Status == SessionStatus.AwaitingApproval && s.CompletionRequestedAtUtc is not null
                ? s.CompletionRequestedAtUtc.Value.AddHours(_economy.AutoApproveHours)
                : null,
            // Kuralların TEK kaynağı yine SessionRules; fark, izin kontrolünün artık
            // exception fırlatmadan yapılması (bkz. SessionRules'taki ihlal/fırlatma ayrımı).
            // Ölçüm: 200 derslik listede try/catch deseni ~10 ms saf fırlatma maliyeti üretiyordu.
            CanComplete: SessionRules.CanRequestCompletion(s, me, now),
            CanApprove: SessionRules.CanApprove(s, me, asSystem: false),
            CanCancel: SessionRules.CanCancel(s, me, now),
            CanDispute: SessionRules.CanDispute(s, me, now));
    }
}
