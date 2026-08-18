namespace PeerLearn.Application.Common;

/// <summary>
/// Distributed lock anahtar sözleşmesi. Cüzdan kilitleri kullanıcı bazlıdır; iki cüzdan
/// birden kilitlenecekse (capture) deadlock'u önlemek için anahtarlar SIRALI alınmalıdır —
/// bunun için OrderedWalletKeys kullanın.
/// </summary>
public static class LockKeys
{
    public static string Wallet(Guid userId) => $"lock:wallet:{userId}";

    /// <summary>Deterministik sırada (Guid karşılaştırması) cüzdan kilidi anahtarları.</summary>
    public static IReadOnlyList<string> OrderedWalletKeys(params Guid[] userIds)
        => userIds.Distinct().OrderBy(id => id).Select(Wallet).ToList();

    /// <summary>
    /// Rezervasyon kilidi: EĞİTMEN bazında. Basım tavanı (MintGuard) "say, sonra yaz"
    /// biçiminde çalıştığı için sayma ile yazma arasının serileşmesi gerekir; aksi halde
    /// eşzamanlı istekler tavanı birlikte aşar.
    /// </summary>
    /// <remarks>
    /// NEDEN ÇİFT DEĞİL EĞİTMEN — ölçülerek düzeltildi (2026-08-17).
    ///
    /// Anahtar önce çift bazındaydı (lock:pair:A:B) ve gerekçesi "karşılıklı basım hep aynı
    /// ikili arasında olur" idi. Gerekçe tek başına doğruydu ama eksikti: MintGuard'ın İKİ
    /// tavanı var ve ikincisinin çiftle ilgisi yok.
    ///
    ///   • çift tavanı    (2/gün) — sayım (eğitmen, öğrenci) ikilisine bakar
    ///   • EĞİTMEN tavanı (8/gün) — sayım YALNIZCA eğitmene bakar, öğrenci kim olursa olsun
    ///
    /// İkinci sayımı değiştiren her yazma aynı eğitmene aittir, ama farklı öğrencilerden
    /// gelen istekler farklı çift anahtarlarına düşüyordu. Yani eğitmen tavanı için fiilen
    /// hiç kilit yoktu. Ölçüldü (tools/e2e-mintguard.ps1, düzeltme öncesi): 12 sahte
    /// öğrenciden gelen eşzamanlı 12 istek → 12 kabul. Tavan sıfır etkiliydi.
    ///
    /// Eğitmen anahtarı ikisini birden korur ve bu tesadüf değil: MintGuard'ın her iki
    /// sayımı da TutorUserId ile filtreleniyor, dolayısıyla herhangi bir sayımı
    /// değiştirebilecek her yazma bu kilidi almak zorundadır. Çift anahtarı geniş değil,
    /// DAR kalıyordu.
    ///
    /// Rol değiştirmeye (A→B ile B→A) karşı ayrı bir anahtar GEREKMİYOR: iki sayım da
    /// yönlüdür (TutorUserId = eğitmen), A'nın eğitmen olduğu bir ders B'nin sayımına hiç
    /// girmez — dolayısıyla iki isteğin birbirini beklemesi için sebep yok.
    /// </remarks>
    public static string Tutor(Guid tutorUserId) => $"lock:tutor:{tutorUserId}";
}
