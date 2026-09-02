namespace PeerLearn.Domain.Economy;

public enum CreditLotSource
{
    /// <summary>Tek seferlik hoş geldin kredisi (kontrollü "mint" — sistemin tek kredi üretme noktası).</summary>
    WelcomeBonus = 0,

    /// <summary>Ders anlatarak kazanılan kredi (30 gün vadeli).</summary>
    LessonEarning = 1,

    AdminGrant = 2,

    /// <summary>
    /// Topluluk katkısı: forumda alınan net oyun puana dönüşmesi
    /// (<see cref="PeerLearn.Domain.Community.CommunityRewardRules"/>).
    /// </summary>
    /// <remarks>
    /// AYRI KAYNAK, LessonEarning'e katılmadı: ekstrede "bu puan nereden geldi"
    /// sorusunun cevabı ayırt edilebilmeli. Birleştirilseydi ders kazancı gibi
    /// görünürdü ve ekonomiyi denetleyen biri ders sayısıyla puanı bağdaştıramazdı.
    ///
    /// Ders kazancı gibi SÜRESİZ (yanmıyor).
    /// </remarks>
    CommunityReward = 3
}

/// <remarks>
/// NEGATİF HAREKET ÜRETEN TEK YOL YÖNETİMDİR. Ders almak ücretsiz olduğu için öğrenci
/// tarafında hiçbir düşüm yok; geriye yalnızca vade süpürmesi (Expiry, eski vadeli lotlar)
/// ve yönetim düzeltmesi (AdminAdjustment) kalıyor.
///
/// <c>LessonSpending</c> KALDIRILDI: eski takas ekonomisinde öğrencinin harcama bacağıydı
/// ve ders ücretsizleştikten sonra hiçbir kod onu yazmıyordu — ama enum'da durduğu için
/// "puan geçmişi"nde eski satırlar görünmeye devam ediyor, kullanıcıya ders almanın bir
/// bedeli varmış izlenimi veriyordu.
/// </remarks>
public enum CreditTransactionType
{
    WelcomeBonus = 0,

    /// <summary>Ders anlatan tarafın kazancı (+). Tek bacaklıdır: karşı tarafta düşüm yoktur.</summary>
    LessonEarning = 1,

    /// <summary>30 günlük vadesi dolan kredinin silinmesi (−). Background job yazar.</summary>
    Expiry = 3,

    AdminAdjustment = 4,

    /// <summary>
    /// Topluluk katkısının puana dönüşmesi (+). Ders kazancı gibi tek bacaklıdır:
    /// kimseden düşülmez, basılır.
    /// </summary>
    /// <remarks>
    /// ⚠️ UNVAN SAYACINA GİRER. <see cref="PeerLearn.Domain.Identity.User.TotalEarnedCredits"/>
    /// eskiden yalnızca ders kazancıyla artıyordu ve iki test paketi bunu
    /// "TotalEarnedCredits == SUM(LessonEarning)" diye sınıyordu. Ürün sahibi kararıyla
    /// (2026-08-29) topluluk katkısı da seviyeye sayıldığı için değişmez KIRILMADI,
    /// GENİŞLEDİ:
    ///
    ///     TotalEarnedCredits == SUM(LessonEarning) + SUM(CommunityReward)
    ///
    /// Testler bu yeni hâle güncellendi. Yeni bir basım türü eklenirse o toplama da
    /// eklenmeli — aksi halde denetim, gerçek bir sapmayı değil eksik sorguyu bulur.
    /// </remarks>
    CommunityReward = 5
}
