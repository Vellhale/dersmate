namespace PeerLearn.Domain.Identity;

/// <summary>
/// Yasal metinlerin (kullanım koşulları + gizlilik) YÜRÜRLÜKTEKİ sürümü.
/// </summary>
/// <remarks>
/// ⚠️ SÜRÜM İSTEMCİDEN GELMİYOR, İSTEMCİYLE KARŞILAŞTIRILIYOR.
///
/// Kayıt isteği kabul edilen sürümü taşıyor ama kaydedilen değer BU sabit. İstemcinin
/// gönderdiği sürümün tek işi eşitlik kontrolü: farklıysa kayıt reddediliyor.
///
/// Sebebi somut: kullanıcının tarayıcısında önbellekten gelen ESKİ bir arayüz olabilir.
/// O arayüz eski metni gösterip "kabul ediyorum" dedirtir; sunucu istemcinin dediğine
/// inansaydı, kullanıcının hiç görmediği yeni metne onay verdiği kaydedilirdi — ya da
/// tam tersi, artık yürürlükte olmayan bir metne verilen onay geçerli sayılırdı.
/// İkisi de kanıt değeri olmayan bir kayıt üretir.
///
/// Ayrıca istemciden gelen bir dizgeyi doğrudan yazmak, isteyenin "v0.1" yazıp
/// onaysız hesap açmasına kapı bırakırdı.
///
/// ─── SÜRÜM DEĞİŞTİRİRKEN ───────────────────────────────────────────────────
/// Metin değiştiğinde burası VE arayüzdeki karşılığı (frontend/src/lib/yasalMetinler.js)
/// birlikte artmalı. İkisi ayrışırsa hiç kimse kayıt olamaz — gürültülü bir hata ve
/// bu bilinçli: sessizce yanlış sürümü kaydetmektense kaydı durdurmak yeğdir.
///
/// Mevcut kullanıcıların eski onayı OTOMATİK GEÇERSİZLEŞMEZ; onlara yeniden onay
/// göstermek ayrı bir akış (henüz yok — bkz. docs/DEVAM-EDILECEK.md).
/// </remarks>
public static class LegalDocuments
{
    /// <summary>
    /// Sürüm = metnin yürürlük tarihi. Artan bir sayaç değil çünkü kullanıcıya gösterilen
    /// şey de tarih ("Son güncelleme: 27 Ağustos 2026"); iki ayrı kimlik tutmak, birinin
    /// diğerinden ayrışması demekti.
    /// </summary>
    /// <remarks>
    /// ⚠️ MOBİL PAKETE GÖMÜLÜ BİR KOPYASI VAR ve o dağıtımla güncellenmez.
    ///
    /// Web'de sürüm, arayüz yeniden derlendiği için dağıtımla birlikte hizalanır.
    /// Mobilde kullanıcının telefonundaki APK eski sabiti taşır; burası artıp o
    /// güncellenmeyince eşitlik kontrolü düşer ve o kullanıcı KAYIT OLAMAZ.
    ///
    /// Sıra: mobil deposundaki src/lib/yasalMetinler.js artır → yeni APK yayınla →
    /// sonra bu değeri dağıt.
    ///
    /// 2026-09-05: gizlilik metnine veri işleyenler, yurt dışına aktarım ve yedek
    /// saklama süresi eklendi (KVKK m.9/m.10).
    /// </remarks>
    public const string CurrentVersion = "2026-09-05";
}
