using iD_Develops.Configuration;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class ProductPreviewTests
{
    [Fact]
    public void PostgresMigrationSnapshotMatchesTheCurrentModel()
    {
        using var db = new iD_Develops.Data.ApplicationDbContext(
            new DbContextOptionsBuilder<iD_Develops.Data.ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=model_check;Username=model_check;Password=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task PreviewSettingPersistsAndRemainsBrowsableInBothLanguages()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var service = new CatalogProductService(db);
        var product = await service.CreateProductAsync(new CatalogProduct
        {
            Name = "Information",
            Slug = "information",
            Status = CatalogProductStatus.Published
        });
        Assert.False(product.IsPreviewOnly);
        await service.UpdateProductAsync(new CatalogProduct
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            Status = CatalogProductStatus.Published,
            IsPreviewOnly = true
        });
        db.ChangeTracker.Clear();
        foreach (var culture in new[] { "en-US", "nl-NL" })
        {
            var read = await service.GetLocalizedProductBySlugAsync(product.Slug, culture, requirePublished: true);
            Assert.NotNull(read);
            Assert.True(read.IsPreviewOnly);
            Assert.True(read.IsSalesActive);
        }
        var saved = await db.CatalogProducts.SingleAsync();
        saved.IsPreviewOnly = false;
        await service.UpdateProductAsync(saved);
        db.ChangeTracker.Clear();
        Assert.False((await db.CatalogProducts.SingleAsync()).IsPreviewOnly);
    }

    [Fact]
    public async Task PreviewSignupCannotCreateSubmissionOrNotifications()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var settings = Options.Create(new MailSettings { To = "company@example.test" });
        var service = new CatalogProductSubmissionService(new ProductFormSubmissionService(db), settings, new(settings));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(
            new CatalogProduct { Name = "Information", Slug = "information", ProductType = CatalogProductType.Signup, IsPreviewOnly = true },
            new Dictionary<string, string>(), "customer@example.test", 1, 1));
        Assert.Empty(await db.ProductFormSubmissions.ToListAsync());
        Assert.Empty(await db.CatalogCheckoutMails.ToListAsync());
    }
}
