namespace PeerLearn.Application.Common;

/// <summary>
/// İş kuralı ihlali. Api katmanındaki middleware bunu ProblemDetails'e çevirir;
/// Code alanı frontend'in hata mesajlarını lokalize etmesi içindir.
/// </summary>
public sealed class AppException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public AppException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}

public static class ErrorCodes
{
    // Identity
    public const string EmailTaken = "EMAIL_TAKEN";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
    public const string UserBanned = "USER_BANNED";
    public const string DeviceBanned = "DEVICE_BANNED";
    public const string InvalidToken = "INVALID_TOKEN";

    // Matchmaking
    public const string PortfolioDuplicate = "PORTFOLIO_DUPLICATE";
    public const string MatchNotFound = "MATCH_NOT_FOUND";
    public const string MatchNotPending = "MATCH_NOT_PENDING";
    public const string MatchNotAccepted = "MATCH_NOT_ACCEPTED";
    public const string NotMatchParticipant = "NOT_MATCH_PARTICIPANT";
    public const string DuplicateMatchRequest = "DUPLICATE_MATCH_REQUEST";
    public const string SelfMatch = "SELF_MATCH";
    public const string MatchHasActiveSessions = "MATCH_HAS_ACTIVE_SESSIONS";

    // Communication
    public const string ConversationAccessDenied = "CONVERSATION_ACCESS_DENIED";
    public const string MessageInvalid = "MESSAGE_INVALID";

    // Identity (cihaz)
    public const string HwidRequired = "HWID_REQUIRED";

    // Scheduling
    public const string SessionNotFound = "SESSION_NOT_FOUND";
    public const string NotSessionParticipant = "NOT_SESSION_PARTICIPANT";
    public const string InvalidSessionState = "INVALID_SESSION_STATE";
    public const string TimeLockActive = "TIME_LOCK_ACTIVE";
    public const string VerificationCodeMismatch = "VERIFICATION_CODE_MISMATCH";
    public const string ScheduleConflict = "SCHEDULE_CONFLICT";
    public const string InvalidBooking = "INVALID_BOOKING";
    public const string ProofInvalid = "PROOF_INVALID";

    // Economy
    /// <summary>
    /// ARTIK ÜRETİLMİYOR. Öğrenci ders için kredi ödemediğinden "yetersiz kredi" diye bir
    /// durum kalmadı. Sözleşmeden hemen düşürülmedi: istemci ve e2e betikleri bu kodu
    /// bekliyor olabilir; onlar temizlendikten sonra kaldırılacak.
    /// </summary>
    [Obsolete("Kredi kontrolü kaldırıldı; bu kod hiçbir yolda üretilmiyor.")]
    public const string InsufficientCredits = "INSUFFICIENT_CREDITS";

    /// <summary>Puan basımı tavanına takıldı (suistimal freni).</summary>
    public const string MintLimitReached = "MINT_LIMIT_REACHED";

    /// <summary>
    /// Hedef kullanıcı yok (404).
    ///
    /// NOT: eski yönetim uçları (BanUser, UnbanUser) aynı durumda <c>NotAuthorized</c>
    /// dönüyor — durum kodu 404 ama gövdedeki kod "yetkin yok" diyor. Yanlış ama yerleşmiş
    /// bir sözleşme; istemci ve e2e onu bekliyor olabileceği için burada DEĞİŞTİRİLMEDİ,
    /// yalnızca yeni uçlar doğrusunu kullanıyor.
    /// </summary>
    public const string UserNotFound = "USER_NOT_FOUND";

    /// <summary>
    /// Yönetim düşümü cüzdanın kullanılabilir bakiyesini aşıyor.
    ///
    /// <see cref="InsufficientCredits"/> ile KARIŞTIRILMAMALI: o kod "öğrencinin ders
    /// alacak kredisi yok" durumuydu ve artık üretilmiyor. Bu kod bir kullanıcı akışına
    /// değil, YÖNETİM işlemine ait — mesajın muhatabı da hakem panelidir.
    /// </summary>
    public const string AdjustmentExceedsBalance = "ADJUSTMENT_EXCEEDS_BALANCE";

    /// <summary>
    /// Aynı tekillik anahtarı FARKLI bir yükle tekrar kullanıldı (409).
    ///
    /// Sessizce ilk sonucu döndürmek burada kabul edilemez: yönetici tutarı düzeltip
    /// tekrar gönderdiyse, tekrar oynatma ESKİ tutarı uygular ve "uygulandı" der. Yani
    /// hata, kullanıcının düzeltmeye çalıştığı şeyin ta kendisini gizlerdi.
    /// </summary>
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string HoldAlreadyResolved = "HOLD_ALREADY_RESOLVED";
    public const string LockTimeout = "LOCK_TIMEOUT";

    // Genel doğrulama
    public const string ValidationFailed = "VALIDATION_FAILED";

    // Moderation
    public const string DisputeNotFound = "DISPUTE_NOT_FOUND";

    // Şikayet (tek yönlü). İtirazın yerini aldı; bkz. Domain/Moderation/Report.cs
    public const string ReportNotFound = "REPORT_NOT_FOUND";
    public const string ReportAlreadyExists = "REPORT_ALREADY_EXISTS";
    public const string DisputeAlreadyOpen = "DISPUTE_ALREADY_OPEN";
    public const string NotAuthorized = "NOT_AUTHORIZED";
}
