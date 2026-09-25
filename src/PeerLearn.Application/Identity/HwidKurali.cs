namespace PeerLearn.Application.Identity;

/// <summary>
/// İstemciden gelen cihaz özetinin (HWID hash) tek biçimlendirme kuralı.
/// </summary>
/// <remarks>
/// NEDEN TEK YER: bu değer üç tabloda aynı cihazı tanımlıyor — UserDevices (ban
/// kontrolü), RefreshTokens.DeviceHwidHash ve PushDevices.HwidHash. Push'un oturum bağı
/// ("bu cihazın en yeni token'ı aktif mi") son ikisinin EŞİTLİĞİNE dayanıyor. Giriş ve
/// yenileme bu fonksiyonun birer kopyasını taşıyordu; kayıt ucu üçüncü bir kopya yazsaydı
/// ve biri örneğin büyük/küçük harfi farklı ele alsaydı, eşitlik sessizce bozulur ve
/// hiçbir cihaz "bağlı" sayılmazdı — bildirimler hata vermeden dururdu.
///
/// ⚠️ DAVRANIŞI DEĞİŞTİRME: sonuç, veritabanındaki mevcut HWID'lerle ve HWID banlarıyla
/// karşılaştırılıyor. Kuralı değiştirmek (ör. kırpma sınırını) eski kayıtları yeni
/// girişlerle eşleşmez yapar ve banlı cihazları serbest bırakabilir.
/// </remarks>
public static class HwidKurali
{
    /// <summary>Kolon sınırı: UserDevices / RefreshTokens / PushDevices hepsi 128.</summary>
    public const int EnFazlaUzunluk = 128;

    /// <summary>
    /// Boşlukları kırpar, küçük harfe çevirir, 128 karaktere keser. Boşsa null.
    /// </summary>
    public static string? Normalize(string? hwid)
    {
        var trimmed = hwid?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, EnFazlaUzunluk)];
    }
}
