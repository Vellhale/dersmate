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
    ///
    /// 2026-09-19: İKİ DEĞİŞİKLİK TEK ARTIŞTA.
    ///   1. Veri sorumlusunun kimliği metne yazıldı — dersmate'i Corventech işletiyor
    ///      (Gizlilik §1, Koşullar §1 ve her iki sayfanın altındaki künye). Metinler
    ///      bugüne kadar "biz" diyordu ama muhatabı adlandırmıyordu; KVKK m.10'un
    ///      istediği ilk bilgi budur.
    ///   2. Arkadaş sayısı + ortak arkadaşlar ifşası (Gizlilik §6). 2026-09-10'da
    ///      metne eklenmiş ama sürümü artırılamamıştı — borç Gizlilik.jsx başında
    ///      kayıtlıydı, burada kapandı.
    ///
    /// Ayrı ayrı artırmak mobil tarafta iki mağaza yayını demekti; aynı gün yürürlüğe
    /// giren iki değişiklik tek kapıdan geçirildi.
    ///
    /// ⚠️ MOBİL DEPODAKİ KOPYA BU DEĞERE ÇEKİLMEDEN YENİ APK YAYINLANMAMALI.
    /// Mobil uygulama henüz mağazada olmadığı için bugün kullanıcıya dokunan bir
    /// kırılma yok: kapı ilk yayından ÖNCE kapanıyor. Yayına çıkmış bir APK varken
    /// aynı işlem yapılsaydı, güncellemeyi almamış herkes kayıt ekranında kilitlenirdi.
    /// (Mobil kopya 2026-09-21'de bu değere çekildi.)
    ///
    /// 2026-09-25: PUSH BİLDİRİMLERİ — yeni bir ifşa, artması ZORUNLUYDU. Üç şey birden
    /// geldi: yeni veri türü (telefonun bildirim adresi, bildirim tercihleri, bildirim
    /// defteri — Domain/Communication), yeni alıcılar (Expo, Google FCM, Apple APNs) ve
    /// onlarla birlikte yeni bir yurt dışı aktarım. Gizlilik metni (web Gizlilik.jsx, mobil
    /// app/gizlilik.jsx) ve silinecekler listeleri AYNI turda yazıldı; sayı metinsiz artmadı.
    ///
    /// ÜÇ YER AYNI DEĞERE, AYNI DALDA çekildi: burası, web frontend/src/lib/yasalMetinler.js
    /// ve mobil src/lib/yasalMetinler.js. Yukarıdaki "önce mobil yayın, sonra sunucu" sırası
    /// bu kez GEREKMEDİ — mağazada henüz uygulama yok, eski sabiti gönderecek kurulu bir paket
    /// de yok. Mağazaya ilk çıkış bu değerle olacak; ondan sonraki her artışta sıra yeniden
    /// "önce mobil yayın".
    ///
    /// Mevcut kullanıcılar yeniden onaylatılmıyor (sürüm yalnızca yeni kayıtları kapsar).
    /// Push'un aydınlatması mobil uygulamada veri akışından ÖNCE yapılıyor ve cihaz kaydı
    /// ucu <c>NotificationPreference.DisclosureShownAtUtc</c> olmadan hiçbir şey yazmıyor.
    /// </remarks>
    public const string CurrentVersion = "2026-09-25";
}
