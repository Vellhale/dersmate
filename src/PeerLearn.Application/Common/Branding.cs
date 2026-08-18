namespace PeerLearn.Application.Common;

/// <summary>
/// Kullanıcıya görünen ÜRÜN ADI. Sunucudan çıkan her metinde (e-posta konusu/gövdesi,
/// bildirim) buradan okunur.
/// </summary>
/// <remarks>
/// NEDEN SABİT: F4'ün sorunu ismin yanlış olması değil, DAĞILMIŞ olmasıydı — logo bir şey,
/// e-postalar başka bir şey diyordu ve ikisini aynı anda düzeltmeyi hatırlamak kimsenin
/// işi değildi. Tek kaynak olunca bir sonraki isim değişikliği tek satır.
///
/// KOD TABANI BİLİNÇLİ OLARAK "PeerLearn" KALDI: namespace'ler, proje adları, veritabanı
/// (`peerlearn`), JWT issuer/audience ve derleme yolları kullanıcıya görünmüyor.
/// Değiştirmenin bedeli gerçek (yaşayan tüm oturumlar düşer, DB göçü gerekir), karşılığı
/// yok. Yani buradaki ad ile namespace'in farklı olması bir tutarsızlık DEĞİL, bilinçli
/// bir ayrım: biri marka, diğeri altyapı kimliği.
///
/// ⚠️ İSTEMCİDE BİR İSTİSNA VAR: frontend/src/lib/hwid.js içindeki
/// <c>ctx.fillText('PeerLearn', 2, 2)</c> satırı buradan BESLENMEZ ve ASLA değişmez —
/// canvas parmak izinin sabitidir; değişirse tüm HWID banları geçersiz olur.
/// </remarks>
public static class Branding
{
    public const string ProductName = "dersmate";
}
