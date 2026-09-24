using System.Globalization;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Bildirim defterindeki DedupeKey'lerin tek üreticisi.
/// </summary>
/// <remarks>
/// UNIQUE(RecipientUserId, DedupeKey) mükerrere karşı SON hat: aynı olay iki kez kuyruğa
/// giremez, aynı hatırlatmayı iki sunucu kopyası ya da yeniden başlatma iki kez yazamaz
/// (<c>INSERT … ON CONFLICT DO NOTHING</c>). Bunun çalışması için aynı olayın anahtarı
/// HER YERDE aynı metin olmalı: anahtarı elle birleştiren ikinci bir yer, bir boşluk ya da
/// büyük harf farkıyla ikinci satırı açardı. Bu yüzden yalnızca buradan üretilir.
///
/// ⚠️ ANAHTARIN BİÇİMİNİ DEĞİŞTİRMEK tekilleştirmeyi o ana kadar yazılmış satırlarla
/// bozar: dağıtım sırasında yeni biçimle yazılan hatırlatma, eski biçimle yazılmış
/// aynı hatırlatmayı "başka bir olay" sanır ve ikinci kez gönderir.
///
/// ⚠️ ANAHTAR AYRIŞTIRILMAZ (test kanalı hariç, orada yanlış okuma yalnızca test
/// bildiriminin kanalını değiştirir). Özellikle onay damgası anahtardan değil
/// Notification.OlayDamgasiUtc sütunundan okunur.
///
/// Kimlikler "D" biçimi (küçük harf, tireli). Anahtar sütunu 160 karakter; en uzunu
/// (otoOnay) ~70.
/// </remarks>
public static class BildirimAnahtarlari
{
    public static string Mesaj(Guid mesajId) => $"mesaj:{mesajId:D}";

    public static string Istek(Guid istekId) => $"istek:{istekId:D}";

    public static string IstekKabul(Guid istekId) => $"istekKabul:{istekId:D}";

    /// <summary>
    /// Günlük "düşmek üzere" özeti: alıcı başına günde bir. Alıcı zaten tekil index'in
    /// parçası, anahtarda yalnızca yuvanın TR tarihi var.
    /// </summary>
    /// <param name="yuvaUtc">Özet yuvası (HatirlatmaPenceresi.OzetYuvasi), UTC.</param>
    public static string IstekDusecek(DateTime yuvaUtc)
        => $"istekDusecek:{(yuvaUtc + SessizSaat.TrFarki).ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Onay bekliyor. Damga, itiraz reddinde yenilenir → yeni anahtar, yeni bildirim.
    /// </summary>
    public static string Onay(Guid dersId, DateTime damgaUtc) => $"onay:{dersId:D}:{Damga(damgaUtc)}";

    /// <param name="saat">24 ya da 2 (HatirlatmaPenceresi.OtoOnayOfsetleri).</param>
    public static string OtoOnay(Guid dersId, DateTime damgaUtc, int saat)
        => $"otoOnay:{dersId:D}:{Damga(damgaUtc)}:{saat.ToString(CultureInfo.InvariantCulture)}";

    public static string Rezervasyon(Guid dersId) => $"rezervasyon:{dersId:D}";

    public static string Iptal(Guid dersId) => $"iptal:{dersId:D}";

    /// <param name="dakika">60 ya da 10 (HatirlatmaPenceresi.DersOfsetleri).</param>
    public static string Yaklasan(Guid dersId, int dakika)
        => $"yaklasan:{dersId:D}:{dakika.ToString(CultureInfo.InvariantCulture)}";

    private const string TestOneki = "test:";

    /// <summary>
    /// Test bildirimi. Her çağrı yeni satır (rastgele kimlik). Kanal anahtarda duruyor,
    /// çünkü dağıtıcı test satırının hangi kanaldan gideceğini başka yerden bilemez
    /// (defter içerik taşımıyor).
    /// </summary>
    public static string Test(string kanal, Guid satirId) => $"{TestOneki}{kanal}:{satirId:N}";

    /// <summary>Test anahtarındaki kanal; biçim tutmazsa ya da kanal bilinmiyorsa null.</summary>
    public static string? TestKanali(string? dedupeKey)
    {
        if (dedupeKey is null || !dedupeKey.StartsWith(TestOneki, StringComparison.Ordinal))
        {
            return null;
        }

        var govde = dedupeKey[TestOneki.Length..];
        var ayrac = govde.IndexOf(':');
        var kanal = ayrac < 0 ? null : govde[..ayrac];
        return BildirimKanallari.Bilinen(kanal) ? kanal : null;
    }

    /// <summary>
    /// Zaman damgasının anahtardaki biçimi: MİKROSANİYE (Ticks / 10), UTC.
    /// </summary>
    /// <remarks>
    /// NEDEN MİKROSANİYE: PostgreSQL timestamptz mikrosaniye tutar ve Npgsql yazarken
    /// 100 ns'lik tick'in son hanesini KESER. Anahtar tick ile yazılsaydı, bellekteki
    /// değerden üretilen anahtar ile veritabanından okunup üretilen anahtar farklı olurdu
    /// (hatırlatma işi damgayı DB'den okuyor, onay satırı handler'da bellekten yazılıyor)
    /// ve aynı olay iki anahtar alırdı. Mikrosaniyeye kesmek iki yolu aynı sayıya indirir.
    /// </remarks>
    public static string Damga(DateTime utc)
        => (DateTime.SpecifyKind(utc, DateTimeKind.Utc).Ticks / 10).ToString(CultureInfo.InvariantCulture);
}
