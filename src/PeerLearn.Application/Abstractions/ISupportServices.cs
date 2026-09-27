using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Abstractions;

/// <summary>Test edilebilirlik için sistem saati soyutlaması (Time-Lock kuralı buna bağlı).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Dağıtık kilit. Redis implementasyonu (production) veya tek-instance in-process fallback.
/// Aynı cüzdana eşzamanlı yazmaları serileştirir; xmin optimistic concurrency son savunmadır.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>Kilit alınamazsa AppException(LockTimeout) fırlatır.</summary>
    Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// Salt okunur sorgu sonuçları için önbellek (Redis; yoksa süreç içi).
///
/// KİLİTTEN FARKI — ve bu fark neden önemli: kilit sağlayıcısında süreç içi yedeğe düşmek
/// TEHLİKELİDİR (çok instance'ta yarış koşulu doğar). Burada değildir: önbellek yalnızca
/// bir hızlandırmadır, kaçırılması ya da bayat kalması en fazla eski bir liste gösterir.
/// Bu yüzden Redis yoksa uyarı vermeden süreç içi belleğe düşer.
///
/// KURAL: doğruluğu önbelleğe BAĞLI hiçbir şey buraya konmaz — bakiye, kilit durumu, yetki.
/// Yalnızca "biraz bayat olabilir" denebilecek keşif/katalog verisi.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Anahtar yoksa <paramref name="factory"/> çalıştırılır, sonucu <paramref name="ttl"/>
    /// süresince saklanır. Önbellek erişilemezse sessizce factory'ye düşer — Redis'in
    /// kapalı olması aramayı ÇALIŞMAZ hâle getirmemeli.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default) where T : class;

    /// <summary>Önek altındaki tüm anahtarları düşürür (ör. katalog değişince "search:").</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);

    /// <summary>
    /// Parolayı doğrular; doğruysa VE saklanan karma güncel iş faktöründen (iterasyon
    /// sayısı) daha zayıf üretilmişse, <paramref name="yenilenmisKarma"/> ile güncel
    /// faktörle üretilmiş taze bir karma döner (doğrulamada yükselt / rehash-on-verify).
    /// </summary>
    /// <remarks>
    /// ⚠️ ÇAĞIRAN, <paramref name="yenilenmisKarma"/> null DEĞİLSE onu KALICILAŞTIRMALI —
    /// aksi halde yükseltme her girişte yeniden hesaplanır ama hiç yazılmaz, yani karma
    /// eski faktörde kalır. Yalnızca giriş yolu (Login) bunu kullanıyor; parola sıfırlama
    /// ve kayıt zaten güncel faktörle taze karma üretiyor.
    /// </remarks>
    bool Verify(string hash, string password, out string? yenilenmisKarma);
}

public interface ITokenService
{
    string CreateAccessToken(User user);

    /// <summary>Tek amaçlı, süreli, imzalı token (e-posta doğrulama linki). DB tablosu gerektirmez.</summary>
    string CreatePurposeToken(Guid userId, string purpose, TimeSpan lifetime);

    /// <summary>Geçersiz/yanlış amaçlı token'da null döner.</summary>
    Guid? ValidatePurposeToken(string token, string purpose);

    /// <summary>
    /// Amacı verilen ÖNEKLE başlayan token'ı doğrular; kullanıcı kimliğiyle birlikte
    /// TAM amaç dizesini de döndürür.
    ///
    /// NEDEN GEREKLİ: parola sıfırlama token'ının amacı sabit değil — kullanıcının o
    /// anki parola hash'inin damgasını taşıyor ki bağlantı kullanıldığı anda ölsün
    /// (bkz. ParolaSifirlama). Ama damgayı hesaplamak için kullanıcıyı bilmek gerekiyor
    /// ve kullanıcıyı öğrenmenin tek yolu token'ı doğrulamak — yani tam amaç dizesi
    /// önceden bilinemiyor. Bu aşırı yükleme yumurta-tavuk sorununu çözüyor: imza,
    /// audience ve süre YİNE doğrulanıyor, yalnızca amaç karşılaştırması "eşittir"
    /// yerine "ile başlar"a iniyor; kesin eşitlik kontrolünü çağıran, kullanıcıyı
    /// yükledikten sonra kendisi yapıyor.
    ///
    /// ⚠️ Önek MUTLAKA ayırıcıyla bitmeli ("password-reset:"): ayırıcısız bir önek,
    /// aynı harflerle başlayan başka bir amacın token'ını da kabul ederdi.
    /// </summary>
    (Guid UserId, string Purpose)? ValidatePurposeTokenByPrefix(string token, string purposePrefix);
}

/// <summary>Depodaki tek bir nesne. Temizlik işi bu listeyi DB'deki referanslarla karşılaştırır.</summary>
public readonly record struct StoredObject(string Key, DateTime LastModifiedUtc);

public interface IProofStorage
{
    /// <summary>Dosyayı kalıcı depoya yazar, storage key döner (DB'de yalnızca key saklanır).</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>
    /// Kanıtı okumak için açar. Dosya yoksa null döner (depo ile DB arasında tutarsızlık:
    /// çağıran bunu 404'e çevirir, exception fırlatmaz).
    /// </summary>
    Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Dosyayı siler. Dosya zaten yoksa HATA DEĞİLDİR (temizlik işi idempotent olmalı:
    /// yarıda kalan bir tur, sonraki turda aynı anahtarı yeniden dener).
    /// </summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Depodaki tüm nesneleri akıtır. Yalnızca bakım işi kullanır — istek yolunda ÇAĞRILMAZ.
    /// Akış olarak dönmesinin sebebi, uzak depolarda (S3) listenin sayfalı gelmesi ve
    /// tamamının belleğe alınmasının gerekmemesi.
    /// </summary>
    IAsyncEnumerable<StoredObject> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// Yüklenen görsellerden konum/cihaz sızdıran metadata'yı (EXIF/GPS/XMP/IPTC ve gömülü
/// EXIF thumbnail'ini) siler: görüntüyü çözer, EXIF yönelimini piksele işler ve metadata
/// olmadan AYNI formatta yeniden kodlar. YALNIZCA görsel içerik tiplerine çağrılır;
/// PDF gibi görsel-olmayan tipler bu servise hiç uğramaz (çağıran içerik tipiyle ayırır).
///
/// NEDEN DEPO KATMANINDA DEĞİL: temizlik depoya YAZMADAN önce, handler'da olmalı. S3'e
/// geçince IProofStorage değişir ama sızıntı riski aynı kalırdı; ayrıca ders kanıtında
/// hash temizlenmiş baytlardan hesaplandığı için depoya giren bayt ile hash aynı olmalı.
/// </summary>
public interface IGorselTemizleyici
{
    /// <summary>
    /// Baytları görsel olarak çözemezse (bozuk/görsel-olmayan/decompression-bomb) false
    /// döner; çağıran kendi bağlamına uygun AppException fırlatır (ProofInvalid ya da
    /// ValidationFailed). Başarılıysa <paramref name="temiz"/> metadata'sız yeniden
    /// kodlanmış baytları taşır.
    /// </summary>
    bool TryTemizle(byte[] icerik, string contentType, out byte[] temiz);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

// ─── Push bildirimleri (2026-09-25) ─────────────────────────────────────────────
// Yerleşim e-posta kalıbının karşılığı: arayüz burada, "Log" ve "Expo" uygulamaları
// Infrastructure/Services'te, seçim Push:Provider ile. Mimari: docs/ASAMA-2-BACKEND.md.

/// <summary>
/// Expo push servisinin sınırları (docs.expo.dev/push-notifications/sending-notifications).
/// Aşılırsa Expo isteği bütünüyle reddeder; parçalama çağıranın değil gönderenin işi.
/// </summary>
public static class PushSinirlari
{
    /// <summary>Tek istekte en fazla mesaj.</summary>
    public const int EnFazlaMesaj = 100;

    /// <summary>Tek makbuz isteğinde en fazla bilet.</summary>
    public const int EnFazlaBilet = 1000;

    /// <summary>Tek mesajın yük sınırı (Android ve iOS için aynı).</summary>
    public const int EnFazlaBayt = 4096;

    /// <summary>Başlık üst sınırı (grafem). Kilit ekranında zaten kesiliyor; biz bilerek keseriz.</summary>
    public const int BaslikEnFazla = 50;

    /// <summary>Gövde üst sınırı (grafem).</summary>
    public const int GovdeEnFazla = 150;
}

/// <summary>
/// Bildirimin <c>data</c> alanı. YALNIZCA bu üçü: kişi kimliği, içerik, ad YOK.
/// </summary>
/// <param name="Tur">Mobilin tanıdığı tür ("mesaj", "istek", "onay"…). Bkz. BildirimKanallari.VeriTuru.</param>
/// <param name="Url">Dokununca açılacak rota. Mobil yalnızca beyaz listedeki biçimleri kabul eder.</param>
/// <param name="Alici">
/// Alıcının HMAC etiketi (BildirimEtiketi.Alici). userId'nin yerine: mobil, hesap değişmiş
/// bir telefonda önceki hesaba ait bildirimi bununla tanıyıp yok sayar; kimlik açığa çıkmaz.
/// </param>
public sealed record PushVerisi(string Tur, string Url, string Alici);

/// <summary>
/// Expo'ya giden TEK mesaj, tek alıcı token'ı. Özellik adları camelCase serileştirilince
/// Expo'nun alan adlarıyla birebir aynı (BildirimYuku.JsonAyarlari). Null alanlar yazılmaz.
/// </summary>
/// <remarks>
/// Kurulumu BildirimYuku'da, platforma göre: Android'de ChannelId/Priority/Tag var,
/// CollapseId YOK; iOS'ta Sound/Badge/CollapseId/ThreadId var. Elle kurma.
/// </remarks>
public sealed record PushMesaji
{
    public required string To { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required PushVerisi Data { get; init; }

    /// <summary>Saniye. Süre dolunca sağlayıcı yeniden teslim etmeye çalışmaz.</summary>
    public int? Ttl { get; init; }

    // ── Android ──
    public string? ChannelId { get; init; }

    /// <summary>"high" | "normal".</summary>
    public string? Priority { get; init; }

    /// <summary>Cihazda aynı etiketli bildirimin YERİNE geçer.</summary>
    public string? Tag { get; init; }

    // ── iOS ──
    public string? Sound { get; init; }
    public int? Badge { get; init; }

    /// <summary>apns-collapse-id: ekrandaki aynı kimlikli bildirimin yerine geçer.</summary>
    public string? CollapseId { get; init; }

    public string? ThreadId { get; init; }
}

/// <summary>
/// Gönderimdeki tek mesajın bileti; <see cref="PushGonderimSonucu.Biletler"/>'de mesajlarla
/// AYNI SIRADA. Expo mesaj ↔ bilet eşlemesini yalnızca sırayla veriyor.
/// </summary>
/// <param name="Basarili">Expo "ok" dedi: kabul edildi (teslim edildi DEĞİL, o makbuzda).</param>
/// <param name="HataKodu">details.error: DeviceNotRegistered, MessageTooBig, MessageRateExceeded…</param>
/// <param name="HataMesaji">⚠️ MASKELENMİŞ olmalı: Expo'nun metni token'ı içeriyor.</param>
public sealed record PushBileti(bool Basarili, string? BiletId, string? HataKodu, string? HataMesaji);

/// <summary>
/// İsteğin BÜTÜNÜYLE düştüğü durum (hiç bilet yok): HTTP hatası, zaman aşımı, ağ hatası
/// ya da Expo'nun istek düzeyi hatası.
/// </summary>
/// <param name="HttpDurumu">Null: yanıt hiç gelmedi (zaman aşımı / ağ hatası).</param>
/// <param name="Kod">Expo hata kodu (ör. PUSH_TOO_MANY_EXPERIENCE_IDS) ya da "ZamanAsimi" / "AgHatasi".</param>
/// <param name="Mesaj">⚠️ Maskelenmiş.</param>
/// <param name="DeneyimTokenlari">
/// PUSH_TOO_MANY_EXPERIENCE_IDS'te Expo'nun verdiği "deneyim → token listesi" eşlemesi:
/// başka bir Expo projesine ait token'ları ayırmanın tek yolu.
/// </param>
public sealed record PushIstekHatasi(
    int? HttpDurumu,
    string? Kod,
    string? Mesaj,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DeneyimTokenlari);

/// <summary>
/// Gönderim sonucu. İKİSİNDEN BİRİ dolu: ya <see cref="IstekHatasi"/> (bilet yok) ya da
/// mesaj sayısı kadar <see cref="Biletler"/>.
/// </summary>
public sealed record PushGonderimSonucu(PushIstekHatasi? IstekHatasi, IReadOnlyList<PushBileti> Biletler)
{
    public static PushGonderimSonucu Hata(PushIstekHatasi hata) => new(hata, []);
}

/// <summary>Bir biletin makbuzu: teslimin sağlayıcıya (FCM/APNs) ulaşıp ulaşmadığı.</summary>
/// <param name="HataKodu">DeviceNotRegistered ise cihaz satırı silinir.</param>
/// <param name="HataMesaji">⚠️ Maskelenmiş.</param>
public sealed record PushMakbuzu(bool Basarili, string? HataKodu, string? HataMesaji);

/// <summary>
/// Push gönderici. İki uygulama: LoggingPushGonderici (geliştirme, yalnızca maskeli log)
/// ve ExpoPushGonderici (düz HTTP, harici SDK yok).
/// </summary>
/// <remarks>
/// ⛔ İSTEK İŞLEYİCİSİNDEN (handler) ÇAĞRILMAZ. Handler bildirim defterine satır yazar
/// (BildirimKuyrugu), gönderimi dağıtım işi yapar. Handler'da Expo'yu beklemek isteği
/// yavaşlatır ve sunucu yeniden başlarsa bildirimi kaybettirirdi.
///
/// Uygulamalar FIRLATMAZ (iptal hariç): ağ ve HTTP hataları <see cref="PushIstekHatasi"/>
/// olarak döner, çünkü dağıtıcı her hatayı satır bazında sınıflandırıp yazmak zorunda.
/// </remarks>
public interface IPushGonderici
{
    /// <summary>En fazla <see cref="PushSinirlari.EnFazlaMesaj"/> mesaj.</summary>
    Task<PushGonderimSonucu> GonderAsync(IReadOnlyList<PushMesaji> mesajlar, CancellationToken ct);

    /// <summary>
    /// En fazla <see cref="PushSinirlari.EnFazlaBilet"/> bilet. Sözlükte olmayan bilet
    /// henüz hazır değildir.
    /// </summary>
    Task<IReadOnlyDictionary<string, PushMakbuzu>> MakbuzlariAlAsync(IReadOnlyList<string> biletler, CancellationToken ct);
}

/// <summary>
/// "Kuyrukta iş var" sinyali: handler commit'ten sonra dağıtım işini uyandırır. İş en geç
/// birkaç saniyede bir zaten tarıyor; sinyal yalnızca gecikmeyi kısaltır, doğruluk ona
/// bağlı değil.
/// </summary>
public interface IBildirimSinyali
{
    /// <summary>
    /// Dağıtım işini uyandırır. ⛔ HİÇBİR KOŞULDA FIRLATMAZ ve sayaç biriktirmez (art arda
    /// bin çağrı tek bir uyanış bırakır).
    /// </summary>
    /// <remarks>
    /// Handler'lar bunu commit'ten SONRA çağırıyor. Fırlatsaydı commit olmuş bir mesaj
    /// istemciye hata olarak döner, istemci yeniden gönderir ve mesaj iki kez yazılırdı.
    /// </remarks>
    void Uyandir();

    /// <summary>
    /// Sinyal gelene ya da <paramref name="enFazla"/> dolana kadar bekler. Sinyalle
    /// uyandıysa true. Yalnızca dağıtım işi çağırır.
    /// </summary>
    Task<bool> BekleAsync(TimeSpan enFazla, CancellationToken ct);
}
