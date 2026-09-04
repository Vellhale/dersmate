using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace PeerLearn.Api.Startup;

/// <summary>
/// Her denetleyiciyi <c>/api/v1/…</c> altında DA yayımlar; eski <c>/api/…</c> yolu olduğu
/// gibi çalışmaya devam eder.
/// </summary>
/// <remarks>
/// <para>
/// ── NEDEN SÜRÜMLEME, NEDEN ŞİMDİ ────────────────────────────────────────────────────
/// Uçlarda sürüm öneki yoktu. Mobil uygulama mağazaya <c>/api/x</c> çağırarak çıkarsa o
/// yollar BİR DAHA KALDIRILAMAZ: sahadaki eski sürümler onları çağırmaya devam eder ve
/// kullanıcının uygulamayı güncellemesini zorlayamayız. Yani bu, sonradan telafisi olmayan
/// tek karar — mağaza gönderiminden önce alınması gerekiyordu.
/// </para>
/// <para>
/// ── NEDEN İKİSİ BİRDEN, NEDEN TOPLU DEĞİŞİM DEĞİL ───────────────────────────────────
/// Alternatif, 683 çağrı noktasında <c>/api/</c> → <c>/api/v1/</c> yazmaktı (13 denetleyici,
/// web 74, mobil 82, test betikleri 454). Ölçüldü ve reddedildi: en büyük yüzey test
/// betikleri ve bu projede kanıt standardı MUTASYON — toplu bir yol değişiminde bir betik
/// yanlış uca gidip yine "geçti" diyebilirdi.
/// </para>
/// <para>
/// İki yolu birden sunmak bu riski tamamen kaldırıyor: istemciler tek tek, kendi hızında
/// geçer ve her geçiş ayrı ayrı doğrulanabilir. Mobil İLK GÜNDEN <c>/api/v1</c> ile çıkar,
/// yani asıl kazanılmak istenen şey daha ilk turda elde edilmiş olur. Eski <c>/api/…</c>
/// yalnızca web ve testler için takma ad olarak kalır — ikisini de biz güncelleyebildiğimiz
/// için ileride sorunsuz kaldırılabilir.
/// </para>
/// <para>
/// ── NEDEN DENETLEYİCİ SEÇİCİSİ, NEYE DOKUNULMUYOR ───────────────────────────────────
/// Yeni yol denetleyici düzeyinde ekleniyor; eylem rotaları (<c>[HttpGet("disputes")]</c>)
/// MVC tarafından her denetleyici seçicisiyle ayrı ayrı birleştirildiği için 73 ucun hepsi
/// tek bir kuralla iki yoldan birden yayımlanıyor. Denetleyici dosyalarının hiçbiri
/// değişmiyor.
/// </para>
/// <para>
/// SignalR'a BİLEREK DOKUNULMUYOR: sohbet <c>/hubs/chat</c> üzerinden gidiyor, <c>/api</c>
/// altında değil ve hub sözleşmesi REST'ten bağımsız evriliyor. Ürün sahibinin kararı
/// (2026-09-04).
/// </para>
/// </remarks>
public sealed class SurumOnekiKurali : IApplicationModelConvention
{
    /// <summary>Eklenen sürüm segmenti. Yeni bir sürüm gerekirse burası değil, İKİNCİ bir kural yazılır.</summary>
    private const string Surum = "v1";

    private const string EskiKok = "api";
    private const string YeniKok = $"{EskiKok}/{Surum}";

    public void Apply(ApplicationModel application)
    {
        foreach (var denetleyici in application.Controllers)
        {
            /*
              Liste kopyalanıyor: Selectors üzerinde gezerken aynı listeye eklemek
              InvalidOperationException verir. Ayrıca kopya olmadan eklenen seçici de
              gezilir ve "api/v1/v1/…" üretilirdi.
            */
            var eklenecekler = new List<SelectorModel>();

            foreach (var seci in denetleyici.Selectors)
            {
                var sablon = seci.AttributeRouteModel?.Template;
                if (sablon is null)
                {
                    continue;
                }

                var yeniSablon = SurumluSablon(sablon);
                if (yeniSablon is null)
                {
                    continue;
                }

                /*
                  Seçicinin KOPYASI alınıyor, yenisi sıfırdan kurulmuyor: seçici yalnızca
                  rota taşımıyor — eylem kısıtlayıcıları (HTTP metodu, [Consumes] gibi)
                  da onun üstünde. Sıfırdan kurulan bir seçici bunları düşürür ve uç
                  sessizce yanlış metotlara açılırdı.
                */
                eklenecekler.Add(new SelectorModel(seci)
                {
                    AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(yeniSablon)),
                });
            }

            foreach (var yeni in eklenecekler)
            {
                denetleyici.Selectors.Add(yeni);
            }
        }
    }

    /// <summary>
    /// <c>api</c> → <c>api/v1</c>, <c>api/admin</c> → <c>api/v1/admin</c>.
    /// <c>api</c> ile başlamayan ya da ZATEN sürümlü olan şablonlar için <c>null</c>.
    /// </summary>
    /// <remarks>
    /// "Zaten sürümlü" kontrolü savunma amaçlı: bir gün bir denetleyici doğrudan
    /// <c>[Route("api/v1/…")]</c> yazarsa bu kural ona ikinci bir <c>v1</c> eklemesin.
    /// </remarks>
    private static string? SurumluSablon(string sablon)
    {
        if (sablon.Equals(EskiKok, StringComparison.OrdinalIgnoreCase))
        {
            return YeniKok;
        }

        if (!sablon.StartsWith($"{EskiKok}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var kalan = sablon[(EskiKok.Length + 1)..];

        if (kalan.Equals(Surum, StringComparison.OrdinalIgnoreCase) ||
            kalan.StartsWith($"{Surum}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{YeniKok}/{kalan}";
    }
}
