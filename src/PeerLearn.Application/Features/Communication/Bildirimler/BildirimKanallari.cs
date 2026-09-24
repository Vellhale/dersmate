using PeerLearn.Domain.Communication;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Bildirim türü → Android kanalı / tercih kategorisi / veri türü / öncelik eşlemesinin
/// TEK kaynağı.
/// </summary>
/// <remarks>
/// ⛔ KANAL KİMLİKLERİ MOBİLLE BİREBİR AYNI (dersmate Mobil → src/lib/bildirimler.js).
/// Android kanalı cihazda uygulamanın oluşturduğu kimlikle yaşar; sunucu tanımadığı bir
/// kanal kimliği gönderirse Android bildirimi SESSİZCE düşürür (hata yok, günlük yok).
/// Kimlik değişecekse iki tarafta aynı gün değişir ve eski kanal mobilde silinir.
///
/// Tercih kategorileri kanallarla bire bir: dört kanal, dört anahtar. Bu yüzden kategori
/// kimliği (PUT /push/preferences/{kategori}) de kanal kimliğinin aynısı.
///
/// ⚠️ Önem (importance) mobilde kanal oluşturulurken sabitlenir ve SONRADAN DEĞİŞMEZ;
/// buradaki öncelik yalnızca teslim önceliği (FCM "high"/"normal").
/// </remarks>
public static class BildirimKanallari
{
    public const string Mesajlar = "mesajlar";
    public const string Istekler = "istekler";
    public const string DersOnayi = "ders-onayi";
    public const string DersPlani = "ders-plani";

    /// <summary>Bilinen dört kanal; kayıt ucu kapalı kanal listesini buna göre süzer.</summary>
    public static readonly IReadOnlyList<string> Hepsi = [Mesajlar, Istekler, DersOnayi, DersPlani];

    public static bool Bilinen(string? kanal) => kanal is not null && Hepsi.Contains(kanal, StringComparer.Ordinal);

    /// <summary>
    /// Türün kanalı. Test türünün kanalı yoktur; kullanıcı hangi türü denemek istediyse o
    /// türün kanalı kullanılır (<paramref name="testKanali"/>, anahtardan: BildirimAnahtarlari.TestKanali).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Eşlemesi olmayan tür. Sessizce yanlış kanala gitmesin.</exception>
    public static string Kanal(NotificationType tur, string? testKanali = null) => tur switch
    {
        NotificationType.NewMessage => Mesajlar,
        NotificationType.MatchRequest or NotificationType.MatchAccepted or NotificationType.MatchExpiringDigest => Istekler,
        NotificationType.ApprovalPending or NotificationType.AutoApproveSoon => DersOnayi,
        NotificationType.LessonBooked or NotificationType.LessonCancelled or NotificationType.LessonSoon => DersPlani,
        NotificationType.Test when Bilinen(testKanali) => testKanali!,
        _ => throw new ArgumentOutOfRangeException(nameof(tur), tur, "Bu bildirim türünün kanalı tanımlı değil.")
    };

    /// <summary>
    /// Alıcının bu tür için tercihi açık mı? Satır yoksa (<paramref name="tercih"/> null)
    /// tembel varsayılan: dördü açık. Test türü tercih süzgecinden MUAF — kullanıcı "neden
    /// bildirim gelmiyor" diye bakarken kapattığı kategori testi de susturmamalı.
    /// </summary>
    /// <remarks>Dağıtıcı bunu GÖNDERİM ANINDA çağırır: kapatılan kategoride kuyrukta bekleyen de gitmez.</remarks>
    public static bool TercihAcik(NotificationPreference? tercih, NotificationType tur)
    {
        if (tur == NotificationType.Test || tercih is null)
        {
            return true;
        }

        return Kanal(tur) switch
        {
            Mesajlar => tercih.Messages,
            Istekler => tercih.Requests,
            DersOnayi => tercih.LessonApproval,
            DersPlani => tercih.LessonPlan,
            _ => true
        };
    }

    /// <summary>
    /// PUT /push/preferences/{kategori} için kolon adı. Bilinmeyen kategori → null (uç 404/400 döner).
    /// </summary>
    /// <remarks>
    /// ⛔ Tek sütunluk upsert kolon adını SQL'e METİN olarak koymak zorunda (tanımlayıcı
    /// parametre olamaz). Bu yüzden kolon adı YALNIZCA bu sabit beyaz listeden gelir;
    /// istekten gelen dizge asla doğrudan SQL'e girmez.
    /// </remarks>
    public static string? TercihSutunu(string? kategori) => kategori switch
    {
        Mesajlar => nameof(NotificationPreference.Messages),
        Istekler => nameof(NotificationPreference.Requests),
        DersOnayi => nameof(NotificationPreference.LessonApproval),
        DersPlani => nameof(NotificationPreference.LessonPlan),
        _ => null
    };

    /// <summary>
    /// Test ucunun "tur" alanı → kanal. Mobil ayarlar ekranı bu dört değeri gönderir.
    /// Bilinmeyen değer → null (uç 400 döner).
    /// </summary>
    public static string? TestKanali(string? testTuru) => testTuru switch
    {
        "mesaj" => Mesajlar,
        "istek" => Istekler,
        "onay" => DersOnayi,
        "ders" => DersPlani,
        _ => null
    };

    /// <summary>
    /// Teslim önceliği. "high" Doze'daki cihazı uyandırır; FCM onu zamana duyarlı iletiler
    /// için ayırmamızı istiyor. İstekler acil değil (kanalı da DEFAULT önemde): "normal".
    /// Mesaj, onay ve ders planı zamana duyarlı.
    /// </summary>
    public static string Oncelik(string kanal) => kanal == Istekler ? "normal" : "high";

    /// <summary>
    /// iOS threadId (bildirim merkezinde gruplama). Mesajlarda sohbet başına grup, yani
    /// etiketin kendisi (null döner, çağıran etiketi kullanır); diğerlerinde kategori grubu.
    /// </summary>
    public static string? IosGrubu(string kanal) => kanal switch
    {
        Mesajlar => null,
        Istekler => "istekler",
        _ => "dersler"
    };

    /// <summary>
    /// data.tur: mobilin tanıdığı tür adı. Mobil bununla hangi ekranı tazeleyeceğine ve
    /// açık sohbette banner'ı bastıracağına karar veriyor.
    /// </summary>
    /// <remarks>⛔ Mobildeki karşılıklarla aynı kalmalı; burada yeniden adlandırmak mobilde sessizce eşleşmeyi bozar.</remarks>
    public static string VeriTuru(NotificationType tur) => tur switch
    {
        NotificationType.NewMessage => "mesaj",
        NotificationType.MatchRequest => "istek",
        NotificationType.MatchAccepted => "istekKabul",
        NotificationType.MatchExpiringDigest => "istekDusecek",
        NotificationType.ApprovalPending => "onay",
        NotificationType.AutoApproveSoon => "otoOnay",
        NotificationType.LessonBooked => "dersPlan",
        NotificationType.LessonCancelled => "dersIptal",
        NotificationType.LessonSoon => "dersYaklasiyor",
        NotificationType.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(tur), tur, "Bu bildirim türünün veri türü tanımlı değil.")
    };
}
