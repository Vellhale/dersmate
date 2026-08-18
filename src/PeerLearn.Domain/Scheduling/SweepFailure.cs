using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Scheduling;

/// <summary>
/// Hangi süpürme fazının hangi kayıtta takıldığı. Yalnızca KAYIT BAZINDA başarısız
/// olabilen fazlar burada: eşleşme süresi dolumu gibi dış bağımlılığı olmayan tek
/// güncellemeler geri çekilmeye ihtiyaç duymadığı için bilerek listelenmedi.
/// </summary>
public enum SweepRecordType
{
    /// <summary>48 saatlik otomatik onay.</summary>
    SessionAutoApprove = 0,

    /// <summary>Süresi geçmiş rezervasyonun düşürülüp escrow'un iadesi.</summary>
    SessionExpire = 1
}

/// <summary>
/// Süpürücünün BİR KAYITTA tekrar tekrar başarısız olmasını kayda geçirir ve o kayda
/// yeniden ne zaman dokunulacağını söyler.
/// </summary>
/// <remarks>
/// NEDEN VAR: süpürme partisi sıralıdır (en eski önce) ve sabit boyutludur. Kalıcı olarak
/// başarısız olan bir kayıt her turda AYNI YERDE, partinin başında durur. Bir düzine böyle
/// kayıt tüm partiyi doldurur ve sağlıklı kayıtlara SIRA HİÇ GELMEZ — otomatik onay durur,
/// escrow'lar askıda kalır, üstelik dışarıdan bakınca iş "çalışıyor" görünür. Hatalı kaydı
/// atlamak (önceki düzeltme) süpürmenin çökmesini engelledi ama bu sıra tıkanmasını değil.
///
/// Üstel geri çekilme iki işi birden yapar: tıkanan kaydı sıradan çıkarır ve umutsuz bir
/// kaydı dakikada bir yeniden denemeyi bırakır (10 dk → 20 → 40 … en fazla 24 saat).
/// Vazgeçilmez: en kötü ihtimalle günde bir denenir, çünkü hatanın sebebi dışarıda
/// (veritabanı kilidi, Redis) olabilir ve kendiliğinden düzelebilir.
///
/// Satır ayrıca OPERATÖR İÇİN bir kayıttır: FailureCount ve LastError, "hangi ders
/// takıldı, neden" sorusunun tek cevabıdır; hakem panelinde metrik olarak gösterilir.
/// </remarks>
public class SweepFailure : BaseEntity
{
    public SweepRecordType RecordType { get; set; }

    /// <summary>Takılan kaydın kimliği (ders veya eşleşme). Modüller arası navigation yok.</summary>
    public Guid RecordId { get; set; }

    public int FailureCount { get; set; }

    public DateTime LastFailedAtUtc { get; set; }

    /// <summary>Bu ana kadar kayıt süpürme sorgusundan DIŞLANIR.</summary>
    public DateTime NextAttemptAtUtc { get; set; }

    /// <summary>Son hatanın kısa hâli. Yığın izi DEĞİL — o log'da; burada teşhis için özet.</summary>
    public string LastError { get; set; } = null!;
}
