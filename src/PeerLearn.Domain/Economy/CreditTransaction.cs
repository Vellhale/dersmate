using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Economy;

/// <summary>
/// Değiştirilemez (immutable, append-only) kredi hareket defteri.
/// Sıfır toplam garantisi: bir ders transferinin iki bacağı (öğrencide LessonSpending −N,
/// eğitmende LessonEarning +N) aynı CorrelationId'yi taşır ve toplamları daima 0'dır.
/// Sistem yalnızca WelcomeBonus ile kontrollü kredi üretir, Expiry ile yakar.
/// </summary>
public class CreditTransaction : BaseEntity
{
    public Guid WalletId { get; set; }

    /// <summary>Pozitif = giriş, negatif = çıkış. DB check: Amount &lt;&gt; 0.</summary>
    public int Amount { get; set; }

    public CreditTransactionType Type { get; set; }

    public Guid? RelatedSessionId { get; set; }

    /// <summary>Transferin karşı tarafındaki kullanıcı (varsa).</summary>
    public Guid? CounterpartyUserId { get; set; }

    /// <summary>Aynı atomik işlemin bacaklarını birbirine bağlar (denetim ve mutabakat için).</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>İşlem sonrası toplam bakiye (Available + Locked) — denetim izi.</summary>
    public int BalanceAfter { get; set; }
}
