namespace PeerLearn.Application.Common;

/// <summary>
/// Arama/filtre metin girdilerinin ön doğrulaması.
///
/// ⛔ KONTROL KARAKTERİ REDDİ (dinamik testte yakalandı). NUL (U+0000) ve diğer kontrol
/// karakterleri PostgreSQL metin alanlarına yazılamaz — Npgsql "Cannot write a string
/// with NUL characters" ile fırlatır ve bu, sorguya ulaştığında istemciye 500
/// (INTERNAL_ERROR) döner. Yani düşük yetkili HERHANGİ bir kullanıcı "?search=ab%00cd"
/// ile tek istekte iç hata ürütebiliyordu (hata günlüğünü kirletir, gerçek hataları
/// maskeler). Kontrol karakterlerini sorgudan ÖNCE reddedip 400 (VALIDATION_FAILED)
/// döndürüyoruz.
/// </summary>
public static class AramaGirdisi
{
    public static void Dogrula(params string?[] alanlar)
    {
        foreach (var alan in alanlar)
        {
            if (alan is null)
            {
                continue;
            }

            foreach (var ch in alan)
            {
                if (char.IsControl(ch))
                {
                    throw new AppException(ErrorCodes.ValidationFailed,
                        "Arama metni geçersiz karakter içeriyor.", statusCode: 400);
                }
            }
        }
    }
}
