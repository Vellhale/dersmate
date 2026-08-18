namespace PeerLearn.Domain.Moderation;

public enum DisputeReason
{
    /// <summary>"Ders yapılmadı" itirazı.</summary>
    SessionNotHeld = 0,

    /// <summary>"Sahte ekran görüntüsü yüklendi" itirazı.</summary>
    FakeProof = 1,

    DurationMismatch = 2,
    Abuse = 3,
    Other = 4
}

public enum DisputeStatus
{
    Open = 0,
    UnderReview = 1,

    /// <summary>Eğitmen haklı bulundu: bloke kredi eğitmene transfer edilir.</summary>
    ResolvedForTutor = 2,

    /// <summary>Öğrenci haklı bulundu: bloke kredi öğrenciye iade edilir, gerekirse yaptırım uygulanır.</summary>
    ResolvedForStudent = 3,

    Dismissed = 4
}

public enum SanctionType
{
    Warning = 0,
    TemporaryBan = 1,
    PermanentBan = 2
}
