using PeerLearn.Application.Matchmaking;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Kuyruğa önceden yazılacak tek bir hatırlatma.
/// </summary>
/// <param name="Ofset">Ders için dakika (60/10), otomatik onay için saat (24/2). Anahtarın parçası.</param>
/// <param name="AnUtc">Gönderim anı → Notification.DueAtUtc.</param>
/// <param name="SonUtc">Bu andan sonra gönderilmez → Notification.ExpiresAtUtc (An + tolerans).</param>
public sealed record Hatirlatma(int Ofset, DateTime AnUtc, DateTime SonUtc);

/// <summary>
/// Hatırlatmaların ne zaman gideceğinin ve hatırlatma işinin hangi kayıtlara bakacağının
/// saf kuralları. Hatırlatma işi (EnqueuePushReminders) yalnızca bunları çağırır.
/// </summary>
/// <remarks>
/// ─── İLERİYE DÖNÜK KUYRUKLAMA ───────────────────────────────────────────────
/// Hatırlatma "anı geldi mi" taramasıyla DEĞİL, önceden yazılır: anı
/// (now − tolerans, now + <see cref="IleriBakis"/>] aralığına giren her hatırlatma
/// DueAt = an, ExpiresAt = an + tolerans ile kuyruğa girer; gönderim zamanını dağıtıcı
/// belirler. Böylece dakikalık iş bir turu kaçırsa bile hatırlatma düşmez. Durum (iptal,
/// onay, yeni damga) gönderim ANINDA yeniden kontrol edilir; önceden yazılmış satır
/// geçersizleşmişse Skipped(DurumDegisti) olur.
///
/// Tolerans bayatlık sınırıdır: sunucu toleranstan uzun kapalı kaldıysa satır Bayat olur,
/// çünkü geç gelen "1 saat kaldı" yanlış bilgidir.
///
/// ⚠️ Sorgu aralıkları (…SorguAraligi) yalnızca ÖN SÜZGEÇ: kesin karar bellekte
/// buradaki fonksiyonlarla verilir. Aralığı daraltmak hatırlatma kaybettirir,
/// genişletmek yalnızca birkaç fazla satır okutur.
/// </remarks>
public static class HatirlatmaPenceresi
{
    /// <summary>Anı bu kadar ileride olan hatırlatma da şimdiden kuyruğa yazılır.</summary>
    public static readonly TimeSpan IleriBakis = TimeSpan.FromHours(2);

    // ─── Ders yaklaşıyor ───────────────────────────────────────────────────────

    /// <summary>Başlangıçtan önce (dakika, tolerans). Sessiz saat UYGULANMAZ.</summary>
    public static readonly IReadOnlyList<(int Dakika, TimeSpan Tolerans)> DersOfsetleri =
    [
        (60, TimeSpan.FromMinutes(15)),
        (10, TimeSpan.FromMinutes(5)),
    ];

    /// <summary>
    /// Bir dersin hatırlatma anları (zamandan bağımsız karar).
    /// </summary>
    /// <param name="baslangicUtc">ScheduledStartUtc. Yalnızca rezervasyonda yazılıyor, yani sabit.</param>
    /// <param name="rezervasyonUtc">LessonSession.CreatedAtUtc.</param>
    /// <remarks>
    /// Hatırlatma anından SONRA yapılmış rezervasyonda o ofset atlanır (5 dakika sonrasına
    /// rezervasyon: "1 saat kaldı" yanlış olurdu). Eğitmenin ilk haberi o durumda
    /// "Yeni ders planlandı" bildirimi.
    /// </remarks>
    public static IReadOnlyList<Hatirlatma> DersAnlari(DateTime baslangicUtc, DateTime rezervasyonUtc)
    {
        var sonuc = new List<Hatirlatma>(DersOfsetleri.Count);
        foreach (var (dakika, tolerans) in DersOfsetleri)
        {
            var an = Utc(baslangicUtc.AddMinutes(-dakika));
            if (an < rezervasyonUtc)
            {
                continue;
            }

            sonuc.Add(new Hatirlatma(dakika, an, an + tolerans));
        }

        return sonuc;
    }

    /// <summary>Şimdi kuyruğa yazılacak ders hatırlatmaları. Başlangıcı geçmiş ders için hiçbiri.</summary>
    public static IReadOnlyList<Hatirlatma> KuyruklanacakDersHatirlatmalari(
        DateTime nowUtc, DateTime baslangicUtc, DateTime rezervasyonUtc)
    {
        if (baslangicUtc <= nowUtc)
        {
            return [];
        }

        return DersAnlari(baslangicUtc, rezervasyonUtc).Where(h => KuyrugaGirer(h, nowUtc)).ToList();
    }

    /// <summary>
    /// Aday dersler: <c>Status == Booked</c> ve ScheduledStartUtc ∈ (Alt, Ust].
    /// IX_LessonSessions_YaklasanDers kullanılır.
    /// </summary>
    /// <remarks>
    /// Türetme: her ofset için an = başlangıç − o, an ∈ (now − tol, now + ileri] ⇔
    /// başlangıç ∈ (now + o − tol, now + o + ileri]. Ofsetlerin birleşimi.
    /// </remarks>
    public static (DateTime AltHaric, DateTime UstDahil) DersSorguAraligi(DateTime nowUtc)
    {
        var alt = DersOfsetleri.Min(o => TimeSpan.FromMinutes(o.Dakika) - o.Tolerans);
        var ust = DersOfsetleri.Max(o => TimeSpan.FromMinutes(o.Dakika)) + IleriBakis;
        return (Utc(nowUtc + alt), Utc(nowUtc + ust));
    }

    // ─── Otomatik onay yaklaşıyor ──────────────────────────────────────────────

    /// <summary>Otomatik onaydan önce (saat, tolerans).</summary>
    public static readonly IReadOnlyList<(int Saat, TimeSpan Tolerans)> OtoOnayOfsetleri =
    [
        (24, TimeSpan.FromMinutes(30)),
        (2, TimeSpan.FromMinutes(15)),
    ];

    /// <summary>24 saatlik hatırlatma sabaha kayarsa otomatik onaya en az bu kadar kalmalı.</summary>
    public static readonly TimeSpan SabahEnGecKalan = TimeSpan.FromHours(3);

    /// <summary>2 saatlik hatırlatma, 24 saatlik olanın gerçek anından en az bu kadar sonra olmalı.</summary>
    public static readonly TimeSpan IkiHatirlatmaArasi = TimeSpan.FromHours(6);

    /// <summary>
    /// Onay bekleyen bir dersin otomatik onay hatırlatma anları.
    /// </summary>
    /// <param name="damgaUtc">CompletionRequestedAtUtc (itiraz reddinde yenilenir).</param>
    /// <param name="otomatikOnaySaati">EconomyOptions.AutoApproveHours (H). D = damga + H.</param>
    /// <remarks>
    /// Kurallar (gerekçeleriyle):
    /// <list type="bullet">
    /// <item>Ofset H'ye eşit ya da büyükse atlanır: H = 24 iken "24 saat kaldı" damgayla
    /// aynı anda, onay bekliyor bildiriminin kopyası olurdu.</item>
    /// <item>R24 = D − 24 sa. Sessiz aralığa düşerse sonraki 09:00'a kayar; ama o an
    /// D − 3 sa'dan ÖNCE kalmalı, kalmıyorsa atlanır (sabah haber verip öğrenciye hiç
    /// zaman bırakmamak anlamsız).</item>
    /// <item>R2 = D − 2 sa. Sessiz aralığa düşerse ÖNCEKİ 21:30'a çekilir (sabaha kaydırmak
    /// onaydan sonraya düşürürdü). R2, R24'ün gerçek anından en az 6 saat sonra olmalı;
    /// değilse atlanır — art arda iki hatırlatma tek bilgiyi iki kez vermek olur.</item>
    /// <item>Her an damgadan SONRA olmalı: olaydan önceye düşen hatırlatma anlamsız.</item>
    /// </list>
    /// ⚠️ 6 saat kuralı, çekilmemiş R2'ye de uygulanır (tasarım metni yalnızca çekileni
    /// söylüyor). Varsayılan H = 48'de fark yok: D'nin günün her dakikasına denk geldiği
    /// durumlarda R2 ile R24'ün gerçek anı arasındaki fark en az 10,5 saat (sessiz aralık
    /// ile 21:30 çekmesi aynı gece içinde kalıyor). Kural yalnızca 24 &lt; H &lt; ~31 gibi
    /// kısa ayarlarda devreye girer; orada da iki hatırlatmayı birbirine yapıştırmamak
    /// istenen şey.
    /// </remarks>
    public static IReadOnlyList<Hatirlatma> OtoOnayAnlari(DateTime damgaUtc, int otomatikOnaySaati)
    {
        var d = Utc(damgaUtc.AddHours(otomatikOnaySaati));
        var sonuc = new List<Hatirlatma>(2);
        DateTime? r24Gercek = null;

        foreach (var (saat, tolerans) in OtoOnayOfsetleri)
        {
            if (saat >= otomatikOnaySaati)
            {
                continue;
            }

            var an = Utc(d.AddHours(-saat));
            bool uygun;

            if (saat == 24)
            {
                if (SessizSaat.SessizMi(an))
                {
                    an = SessizSaat.SonrakiSabah(an);
                }

                uygun = an < d - SabahEnGecKalan;
            }
            else
            {
                if (SessizSaat.SessizMi(an))
                {
                    an = SessizSaat.OncekiAksam(an);
                }

                uygun = r24Gercek is null || an >= r24Gercek.Value + IkiHatirlatmaArasi;
            }

            if (!uygun || an <= damgaUtc)
            {
                continue;
            }

            if (saat == 24)
            {
                r24Gercek = an;
            }

            sonuc.Add(new Hatirlatma(saat, an, an + tolerans));
        }

        return sonuc;
    }

    /// <summary>Şimdi kuyruğa yazılacak otomatik onay hatırlatmaları.</summary>
    public static IReadOnlyList<Hatirlatma> KuyruklanacakOtoOnayHatirlatmalari(
        DateTime nowUtc, DateTime damgaUtc, int otomatikOnaySaati)
        => OtoOnayAnlari(damgaUtc, otomatikOnaySaati).Where(h => KuyrugaGirer(h, nowUtc)).ToList();

    /// <summary>
    /// Aday dersler: <c>Status == AwaitingApproval</c> ve CompletionRequestedAtUtc ∈ (Alt, Ust].
    /// IX_LessonSessions_OnayBekleyen kullanılır.
    /// </summary>
    /// <remarks>
    /// Türetme: her hatırlatma anı D − 24 sa ile D − 2 sa arasında kalır (R24 en fazla
    /// 11 sa ileri kayar ve yine D − 3 sa'dan önce; R2 yalnızca GERİ çekilir). An ∈
    /// (now − en büyük tolerans, now + ileri] ⇒ D ∈ (now − tol + 2 sa, now + ileri + 24 sa]
    /// ⇒ damga = D − H.
    /// </remarks>
    public static (DateTime AltHaric, DateTime UstDahil) OtoOnaySorguAraligi(DateTime nowUtc, int otomatikOnaySaati)
    {
        var enBuyukTolerans = OtoOnayOfsetleri.Max(o => o.Tolerans);
        var enKisaKalan = TimeSpan.FromHours(OtoOnayOfsetleri.Min(o => o.Saat));
        var enUzunKalan = TimeSpan.FromHours(OtoOnayOfsetleri.Max(o => o.Saat));
        var h = TimeSpan.FromHours(otomatikOnaySaati);

        return (Utc(nowUtc - enBuyukTolerans + enKisaKalan - h),
                Utc(nowUtc + IleriBakis + enUzunKalan - h));
    }

    // ─── İstekler düşmek üzere (günlük özet) ──────────────────────────────────

    /// <summary>Özet yuvasının UTC saati: 07:00 UTC = 10:00 TR.</summary>
    public static readonly TimeSpan OzetSaatiUtc = TimeSpan.FromHours(7);

    /// <summary>Yuvadan bu kadar önce yazılmaya başlanır (dağıtıcı yine yuva anında gönderir).</summary>
    public static readonly TimeSpan OzetOnce = TimeSpan.FromHours(2);

    /// <summary>Yuvadan bu kadar sonrasına kadar yazılabilir ve gönderilebilir; sonra Bayat.</summary>
    public static readonly TimeSpan OzetSonra = TimeSpan.FromHours(1);

    /// <summary>Özete giren isteğin düşmesine kalan süre: (6 sa, 30 sa].</summary>
    public static readonly TimeSpan OzetEnAzKalan = TimeSpan.FromHours(6);

    /// <summary>
    /// Şimdi bir özet yuvasının yazım penceresinde miyiz? Evetse yuva anı (bugün 07:00 UTC),
    /// değilse null. Pencere [yuva − 2 sa, yuva + 1 sa).
    /// </summary>
    /// <remarks>
    /// Yuva saati boyunca sunucu kapalıysa o günün özeti hiç yazılmaz ya da Bayat olur;
    /// ertesi günün özeti kendi penceresindeki istekleri kapsar. O istekler düşmeden önce
    /// yalnızca bu yolla haber alamaz — kabul edilmiş sınır.
    /// </remarks>
    public static DateTime? OzetYuvasi(DateTime nowUtc)
    {
        var yuva = Utc(nowUtc.Date + OzetSaatiUtc);
        return nowUtc >= yuva - OzetOnce && nowUtc < yuva + OzetSonra ? yuva : null;
    }

    /// <summary>Özet satırının ExpiresAtUtc'si.</summary>
    public static DateTime OzetSonu(DateTime yuvaUtc) => Utc(yuvaUtc + OzetSonra);

    /// <summary>
    /// Bu yuvanın özetine girecek istekler: <c>Status == Pending</c> ve CreatedAtUtc ∈ (Alt, Ust].
    /// Sorgu IX_Matches_PendingAge filtresine birebir uyar.
    /// </summary>
    /// <remarks>
    /// Düşme anı (oluşturma + 14 g) ∈ (yuva + 6 sa, yuva + 30 sa]. Ardışık günlerin aralıkları
    /// (yuva+6, yuva+30] ve (yuva+30, yuva+54] BOŞLUKSUZ ve ÖRTÜŞMESİZ: her istek tam olarak
    /// bir özete girer ve düşmesinden 6–30 saat önce haber verilir.
    /// </remarks>
    public static (DateTime AltHaric, DateTime UstDahil) OzetAdayAraligi(DateTime yuvaUtc)
    {
        var omur = TimeSpan.FromDays(MatchRules.RequestExpireDays);
        var gun = TimeSpan.FromDays(1);
        return (Utc(yuvaUtc + OzetEnAzKalan - omur), Utc(yuvaUtc + OzetEnAzKalan + gun - omur));
    }

    // ─── Ortak ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hatırlatma şimdi kuyruğa yazılmalı mı: an ∈ (now − tolerans, now + ileri].
    /// Alt sınır "son &gt; now" ile aynı: bayatlamamış olan yazılır.
    /// </summary>
    public static bool KuyrugaGirer(Hatirlatma h, DateTime nowUtc) => h.SonUtc > nowUtc && h.AnUtc <= nowUtc + IleriBakis;

    private static DateTime Utc(DateTime deger) => DateTime.SpecifyKind(deger, DateTimeKind.Utc);
}
