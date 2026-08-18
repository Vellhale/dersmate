namespace PeerLearn.Domain.Scheduling;

public enum SessionStatus
{
    /// <summary>Rezerve edildi; öğrencinin kredisi bloke (hold) durumda.</summary>
    Booked = 0,

    /// <summary>Time-lock doldu, eğitmen "Dersi Tamamladım" dedi ve kanıt yükledi; öğrenci onayı bekleniyor.</summary>
    AwaitingApproval = 1,

    /// <summary>Öğrenci onayladı (veya otomatik onay süresi doldu); kredi transferi tamamlandı.</summary>
    Completed = 2,

    Disputed = 3,
    Cancelled = 4,

    /// <summary>Ders saati geçti ama eğitmen hiç tamamlama talebinde bulunmadı; hold serbest bırakıldı.</summary>
    Expired = 5
}

public enum ProofStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}
