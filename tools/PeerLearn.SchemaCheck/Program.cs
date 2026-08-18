using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PeerLearn.Infrastructure.Persistence;

// EF modelini DB'ye bağlanmadan kurup doğrular: Fluent API'de hatalı bir konfigürasyon varsa
// burada exception fırlar. CI'da "şema hâlâ geçerli mi?" kontrolü olarak da kullanılabilir.
var options = new DbContextOptionsBuilder<PeerLearnDbContext>()
    .UseNpgsql("Host=localhost;Database=model-check-only;Username=x;Password=x")
    .Options;

using var context = new PeerLearnDbContext(options);

// Design-time model: check constraint gibi metadata'ya erişim için gerekli.
var entityTypes = context.GetService<IDesignTimeModel>().Model.GetEntityTypes().ToList();

Console.WriteLine($"Model OK — {entityTypes.Count} entity:");
foreach (var et in entityTypes.OrderBy(e => e.GetSchema()).ThenBy(e => e.GetTableName()))
{
    var indexCount = et.GetIndexes().Count();
    var checkCount = et.GetCheckConstraints().Count();
    Console.WriteLine($"  {et.GetSchema()}.{et.GetTableName(),-25} indexes: {indexCount}, checks: {checkCount}");
}
