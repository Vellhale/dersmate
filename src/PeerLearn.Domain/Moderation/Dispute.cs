using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Moderation;

/// <summary>
/// Ders itirazı. Açık bir itiraz varken ders Disputed durumuna alınır ve kredi transferi dondurulur;
/// karar admin panelinden verilir. Bir ders için aynı anda tek açık itiraz olabilir (partial unique index).
/// </summary>
public class Dispute : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid RaisedByUserId { get; set; }

    public DisputeReason Reason { get; set; }
    public string Description { get; set; } = null!;

    public DisputeStatus Status { get; set; } = DisputeStatus.Open;

    /// <summary>
    /// İtiraz edilen tarafın (eğitmenin) yazılı savunması.
    ///
    /// NEDEN VAR: bu alan olmadan hakem yalnızca ŞİKÂYETÇİNİN anlatısını ve eğitmenin
    /// yüklediği görseli görüyordu. Ekran görüntüsü "ne oldu"yu göstermez — dersin neden
    /// yarıda kaldığını, öğrencinin neden katılmadığını yalnızca eğitmen anlatabilir.
    /// Tek taraflı beyanla kalıcı kredi kaybına hükmetmek savunma hakkının yokluğudur.
    ///
    /// Null kalabilir: eğitmen yanıt vermeyebilir; bu da hakem için bir veridir.
    /// </summary>
    public string? TutorStatement { get; set; }

    public DateTime? TutorStatementAtUtc { get; set; }

    public Guid? ResolvedByAdminId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
