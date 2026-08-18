namespace PeerLearn.Domain.Economy;

public enum CreditLotSource
{
    /// <summary>Tek seferlik hoş geldin kredisi (kontrollü "mint" — sistemin tek kredi üretme noktası).</summary>
    WelcomeBonus = 0,

    /// <summary>Ders anlatarak kazanılan kredi (30 gün vadeli).</summary>
    LessonEarning = 1,

    AdminGrant = 2
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

    AdminAdjustment = 4
}
