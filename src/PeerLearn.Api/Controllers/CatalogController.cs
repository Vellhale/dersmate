using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Api.Controllers;

/// <summary>Ders kataloğu (okuma). Portföy ekranı konu seçimini buradan doldurur.</summary>
[ApiController]
[Route("api/catalog")]
public sealed class CatalogController : ControllerBase
{
    private readonly IAppDbContext _db;

    public CatalogController(IAppDbContext db) => _db = db;

    public sealed record CategoryRow(Guid CategoryId, string Name, Guid? ParentCategoryId, int SortOrder);

    /// <summary>
    /// Kategori ağacı — arama ekranındaki filtre pill'lerini besler. Düz liste döner,
    /// hiyerarşiyi istemci kurar: ağaç iki seviye derin ve küçük; iç içe JSON üretmek
    /// hem sunucuda hem istemcide gereksiz karmaşıklık olurdu.
    /// </summary>
    [HttpGet("categories")]
    public async Task<IReadOnlyList<CategoryRow>> GetCategories(CancellationToken ct)
    {
        return await _db.EducationCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.Name, c.ParentCategoryId, c.SortOrder))
            .ToListAsync(ct);
    }

    public sealed record TopicRow(Guid TopicId, string Topic, string Subject, string Category, string RootCategory);

    [HttpGet("topics")]
    public async Task<IReadOnlyList<TopicRow>> GetTopics(CancellationToken ct)
    {
        return await (
                from topic in _db.Topics.AsNoTracking()
                join subject in _db.Subjects on topic.SubjectId equals subject.Id
                join category in _db.EducationCategories on subject.CategoryId equals category.Id
                where topic.IsActive && subject.IsActive && category.IsActive
                orderby category.SortOrder, subject.SortOrder, topic.SortOrder
                select new TopicRow(
                    topic.Id,
                    topic.Name,
                    subject.Name,
                    category.Name,
                    category.ParentCategory != null ? category.ParentCategory.Name : category.Name))
            .ToListAsync(ct);
    }
}
