namespace PeerLearn.Domain.Communication;

/*
  Bu dosyadaki enum'ların hepsi veritabanında METİN olarak saklanıyor
  (HasConversion<string>). Yeni üye eklemek göç gerektirmez; bir üyeyi VERİSİNDEN ÖNCE
  silmek, o satırlar okunurken uzak bir yerde patlar (CLAUDE.md, dokunulmaz bölüm).
  Üye ADINI değiştirmek de silmekle aynı şeydir.
*/

/// <summary>Bildirim defterindeki satırın türü: hangi olay, hangi metin, hangi kanal.</summary>
/// <remarks>
/// Tür → kanal / tercih kategorisi / veri türü eşlemesi TEK yerde:
/// <c>PeerLearn.Application.Features.Communication.Bildirimler.BildirimKanallari</c>.
/// Yeni üye eklenirse oraya da eklenmeli; eşlemesi olmayan tür orada fırlatır ve
/// dağıtıcı satırı Failed yazar — sessizce yanlış kanala gitmesinden iyidir.
/// </remarks>
public enum NotificationType
{
    /// <summary>Sohbette yeni mesaj. İçerik push'a HİÇ girmez; yalnızca gönderenin adı.</summary>
    NewMessage = 0,

    /// <summary>Gelen arkadaş isteği. Gönderenin adı YAZILMAZ (yabancının serbest metni).</summary>
    MatchRequest = 1,

    /// <summary>Gönderilen isteğin kabul edilmesi. Ret bildirilmez: engeli sızdırırdı.</summary>
    MatchAccepted = 2,

    /// <summary>Düşmek üzere olan isteklerin günlük özeti (alıcı başına günde en fazla bir).</summary>
    MatchExpiringDigest = 3,

    /// <summary>Kanıt yüklendi (ya da itiraz reddedildi), öğrencinin onayı bekleniyor.</summary>
    ApprovalPending = 4,

    /// <summary>Otomatik onaya 24 / 2 saat kaldı.</summary>
    AutoApproveSoon = 5,

    /// <summary>Eğitmene: öğrenci yeni bir ders planladı.</summary>
    LessonBooked = 6,

    /// <summary>İptal etmeyen tarafa: ders iptal edildi.</summary>
    LessonCancelled = 7,

    /// <summary>Derse 60 / 10 dakika kaldı. Sessiz saatten ETKİLENMEZ.</summary>
    LessonSoon = 8,

    /// <summary>Kullanıcının kendi cihazlarına gönderdiği test bildirimi. Tercih süzgecinden muaf.</summary>
    Test = 9
}

/// <summary>Defter satırının yaşam döngüsü.</summary>
public enum NotificationStatus
{
    /// <summary>
    /// Kuyrukta. Varsayılan üye bilerek 0: yeni satır başka bir şey yazılmadıkça bekler.
    /// </summary>
    /// <remarks>
    /// ⚠️ Kısmi index <c>IX_Notifications_Bekleyen</c> filtresi <c>"Status" = 'Pending'</c>.
    /// Sahiplenme sorgusu bu koşulu BİREBİR içermeli; yoksa index sessizce devreden çıkar.
    /// </remarks>
    Pending = 0,

    /// <summary>En az bir cihaz için Expo bileti "ok" döndü.</summary>
    Sent = 1,

    /// <summary>Bilerek gönderilmedi; nedeni <see cref="NotificationOutcome"/>'ta.</summary>
    Skipped = 2,

    /// <summary>Gönderilemedi ve bir daha denenmeyecek; nedeni LastError'da (maskeli).</summary>
    Failed = 3
}

/// <summary>
/// Satırın NEDEN o durumda bittiği. Yalnızca teşhis ve test için; iş mantığı bu alana
/// bakarak karar vermez (karar gönderim anında yeniden hesaplanır).
/// </summary>
public enum NotificationOutcome
{
    /// <summary>Alıcı bu kategoriyi uygulama içinden kapatmış.</summary>
    TercihKapali = 0,

    /// <summary>Android: alıcının bağlı TÜM cihazlarında bu kanal telefon ayarlarından kapalı.</summary>
    KanalKapali = 1,

    /// <summary>Alıcının oturumu açık bağlı cihazı yok.</summary>
    CihazYok = 2,

    /// <summary>Cihazların tamamı geçersiz çıktı (ör. başka Expo projesinin token'ı).</summary>
    CihazGecersiz = 3,

    /// <summary>Taraflar arasında (herhangi bir yönde) engel var.</summary>
    Engel = 4,

    /// <summary>Alıcı ya da aktör artık Active değil (askı, ban, silinmiş hesap).</summary>
    HesapPasif = 5,

    /// <summary>Olayın konusu değişti: istek yanıtlandı, ders iptal edildi, damga yenilendi…</summary>
    DurumDegisti = 6,

    /// <summary>Mesaj gönderimden önce okundu.</summary>
    Okundu = 7,

    /// <summary>Aynı sohbetin daha yeni bir mesaj satırıyla birleştirildi.</summary>
    Birlestirildi = 8,

    /// <summary>Aynı çift için yakın zamanda bildirim gitti (istek freni).</summary>
    Tekrar = 9,

    /// <summary>ExpiresAtUtc geçti; geç gelen hatırlatma yanlış bilgidir.</summary>
    Bayat = 10
}

/// <summary>Push token'ının ait olduğu platform. Yük platforma göre farklı kurulur.</summary>
public enum PushPlatform
{
    Android = 0,
    Ios = 1
}
