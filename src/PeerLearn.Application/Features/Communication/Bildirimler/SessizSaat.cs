namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Gece sessizliği: 22:00–09:00 (Türkiye saati) arasında ACİL OLMAYAN bildirimler sabaha
/// kayar. Saf fonksiyonlar; tüm giriş ve çıkışlar UTC.
/// </summary>
/// <remarks>
/// ─── HANGİ BİLDİRİM KAYAR ───────────────────────────────────────────────────
/// Kayar: gelen istek, istek kabulü, onay bekliyor, uzak tarihli ders planı ve iptal.
/// KAYMAZ: yeni mesaj ve yaklaşan ders hatırlatması (60/10 dk) — gece dersi olan kişi
/// hatırlatmayı istiyor, gece yazan arkadaş da cevap bekliyor. Kararı çağıran verir;
/// bu sınıf yalnızca "kaydırılmış an"ı hesaplar.
///
/// ─── NEDEN SABİT UTC+3 ──────────────────────────────────────────────────────
/// Türkiye 2016'dan beri kalıcı UTC+3, yaz saati yok (IANA tzdata, Europe/Istanbul).
/// Kullanıcının saat dilimi bilinmiyor; kitle Türkiye. TimeZoneInfo kullanmak Windows
/// ile Linux arasında farklı kimlik ("Turkey Standard Time" / "Europe/Istanbul") ve
/// konteynerde tzdata bağımlılığı getirirdi — sabit bir fark için gereksiz risk.
///
/// ⚠️ Npgsql timestamptz'ye yalnızca Kind=Utc DateTime yazar (başkasında fırlatır).
/// Buradan dönen her an bu yüzden açıkça Utc işaretli.
/// </remarks>
public static class SessizSaat
{
    /// <summary>Türkiye saati = UTC + 3.</summary>
    public static readonly TimeSpan TrFarki = TimeSpan.FromHours(3);

    /// <summary>Sessizliğin başladığı TR saati (dahil).</summary>
    public static readonly TimeSpan Baslangic = new(22, 0, 0);

    /// <summary>Sessizliğin bittiği TR saati (hariç): 09:00'da gönderim serbest.</summary>
    public static readonly TimeSpan Bitis = new(9, 0, 0);

    /// <summary>
    /// Sessize düşen "otomatik onaya 2 saat" hatırlatmasının geri çekildiği TR saati.
    /// Sabaha kaydırılamaz (sabah onay çoktan gerçekleşmiş olurdu), akşama çekilir.
    /// </summary>
    public static readonly TimeSpan AksamSiniri = new(21, 30, 0);

    /// <summary>
    /// Ders planı/iptal bildiriminde: başlangıca bundan az kaldıysa sessiz saat UYGULANMAZ.
    /// Sessiz aralık en fazla 11 saat sürdüğü için 12 saatten uzak bir ders, sabah 09:00
    /// bildirimiyle bile en az bir saat önceden haber verilmiş olur.
    /// </summary>
    public static readonly TimeSpan UzakPlanEsigi = TimeSpan.FromHours(12);

    /// <summary>An, TR saatiyle 22:00–09:00 arasında mı?</summary>
    public static bool SessizMi(DateTime utc)
    {
        var saat = Tr(utc).TimeOfDay;
        return saat >= Baslangic || saat < Bitis;
    }

    /// <summary>An'dan SONRAKİ ilk 09:00 (TR), UTC olarak.</summary>
    public static DateTime SonrakiSabah(DateTime utc)
    {
        var tr = Tr(utc);
        var aday = tr.Date + Bitis;
        if (aday <= tr)
        {
            aday = aday.AddDays(1);
        }

        return TrdenUtc(aday);
    }

    /// <summary>An'dan ÖNCEKİ (ya da ona eşit) son 21:30 (TR), UTC olarak.</summary>
    public static DateTime OncekiAksam(DateTime utc)
    {
        var tr = Tr(utc);
        var aday = tr.Date + AksamSiniri;
        if (aday > tr)
        {
            aday = aday.AddDays(-1);
        }

        return TrdenUtc(aday);
    }

    /// <summary>Sessiz aralıktaysa sonraki 09:00 TR; değilse anın kendisi.</summary>
    /// <remarks>Gelen istek ve istek kabulü bunu kullanır.</remarks>
    public static DateTime Kaydir(DateTime utc) => SessizMi(utc) ? SonrakiSabah(utc) : UtcIsaretle(utc);

    /// <summary>
    /// Son anı olan olay için kaydırma: sabaha kaydırılmış an <paramref name="sonAn"/> −
    /// <paramref name="pay"/>'a yetişmiyorsa HEMEN (kaydırmadan) gönderilir.
    /// </summary>
    /// <remarks>
    /// Onay bekliyor: gece yüklenen kanıt sabah bildirilir; ama otomatik onaya iki saatten
    /// az kalacaksa uyandırmak, öğrencinin itiraz hakkını sessizce kaybetmesinden iyidir.
    /// </remarks>
    public static DateTime Kaydir(DateTime utc, DateTime sonAn, TimeSpan pay)
    {
        var kaymis = Kaydir(utc);
        return kaymis <= sonAn - pay ? kaymis : UtcIsaretle(utc);
    }

    /// <summary>
    /// Ders planı ve iptal: başlangıca <see cref="UzakPlanEsigi"/>'nden az kaldıysa hemen,
    /// değilse sessiz saate göre.
    /// </summary>
    /// <remarks>
    /// 5 dakika sonrasına yapılan rezervasyonu eğitmen sabah öğrenirse ders kaçmış olur;
    /// yarın akşamki ders için gece 02:00'de telefonu çaldırmanın ise gereği yok.
    /// </remarks>
    public static DateTime UzakPlan(DateTime nowUtc, DateTime baslangicUtc)
        => baslangicUtc - nowUtc < UzakPlanEsigi ? UtcIsaretle(nowUtc) : Kaydir(nowUtc);

    /// <summary>UTC an → TR duvar saati (Kind=Unspecified; yalnızca saat/tarih okumak için).</summary>
    private static DateTime Tr(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) + TrFarki;

    /// <summary>TR duvar saati → UTC an.</summary>
    private static DateTime TrdenUtc(DateTime tr) => DateTime.SpecifyKind(tr - TrFarki, DateTimeKind.Utc);

    /// <summary>
    /// Zaten UTC olan anı Kind=Utc işaretler; DEĞERİ DEĞİŞTİRMEZ. Girdiler sözleşme gereği
    /// UTC (IClock, timestamptz); Kind'i Unspecified gelse de dönüştürme yapılmaz.
    /// </summary>
    private static DateTime UtcIsaretle(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc);
}
