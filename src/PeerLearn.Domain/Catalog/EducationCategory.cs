using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Catalog;

/// <summary>
/// Hiyerarşik kategori ağacı. Örn: "YKS" → "TYT" / "AYT", "Üniversite" → "Mühendislik".
/// Self-referencing yapı sayesinde yeni sınav türleri / fakülteler şema değişmeden eklenir.
/// </summary>
public class EducationCategory : BaseEntity
{
    public Guid? ParentCategoryId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public EducationCategory? ParentCategory { get; set; }
    public ICollection<EducationCategory> Children { get; set; } = new List<EducationCategory>();
    public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
}
