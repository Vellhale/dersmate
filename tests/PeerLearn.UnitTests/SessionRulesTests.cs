using PeerLearn.Application.Common;
using PeerLearn.Application.Scheduling;
using PeerLearn.Domain.Scheduling;
using Xunit;

namespace PeerLearn.UnitTests;

public class SessionRulesTests
{
    private static readonly Guid Tutor = Guid.NewGuid();
    private static readonly Guid Student = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

    private static LessonSession NewSession(SessionStatus status, DateTime endUtc) => new()
    {
        TutorUserId = Tutor,
        StudentUserId = Student,
        ScheduledStartUtc = endUtc.AddMinutes(-60),
        ScheduledEndUtc = endUtc,
        DurationMinutes = 60,
        Status = status,
        VerificationCode = "K7PMQ2XR"
    };

    // ---- TIME-LOCK ----

    [Fact]
    public void TimeLock_ders_bitmeden_tamamlamayi_engeller()
    {
        var session = NewSession(SessionStatus.Booked, endUtc: Now.AddMinutes(30));

        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanRequestCompletion(session, Tutor, Now));

        Assert.Equal(ErrorCodes.TimeLockActive, ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void TimeLock_bitis_aninda_ve_sonrasinda_acilir()
    {
        var session = NewSession(SessionStatus.Booked, endUtc: Now);
        SessionRules.EnsureCanRequestCompletion(session, Tutor, Now); // exception yok

        var later = NewSession(SessionStatus.Booked, endUtc: Now.AddHours(-1));
        SessionRules.EnsureCanRequestCompletion(later, Tutor, Now);
    }

    [Fact]
    public void Tamamlamayi_yalnizca_egitmen_isteyebilir()
    {
        var session = NewSession(SessionStatus.Booked, endUtc: Now.AddHours(-1));

        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanRequestCompletion(session, Student, Now));

        Assert.Equal(ErrorCodes.NotSessionParticipant, ex.Code);
    }

    [Fact]
    public void Booked_disinda_tamamlama_istenemez()
    {
        var session = NewSession(SessionStatus.AwaitingApproval, endUtc: Now.AddHours(-1));

        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanRequestCompletion(session, Tutor, Now));

        Assert.Equal(ErrorCodes.InvalidSessionState, ex.Code);
    }

    // ---- Doğrulama kodu ----

    [Theory]
    [InlineData("K7PMQ2XR")]
    [InlineData("  k7pmq2xr  ")] // trim + case-insensitive normalizasyon
    public void Dogru_kod_kabul_edilir(string provided)
    {
        var session = NewSession(SessionStatus.Booked, Now);
        SessionRules.EnsureVerificationCodeMatches(session, provided);
    }

    [Fact]
    public void Yanlis_kod_reddedilir()
    {
        var session = NewSession(SessionStatus.Booked, Now);

        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureVerificationCodeMatches(session, "WRONG123"));

        Assert.Equal(ErrorCodes.VerificationCodeMismatch, ex.Code);
    }

    // ---- Onay ----

    [Fact]
    public void Onayi_yalnizca_ogrenci_verebilir_sistem_haric()
    {
        var session = NewSession(SessionStatus.AwaitingApproval, Now);
        session.CompletionRequestedAtUtc = Now.AddHours(-50);

        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanApprove(session, Tutor, asSystem: false));
        Assert.Equal(ErrorCodes.NotSessionParticipant, ex.Code);

        SessionRules.EnsureCanApprove(session, Student, asSystem: false);
        SessionRules.EnsureCanApprove(session, null, asSystem: true); // otomatik onay job'ı
    }

    [Fact]
    public void Otomatik_onay_penceresi_dolmadan_veya_tamamlama_istegi_olmadan_calismaz()
    {
        var deadline = Now.AddHours(-48);

        // Sayaç henüz dolmamış (ör. Dismissed itiraz sonrası sıfırlanmış): sistem onaylayamaz.
        var fresh = NewSession(SessionStatus.AwaitingApproval, Now);
        fresh.CompletionRequestedAtUtc = Now.AddHours(-1);
        var ex1 = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanApprove(fresh, null, asSystem: true, systemApprovalDeadline: deadline));
        Assert.Equal(ErrorCodes.InvalidSessionState, ex1.Code);

        // Hiç tamamlama istenmemiş ders sistem tarafından ASLA onaylanamaz.
        var noRequest = NewSession(SessionStatus.AwaitingApproval, Now);
        var ex2 = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanApprove(noRequest, null, asSystem: true));
        Assert.Equal(ErrorCodes.InvalidSessionState, ex2.Code);

        // 48 saat gerçekten dolmuşsa geçer.
        var due = NewSession(SessionStatus.AwaitingApproval, Now);
        due.CompletionRequestedAtUtc = Now.AddHours(-50);
        SessionRules.EnsureCanApprove(due, null, asSystem: true, systemApprovalDeadline: deadline);

        // Öğrencinin kendi onayı sayaçtan bağımsızdır.
        SessionRules.EnsureCanApprove(fresh, Student, asSystem: false);
    }

    // ---- İptal / İtiraz ----

    [Fact]
    public void Ders_basladiktan_sonra_iptal_edilemez()
    {
        var session = NewSession(SessionStatus.Booked, endUtc: Now.AddMinutes(30)); // start = Now-30dk

        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanCancel(session, Student, Now));

        Assert.Equal(ErrorCodes.InvalidSessionState, ex.Code);
    }

    [Fact]
    public void Itiraz_onay_bekleyen_veya_saati_gecmis_ders_icin_acilir()
    {
        SessionRules.EnsureCanDispute(NewSession(SessionStatus.AwaitingApproval, Now.AddHours(1)), Student, Now);
        SessionRules.EnsureCanDispute(NewSession(SessionStatus.Booked, Now.AddHours(-1)), Student, Now);

        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanDispute(NewSession(SessionStatus.Booked, Now.AddHours(1)), Student, Now));
        Assert.Equal(ErrorCodes.InvalidSessionState, ex.Code);
    }

    // ---- Basılan puan (eğitmenin kazancı) ----

    [Theory]
    [InlineData(30, 50)]
    [InlineData(60, 100)]
    public void Her_30_dakika_50_puan_basar(int minutes, int expected)
        => Assert.Equal(expected, SessionRules.CalculateMintAmount(minutes));

    /// <summary>
    /// Ölçek 30 dakikalık BLOKLAR üzerinden: eksik blok puan üretmez.
    /// Bu, süre kümesi ileride genişletilirse (ör. 90 dk) yuvarlamanın sessizce puan
    /// üretmeyeceğinin garantisi.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(29, 0)]
    [InlineData(59, 50)]
    [InlineData(90, 150)]
    [InlineData(-60, 0)]
    public void Eksik_blok_puan_uretmez(int minutes, int expected)
        => Assert.Equal(expected, SessionRules.CalculateMintAmount(minutes));

    // ---- Süre kümesi ----

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void Izin_verilen_sureler_kabul_edilir(int minutes)
        => SessionRules.EnsureCanBook(Now, Now.AddHours(1), minutes);

    /// <summary>
    /// Süre bir ARALIK değil KÜME: 45 dakika eski "30-180 arası" kuralında geçerliydi,
    /// artık değil. Aralık bırakılsaydı 1,5 bloklu bir ders yuvarlama kararına kalırdı.
    /// </summary>
    [Theory]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(15)]
    public void Kume_disi_sureler_reddedilir(int minutes)
    {
        var ex = Assert.Throws<AppException>(() =>
            SessionRules.EnsureCanBook(Now, Now.AddHours(1), minutes));

        Assert.Equal(ErrorCodes.InvalidBooking, ex.Code);
    }
}
