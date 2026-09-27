using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Communication;

/// <summary>
/// Expo'nun kabul ettiği bir gönderimin bileti. Makbuz işi (15 dk sonra) bileti sorar:
/// DeviceNotRegistered dönerse cihaz silinir, her durumda bilet silinir.
/// </summary>
/// <remarks>
/// ⚠️ FK BİLEREK YOK (ne PushDevices'a ne Notifications'a). Gönderimle bilet yazımı
/// arasında cihaz satırı silinebilir (çıkış, hesap silme). FK olsaydı bilet INSERT'i ona
/// çarpar ve aynı yazımdaki diğer biletleri de geri alırdı. Silinmiş cihazın biletinin
/// makbuzu geldiğinde Id ile ExecuteDelete 0 satır etkiler; bu hata sayılmaz.
///
/// Expo makbuzları 24 saatte temizliyor; daha eski bilet sorulmadan silinir.
/// </remarks>
public class PushTicket : BaseEntity
{
    /// <summary>Expo bilet kimliği. Tekil.</summary>
    public string TicketId { get; set; } = null!;

    public Guid PushDeviceId { get; set; }

    public Guid? NotificationId { get; set; }
}
