namespace PeerLearn.Application.Matchmaking;

/// <summary>
/// Eşleşme isteğinin süre kuralları. Saf sabitler ve fonksiyonlar.
/// </summary>
/// <remarks>
/// NEDEN AYRI BİR DOSYA: 14 günlük ömür eskiden yalnızca süpürücüde, private bir sabitti
/// (<c>SweepSessions.MatchRequestExpireDays</c>). Push ile birlikte aynı değeri ÜÇ yer
/// okuyor: süpürücü (düşürme), RespondMatch (süresi dolmuş isteğe 409) ve günlük "düşmek
/// üzere" özeti (hangi istek hangi gün haber verilecek). Üç kopya olsaydı biri değişip
/// diğerleri kalınca özet "yaklaşık 20 saat sonra düşecek" der ve istek o gece düşerdi,
/// ya da kullanıcı çoktan düşmüş bir isteği kabul edebilirdi.
/// </remarks>
public static class MatchRules
{
    /// <summary>
    /// Muhatabın hiç yanıt vermediği isteğin ömrü (gün). Süresi dolan istek Pending
    /// olmaktan çıkar; gönderen aynı kişiye aynı konu için yeniden istek atabilir.
    /// </summary>
    public const int RequestExpireDays = 14;

    /// <summary>İsteğin düşeceği an: oluşturulma + <see cref="RequestExpireDays"/>.</summary>
    public static DateTime DusmeAni(DateTime createdAtUtc) => createdAtUtc.AddDays(RequestExpireDays);

    /// <summary>
    /// İstek artık yanıtlanamaz mı? Süpürücünün koşuluyla (<c>CreatedAtUtc &lt;= now - 14 g</c>)
    /// BİREBİR aynı sınır: süpürücü henüz geçmemiş olsa bile kabul ile düşürme aynı ana
    /// bakmalı, aksi hâlde sınır anında ikisi farklı karar verir.
    /// </summary>
    public static bool SuresiDoldu(DateTime createdAtUtc, DateTime nowUtc) => DusmeAni(createdAtUtc) <= nowUtc;
}
