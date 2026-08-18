using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Economy;

/// <summary>
/// Hangi lottan ne kadar düşüldüğünün izi (append-only). Tüketen her zaman bir
/// <see cref="CreditTransaction"/>'dır: vade süpürmesi (Expiry) ya da yönetim düşümü
/// (AdminAdjustment).
/// </summary>
/// <remarks>
/// ESKİDEN İKİ TÜKETİCİ VARDI. İkincisi <c>CreditHoldId</c> idi: rezervasyon anında FIFO
/// bloke edilen miktar. Bloke iadesi de burada <c>IsReversal=true</c> satırıyla tutulurdu.
/// Ders almak ücretsizleşince bloke diye bir şey kalmadı; iki alan da hiçbir kod tarafından
/// yazılmıyordu ama check constraint'ler ("tam olarak biri dolu") ve kısmi unique index
/// hâlâ onlara göre kuruluydu — yani ölü bir kavram şemanın şeklini belirlemeye devam
/// ediyordu. İkisi de kaldırıldı.
///
/// Lot denetimi artık düz: Initial − Remaining == SUM(Amount).
/// </remarks>
public class CreditLotConsumption : BaseEntity
{
    public Guid CreditLotId { get; set; }
    public int Amount { get; set; }
    public Guid CreditTransactionId { get; set; }
}
