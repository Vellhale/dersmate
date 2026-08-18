using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Identity;

/// <summary>
/// Öğretmen adayı beyanı (eğitim fakültesi / pedagojik formasyon). Gönüllü (0 kredi) ders
/// ilanı açabilmenin ön koşuludur.
///
/// DÜRÜSTLÜK NOTU — BU BEYAN DOĞRULANMIŞ DEĞİLDİR: kullanıcı okulunu ve bölümünü kendi
/// yazar; sistemin bunu teyit edecek bir kaynağı yok. Kötüye kullanımın ekonomik zararı
/// SIFIRDIR (gönüllü ders anlatan zaten kredi kazanmaz), risk yalnızca rozet enflasyonudur.
/// Bu yüzden akış beyanla açılır, <see cref="VerifiedAtUtc"/> ise yönetimin sonradan
/// (öğrenci belgesi vb. ile) teyit ettiği ayrı bir durumdur. Arayüz doğrulanmış ile
/// beyan edilmiş rozeti AYIRT ETMELİDİR.
/// </summary>
public class TeacherCandidateProfile : BaseEntity
{
    public Guid UserId { get; set; }

    public string University { get; set; } = null!;
    public string Faculty { get; set; } = null!;
    public string Department { get; set; } = null!;

    /// <summary>Sınıf (1-6). Formasyon öğrencileri için mezuniyet sonrası da olabilir.</summary>
    public int? GradeYear { get; set; }

    /// <summary>Pedagojik formasyon programında mı (eğitim fakültesi dışından geliyorsa).</summary>
    public bool HasPedagogicalCertificate { get; set; }

    public DateTime DeclaredAtUtc { get; set; }

    /// <summary>Yönetim teyit ettiyse dolu. Null = yalnızca beyan.</summary>
    public DateTime? VerifiedAtUtc { get; set; }
    public Guid? VerifiedByAdminId { get; set; }

    /// <summary>
    /// Yönetim beyanı UYGUN BULMADIYSA dolu. Doğrulanmış ile beyan edilmiş arasında üçüncü
    /// bir durum gerekiyor: aksi halde uydurma bir beyan (ör. "Üniversite: asdasd") hakem
    /// kuyruğunda sonsuza dek bekler ve moderatör "baktım, kabul etmedim" diyemez.
    /// </summary>
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByAdminId { get; set; }

    /// <summary>
    /// Kararın dayanağı; kullanıcı da kendi profilinde görür.
    /// </summary>
    /// <remarks>
    /// ESKİDEN TEK DAYANAK BUYDU: sistemde belge yükleme kanalı yoktu ve doğrulama
    /// sistem dışı bir kanıta (e-postayla gelen belge) dayanıyordu — yani karar, kayıtta
    /// izi olmayan bir şeye dayanıyordu. Artık <see cref="DocumentStorageKey"/> var; not
    /// belgenin yerini almıyor, kararın gerekçesini taşıyor.
    /// </remarks>
    public string? ReviewNote { get; set; }

    /// <summary>
    /// Öğrenci belgesinin depo anahtarı (PDF ya da görsel). Beyan için ZORUNLUDUR.
    /// </summary>
    /// <remarks>
    /// NEDEN ZORUNLU: bu beyan gönüllü/0 puanlı ders anlatma yetkisi açıyor ve rozet
    /// veriyor. Belgesiz beyanda moderatörün elinde yalnızca kullanıcının yazdığı
    /// üniversite/bölüm metni oluyordu — "Üniversite: asdasd" ile gerçek bir beyan arasında
    /// karar verecek hiçbir dayanak yoktu.
    ///
    /// DB'de yalnızca ANAHTAR duruyor, dosyanın kendisi kanıt deposunda (IProofStorage) —
    /// ders kanıtlarıyla aynı yol. Belge kişisel veridir; yalnızca sahibi ve moderasyon
    /// erişebilir, listelerde/DTO'larda dosya yolu dönmez.
    /// </remarks>
    public string? DocumentStorageKey { get; set; }

    /// <summary>Belgenin MIME türü — indirirken doğru Content-Type ile dönmek için.</summary>
    public string? DocumentContentType { get; set; }

    public DateTime? DocumentUploadedAtUtc { get; set; }

    /// <summary>Belge yüklenmiş mi. Beyanın kuyruğa girebilmesi için şart.</summary>
    public bool HasDocument => DocumentStorageKey is not null;

    public bool IsVerified => VerifiedAtUtc is not null;
    public bool IsRejected => RejectedAtUtc is not null;

    /// <summary>Hakem kuyruğunda bekliyor: henüz ne doğrulanmış ne reddedilmiş.</summary>
    public bool IsPendingReview => VerifiedAtUtc is null && RejectedAtUtc is null;

    /// <summary>
    /// Durumun TEK doğru kaynağı. Hakem kuyruğu, profil kartı ve testler aynı üç değeri
    /// okuyor; her birinde iki tarihten ayrı ayrı türetmek, birinin unutulup diğerleriyle
    /// çelişmesine açık kapı bırakırdı.
    /// </summary>
    public TeacherCandidateReviewStatus ReviewStatus =>
        VerifiedAtUtc is not null ? TeacherCandidateReviewStatus.Verified
        : RejectedAtUtc is not null ? TeacherCandidateReviewStatus.Rejected
        : TeacherCandidateReviewStatus.Pending;
}
