using PeerLearn.Application.Common;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Scheduling;

/// <summary>
/// Ders oturumu durum makinesi kuralları. Saf fonksiyonlar — Time-Lock dahil tüm
/// geçiş kuralları burada test edilebilir halde toplanır; handler'lar yalnızca çağırır.
/// </summary>
public static class SessionRules
{
    /// <summary>
    /// Seçilebilecek ders süreleri. ARALIK DEĞİL, KÜME.
    /// </summary>
    /// <remarks>
    /// Önceki kural "30-180 arası" idi ve 45 dk gibi ara değerlere de izin veriyordu.
    /// Ödül artık 30 dakikalık bloklar üzerinden hesaplandığı için süre kümesi de
    /// bloklara oturmalı: aralık bırakılsaydı 45 dakikalık bir ders 1,5 blok eder ve
    /// yuvarlama kararı sessizce puan üretir ya da yakardı.
    /// </remarks>
    public static readonly int[] AllowedDurations = [30, 60];

    /// <summary>Bir ödül bloğu (30 dakika) karşılığı basılan puan.</summary>
    public const int MintPerBlock = 50;

    /// <summary>Bir ödül bloğunun dakika cinsinden uzunluğu.</summary>
    public const int MintBlockMinutes = 30;

    /// <summary>
    /// Ders onaylandığında EĞİTMENE basılacak puan: her 30 dakika için 50.
    /// 30 dk → 50, 60 dk → 100.
    /// </summary>
    /// <remarks>
    /// Adı bilerek "Cost" değil "Mint": bu tutar kimseden TAHSİL EDİLMİYOR, yoktan
    /// üretiliyor. Eski ad (CalculateCreditCost) korunsaydı, kodu okuyan herkes hâlâ
    /// bir öğrencinin ödediğini sanırdı — bu projede en pahalı hatalar tam olarak
    /// adı değişmemiş kavramlardan çıktı.
    /// </remarks>
    public static int CalculateMintAmount(int durationMinutes)
        => Math.Max(0, durationMinutes) / MintBlockMinutes * MintPerBlock;

    public static void EnsureCanBook(DateTime nowUtc, DateTime startUtc, int durationMinutes)
    {
        if (!AllowedDurations.Contains(durationMinutes))
        {
            throw new AppException(ErrorCodes.InvalidBooking,
                $"Ders süresi yalnızca {string.Join(" veya ", AllowedDurations)} dakika olabilir.");
        }

        if (startUtc <= nowUtc)
        {
            throw new AppException(ErrorCodes.InvalidBooking, "Ders başlangıcı gelecekte olmalı.");
        }
    }

    /// <summary>
    /// TIME-LOCK: "Dersi Tamamladım" ancak planlanan bitiş saatinden SONRA tetiklenebilir.
    /// Bu kural sunucu saatiyle çalışır; istemciden gelen hiçbir zaman bilgisine güvenilmez.
    /// </summary>
    public static void EnsureTimeLockPassed(DateTime nowUtc, LessonSession session)
    {
        if (nowUtc < session.ScheduledEndUtc)
        {
            var remaining = (int)Math.Ceiling((session.ScheduledEndUtc - nowUtc).TotalMinutes);
            throw new AppException(ErrorCodes.TimeLockActive,
                $"Time-Lock aktif: ders bitişine {remaining} dakika var. Tamamlama, planlanan bitişten önce yapılamaz.",
                statusCode: 409);
        }
    }

    /*
      İHLAL ÜRETEN + FIRLATAN AYRIMI
      Her kuralın TEK uygulaması "ihlal varsa AppException NESNESİ döndürür, yoksa null"
      biçimindedir. Ensure* onu fırlatır, Can* yalnızca null mı diye bakar.

      Neden: "Derslerim" listesi her satır için dört kuralı da sorguluyordu ve bunu
      try/catch ile yapmak satır başına ~4 exception demekti (ölçüldü: 200 derslik
      listede ~10 ms saf fırlatma/stack-unwind maliyeti + GC baskısı). Kuralları
      kopyalamak yerine fırlatmayı ayırmak hem maliyeti sıfırlar hem tek kaynağı korur:
      exception NESNESİ oluşturmak ucuzdur, pahalı olan fırlatmaktır.
    */

    public static void EnsureCanRequestCompletion(LessonSession session, Guid callerUserId, DateTime nowUtc)
    {
        var violation = CompletionViolation(session, callerUserId, nowUtc);
        if (violation is not null) throw violation;
    }

    public static bool CanRequestCompletion(LessonSession session, Guid callerUserId, DateTime nowUtc)
        => CompletionViolation(session, callerUserId, nowUtc) is null;

    private static AppException? CompletionViolation(LessonSession session, Guid callerUserId, DateTime nowUtc)
    {
        if (session.TutorUserId != callerUserId)
        {
            return new AppException(ErrorCodes.NotSessionParticipant,
                "Yalnızca dersi anlatan eğitmen tamamlama isteği gönderebilir.", statusCode: 403);
        }

        if (session.Status != SessionStatus.Booked)
        {
            return new AppException(ErrorCodes.InvalidSessionState,
                $"Ders '{session.Status}' durumunda; tamamlama yalnızca Booked durumunda istenebilir.", statusCode: 409);
        }

        if (nowUtc < session.ScheduledEndUtc)
        {
            var remaining = (int)Math.Ceiling((session.ScheduledEndUtc - nowUtc).TotalMinutes);
            return new AppException(ErrorCodes.TimeLockActive,
                $"Time-Lock aktif: ders bitişine {remaining} dakika var. Tamamlama, planlanan bitişten önce yapılamaz.",
                statusCode: 409);
        }

        return null;
    }

    public static void EnsureVerificationCodeMatches(LessonSession session, string providedCode)
    {
        var normalized = (providedCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.Equals(session.VerificationCode, normalized, StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.VerificationCodeMismatch,
                "Doğrulama kodu (Session ID) eşleşmiyor.", statusCode: 409);
        }
    }

    /// <param name="systemApprovalDeadline">
    /// AsSystem (otomatik onay) çağrılarında zorunlu: yalnızca CompletionRequestedAtUtc bu
    /// eşikten ESKİ dersler otomatik onaylanabilir. Taze okuma üzerinde yeniden doğrulanır ki
    /// snapshot ile transaction arasında sayaç sıfırlanmışsa (ör. Dismissed itiraz) öğrencinin
    /// yenilenen onay penceresi atlanmasın.
    /// </param>
    public static void EnsureCanApprove(
        LessonSession session, Guid? approverUserId, bool asSystem, DateTime? systemApprovalDeadline = null)
    {
        var violation = ApprovalViolation(session, approverUserId, asSystem, systemApprovalDeadline);
        if (violation is not null) throw violation;
    }

    public static bool CanApprove(LessonSession session, Guid? approverUserId, bool asSystem)
        => ApprovalViolation(session, approverUserId, asSystem, systemApprovalDeadline: null) is null;

    private static AppException? ApprovalViolation(
        LessonSession session, Guid? approverUserId, bool asSystem, DateTime? systemApprovalDeadline)
    {
        if (!asSystem && session.StudentUserId != approverUserId)
        {
            return new AppException(ErrorCodes.NotSessionParticipant,
                "Yalnızca dersi alan öğrenci onay verebilir.", statusCode: 403);
        }

        if (session.Status != SessionStatus.AwaitingApproval)
        {
            return new AppException(ErrorCodes.InvalidSessionState,
                $"Ders '{session.Status}' durumunda; onay yalnızca AwaitingApproval durumunda verilebilir.", statusCode: 409);
        }

        if (asSystem &&
            (session.CompletionRequestedAtUtc is null ||
             (systemApprovalDeadline is not null && session.CompletionRequestedAtUtc > systemApprovalDeadline)))
        {
            return new AppException(ErrorCodes.InvalidSessionState,
                "Otomatik onay penceresi henüz dolmadı.", statusCode: 409);
        }

        return null;
    }

    public static void EnsureCanCancel(LessonSession session, Guid callerUserId, DateTime nowUtc)
    {
        var violation = CancelViolation(session, callerUserId, nowUtc);
        if (violation is not null) throw violation;
    }

    public static bool CanCancel(LessonSession session, Guid callerUserId, DateTime nowUtc)
        => CancelViolation(session, callerUserId, nowUtc) is null;

    private static AppException? CancelViolation(LessonSession session, Guid callerUserId, DateTime nowUtc)
    {
        if (session.TutorUserId != callerUserId && session.StudentUserId != callerUserId)
        {
            return new AppException(ErrorCodes.NotSessionParticipant, "Bu dersin tarafı değilsiniz.", statusCode: 403);
        }

        if (session.Status != SessionStatus.Booked)
        {
            return new AppException(ErrorCodes.InvalidSessionState,
                "Yalnızca Booked durumundaki ders iptal edilebilir.", statusCode: 409);
        }

        if (nowUtc >= session.ScheduledStartUtc)
        {
            return new AppException(ErrorCodes.InvalidSessionState,
                "Ders saati başladıktan sonra iptal edilemez; sorun varsa itiraz açın.", statusCode: 409);
        }

        return null;
    }

    public static void EnsureCanDispute(LessonSession session, Guid callerUserId, DateTime nowUtc)
    {
        var violation = DisputeViolation(session, callerUserId, nowUtc);
        if (violation is not null) throw violation;
    }

    public static bool CanDispute(LessonSession session, Guid callerUserId, DateTime nowUtc)
        => DisputeViolation(session, callerUserId, nowUtc) is null;

    private static AppException? DisputeViolation(LessonSession session, Guid callerUserId, DateTime nowUtc)
    {
        if (session.StudentUserId != callerUserId)
        {
            return new AppException(ErrorCodes.NotSessionParticipant,
                "İtirazı yalnızca dersi alan öğrenci açabilir.", statusCode: 403);
        }

        // İki geçerli senaryo: (1) eğitmen tamamlama istedi, öğrenci itiraz ediyor;
        // (2) ders saati geçti ama hiç yapılmadı (eğitmen tamamlama istemeden "yapıldı" iddiası yoksa
        //     öğrenci beklemek zorunda kalmasın).
        var valid = session.Status == SessionStatus.AwaitingApproval
                    || (session.Status == SessionStatus.Booked && nowUtc >= session.ScheduledEndUtc);

        if (!valid)
        {
            return new AppException(ErrorCodes.InvalidSessionState,
                "İtiraz yalnızca onay bekleyen veya saati geçmiş dersler için açılabilir.", statusCode: 409);
        }

        return null;
    }
}
