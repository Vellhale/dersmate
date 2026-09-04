namespace PeerLearn.Domain.Identity;

public enum UserStatus
{
    /// <summary>Kayıt oldu, e-posta/SMS doğrulaması bekleniyor. Hoş geldin kredisi henüz verilmez.</summary>
    PendingVerification = 0,
    Active = 1,
    Suspended = 2,
    Banned = 3,

    /// <summary>
    /// Kullanıcı hesabını KENDİSİ sildi (Google Play'in hesap silme politikası ve
    /// KVKK/GDPR silme hakkı gereği).
    ///
    /// SATIR DURUYOR, KİŞİSEL VERİ SİLİNİYOR. Users satırını gerçekten silmek mümkün
    /// değil: 23 yabancı anahtar buraya bakıyor ve çoğu RESTRICT — ders oturumları,
    /// eşleşmeler, değerlendirmeler, kredi defteri ve moderasyon kayıtları KARŞI TARAFA
    /// ait. Silmek, hiç silinmemesi gereken başkasının geçmişini yok ederdi.
    ///
    /// Bunun yerine kimlik alanları temizlenip satır mezar taşına çevriliyor
    /// (bkz. DeleteAccountHandler): ad "Silinmiş kullanıcı", e-posta kullanılamaz bir
    /// yer tutucu, parola özeti kullanılamaz hâle getiriliyor.
    /// </summary>
    Deleted = 4
}

/// <summary>
/// RBAC rolü. Tek kolon (çoklu rol tablosu değil) çünkü bu üründe roller HİYERARŞİK ve
/// birbirini dışlar: bir kullanıcı aynı anda hem öğrenci hem moderatör "gibi" davranmaz,
/// moderatör zaten öğrencinin yapabildiği her şeyi yapabilir.
///
/// Yetki ayrımı (bkz. Api/Authorization/Policies):
///   Student   — kendi dersleri, portföyü, cüzdanı.
///   Moderator — itiraz kuyruğunu görür ve KARARA bağlar; ban veremez, rol değiştiremez.
///   Admin     — hepsi + kalıcı ban, HWID ban, rol atama, ekonomi metrikleri.
///
/// Ban yetkisinin moderatörden ayrılması bilinçli: kalıcı ban geri alınması en pahalı
/// işlem ve tek kişilik bir kararla verilmemeli.
/// </summary>
public enum UserRole
{
    Student = 0,
    Moderator = 1,
    Admin = 2
}

/// <summary>
/// Çerez rızası (KVKK/GDPR). Üç durumlu olmak ZORUNDA: "henüz sorulmadı" ile "reddetti"
/// aynı şey değildir — birincisinde banner gösterilir, ikincisinde gösterilmez ve script
/// yüklenmez. İki durumlu bir bool bu ayrımı kaybeder ve kullanıcıya her açılışta banner
/// göstermek (ya da hiç göstermemek) zorunda kalırdık.
/// </summary>
public enum CookieConsentState
{
    NotAsked = 0,
    Granted = 1,
    Denied = 2
}

/// <summary>
/// Öğretmen adaylığı beyanının hakem durumu. İki durum (doğrulandı/doğrulanmadı) yetmez:
/// "henüz bakılmadı" ile "bakıldı, kabul edilmedi" moderatör için de kullanıcı için de
/// farklı şeylerdir — birincisi kuyrukta bekler, ikincisi bir karardır ve gerekçesi vardır.
/// </summary>
public enum TeacherCandidateReviewStatus
{
    Pending = 0,
    Verified = 1,
    Rejected = 2
}
