namespace PeerLearn.Domain.Matchmaking;

public enum PortfolioDirection
{
    /// <summary>Verebileceğim ders/konu (arz).</summary>
    Offer = 0,

    /// <summary>Almak istediğim ders/konu (talep).</summary>
    Seek = 1
}

public enum MatchStatus
{
    Pending = 0,
    Accepted = 1,

    /// <summary>Muhatap isteği açıkça reddetti.</summary>
    Declined = 2,

    /// <summary>
    /// Muhatap hiç yanıt vermedi ve istek zaman aşımına uğradı (süpürücü yazar).
    /// Reddetmekten AYRI tutulur: reddetmek bir karardır, süre dolumu sessizliktir —
    /// ve ⚡ hızlı yanıt ortancası bu ikisini farklı değerlendirir.
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Taraflardan biri eşleşmeyi sonlandırdı. Sohbet OKUNABİLİR kalır ama yeni mesaj
    /// yazılamaz; yeni ders de rezerve edilemez.
    /// </summary>
    Closed = 4
}
