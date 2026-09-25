using PeerLearn.Application.Abstractions;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>Tek bir cihaz mesajının bileti ne anlama geliyor.</summary>
public enum BiletKarari
{
    /// <summary>Expo kabul etti. Satır, en az bir cihazdan bu gelirse Sent.</summary>
    Basarili,

    /// <summary>Token artık kayıtlı değil (DeviceNotRegistered): cihaz satırı silinir.</summary>
    CihazOlu,

    /// <summary>Geçici (MessageRateExceeded): hiçbir cihazdan ok gelmediyse satır beklemeyle kuyruğa döner.</summary>
    Gecici,

    /// <summary>
    /// Kalıcı ve cihazın suçu değil (MessageTooBig, InvalidCredentials, MismatchSenderId,
    /// tanınmayan kod): yeniden denemek aynı sonucu verir. Satır Failed, cihaz SİLİNMEZ.
    /// </summary>
    Kalici
}

/// <summary>Bütün isteğin düştüğü durumda (hiç bilet yok) ne yapılacak.</summary>
public enum IstekKarari
{
    /// <summary>Ağ, zaman aşımı, 429, 5xx, 401/403: partideki her satır beklemeyle kuyruğa döner.</summary>
    Gecici,

    /// <summary>
    /// Partide başka bir Expo projesinin token'ları var (PUSH_TOO_MANY_EXPERIENCE_IDS): yabancı
    /// token'lar ayrılır, kalanlar DENEME SAYILMADAN hemen yeniden gönderilir.
    /// </summary>
    YabanciDeneyim,

    /// <summary>
    /// Diğer istek düzeyi 400'ler: parti ikiye bölünerek yeniden gönderilir; yalnızca tek başına
    /// düşen mesaj kalıcı hata sayılır. Tek bozuk mesaj yüz kişinin bildirimini durdurmasın.
    /// </summary>
    Bolunebilir
}

/// <summary>
/// Expo yanıtlarının dağıtıcıdaki anlamı ve yeniden deneme beklemesi. Saf; birim testli.
/// </summary>
/// <remarks>
/// ─── NEDEN APPLICATION'DA ───────────────────────────────────────────────────
/// Tasarım bu sınıflandırmayı Infrastructure'daki ExpoYanitCozumleyici'ye veriyordu. Ama
/// kararı UYGULAYAN dağıtıcı Application'da ve Application Infrastructure'a bağlanamaz.
/// Bölüşüm şöyle: ExpoYanitCozumleyici Expo'nun JSON biçimini çözer ve metinleri maskeler
/// (sağlayıcıya özgü), bu sınıf "hangi kod ne demek, kaç kez denenir, ne kadar beklenir"
/// kararını verir (sağlayıcıdan bağımsız sözleşme). Kod adları Expo belgesinden:
/// docs.expo.dev/push-notifications/sending-notifications.
///
/// ─── HATA KODLARI SINIFLANDIRILIR, METİNLER DEĞİL ───────────────────────────
/// Karar yalnızca makine okunur koda bakıyor; Expo'nun İngilizce mesajı değişse de karar
/// değişmez. Metin yalnızca teşhis için LastError'a (maskeli) yazılır.
/// </remarks>
public static class PushHataKurali
{
    /// <summary>
    /// Geçici hatada toplam deneme. Altıncı başarısızlıktan sonra satır Failed: toplam bekleme
    /// 30 sn + 1 + 2 + 4 + 8 dk ≈ 15,5 dk. Mesaj bildiriminin ömrü zaten 6 saat; daha uzun
    /// süren bir Expo kesintisinde satırı sonsuza kadar döndürmek yerine iz bırakıp bırakmak.
    /// </summary>
    public const int EnFazlaDeneme = 6;

    public static readonly TimeSpan BeklemeTabani = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan BeklemeTavani = TimeSpan.FromMinutes(30);

    /// <summary>LastError kolonu sınırı (varchar(300)).</summary>
    public const int SonHataEnFazla = 300;

    /// <summary>Expo'nun kalıcı saydığımız ve "cihaz öldü" dediği bilet hata kodu.</summary>
    public const string CihazKayitliDegil = "DeviceNotRegistered";

    /// <summary>Başka projenin token'ları; istek düzeyi hata kodu.</summary>
    public const string CokDeneyim = "PUSH_TOO_MANY_EXPERIENCE_IDS";

    /// <summary>Expo 4096 baytı aşan mesajı reddeder; biz göndermeden önce ölçüp aynı kodu yazıyoruz.</summary>
    public const string MesajCokBuyuk = "MessageTooBig";

    public static BiletKarari Bilet(PushBileti bilet)
    {
        if (bilet.Basarili)
        {
            return BiletKarari.Basarili;
        }

        return bilet.HataKodu switch
        {
            CihazKayitliDegil => BiletKarari.CihazOlu,
            "MessageRateExceeded" => BiletKarari.Gecici,
            // MessageTooBig: kod hatası (yük kurulumu sınırı aşmış), InvalidCredentials /
            // MismatchSenderId: FCM/APNs kimlik bilgisi yanlış — ikisi de cihazın suçu değil,
            // cihazı silmek sorunu gizler ve kullanıcıyı bildirimsiz bırakırdı.
            _ => BiletKarari.Kalici
        };
    }

    /// <summary>
    /// Bu bilet hatası kod hatası ya da yapılandırma hatası mı? (LogError ile yazılmalı:
    /// tek kullanıcının sorunu değil, herkesi etkiliyor.)
    /// </summary>
    public static bool YapilandirmaHatasi(string? hataKodu)
        => hataKodu is MesajCokBuyuk or "InvalidCredentials" or "MismatchSenderId";

    public static IstekKarari Istek(PushIstekHatasi hata)
    {
        if (hata.HttpDurumu == 400 && hata.Kod == CokDeneyim && hata.DeneyimTokenlari is { Count: > 0 })
        {
            return IstekKarari.YabanciDeneyim;
        }

        /* 400 ve 413: isteğin İÇERİĞİ reddedildi (bozuk alan, çok büyük gövde). Bölmek sorunlu
           mesajı yalıtır. 401/403/404/429/5xx ve yanıtsızlık İÇERİKTEN bağımsız: bölmek aynı
           hatayı yüz kez almak olurdu. Özellikle 401/403 (erişim token'ı ya da Enhanced
           Security) hiçbir mesajın suçu değil; Failed yazmak kesinti bitince kaybettirirdi. */
        return hata.HttpDurumu is 400 or 413 ? IstekKarari.Bolunebilir : IstekKarari.Gecici;
    }

    /// <summary>401/403: erişim token'ı yanlış ya da Enhanced Security token'sız isteği reddetti. LogCritical.</summary>
    public static bool YetkiHatasi(PushIstekHatasi hata) => hata.HttpDurumu is 401 or 403;

    /// <summary>
    /// <paramref name="deneme"/>. başarısızlıktan sonraki bekleme: min(30 sn · 2^(n−1), 30 dk).
    /// SweepSessions.Gecikme kalıbı; üs kırpılıyor ki tick çarpımı taşıp NEGATİF bir gecikme
    /// (hemen yeniden deneme) üretmesin.
    /// </summary>
    public static TimeSpan Bekleme(int deneme)
    {
        var us = Math.Min(Math.Max(deneme - 1, 0), 12);
        var ticks = BeklemeTabani.Ticks * (1L << us);
        return TimeSpan.FromTicks(Math.Min(ticks, BeklemeTavani.Ticks));
    }

    /// <summary>
    /// LastError'a yazılacak kısa metin: "kod: mesaj", maskeli ve kolon sınırında. Gönderici
    /// metni zaten maskeli veriyor; burada İKİNCİ KEZ maskeleniyor, çünkü tam token'ın
    /// veritabanına düşmesi geri alınamaz (yedeklere de gider) ve bedeli bir regex çağrısı.
    /// </summary>
    public static string SonHata(string? kod, string? mesaj)
    {
        var metin = string.IsNullOrWhiteSpace(mesaj) ? kod ?? "Bilinmeyen hata" : $"{kod ?? "Hata"}: {mesaj}";
        metin = PushTokenKurali.MetniMaskele(metin) ?? string.Empty;
        return metin.Length > SonHataEnFazla ? metin[..SonHataEnFazla] : metin;
    }
}
