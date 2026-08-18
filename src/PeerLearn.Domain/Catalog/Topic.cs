using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Catalog;

/// <summary>
/// Konu. Örn: "Türev", "Organik Kimya". ParentTopicId ile alt konu kırılımı desteklenir
/// (Örn: "Türev" → "Türev Alma Kuralları").
/// </summary>
public class Topic : BaseEntity
{
    public Guid SubjectId { get; set; }
    public Guid? ParentTopicId { get; set; }
    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Subject Subject { get; set; } = null!;
    public Topic? ParentTopic { get; set; }
    public ICollection<Topic> SubTopics { get; set; } = new List<Topic>();
}
