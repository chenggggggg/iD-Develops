using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;

namespace iD_Develops.Tests.Services;

public sealed class CatalogProductHomepageTests
{
    [Fact]
    public async Task GetFeaturedProductsForCultureAsync_ReturnsOnlyEligibleProductsInDisplayOrder()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        dbContext.CatalogProducts.AddRange(
            Product("later", sortOrder: 20),
            Product("first", sortOrder: 10, dutchName: "Eerste"),
            Product("draft", status: CatalogProductStatus.Draft),
            Product("inactive", isSalesActive: false),
            Product("not-featured", isFeatured: false),
            Product("hidden", hideFromProductsPage: true),
            Product("private", requireAccessToken: true),
            Product("download", productType: CatalogProductType.FreeDownload));
        await dbContext.SaveChangesAsync();

        var service = new CatalogProductService(dbContext);
        var products = await service.GetFeaturedProductsForCultureAsync("nl-NL", maximumCount: 4);

        Assert.Collection(
            products,
            product =>
            {
                Assert.Equal("first", product.Slug);
                Assert.Equal("Eerste", product.Name);
                Assert.Equal("Nederlandse kaartomschrijving", product.HomepageDescription);
                Assert.Equal("Voor elk niveau", product.HomepageSubtitle);
                Assert.Equal("Eerste voordeel\nTweede voordeel", product.HomepageHighlights);
                Assert.Equal("Bekijk deze cursus", product.HomepageButtonText);
                Assert.Equal("Populair", product.HomepageHighlightText);
                Assert.True(product.IsHomepageHighlighted);
            },
            product => Assert.Equal("later", product.Slug));
    }

    [Fact]
    public async Task GetFeaturedProductsForCultureAsync_RespectsMaximumCount()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        dbContext.CatalogProducts.AddRange(
            Product("one", sortOrder: 10),
            Product("two", sortOrder: 20));
        await dbContext.SaveChangesAsync();

        var service = new CatalogProductService(dbContext);
        var products = await service.GetFeaturedProductsForCultureAsync("en-US", maximumCount: 1);

        Assert.Single(products);
        Assert.Equal("one", products[0].Slug);
    }

    private static CatalogProduct Product(
        string slug,
        int sortOrder = 0,
        string? dutchName = null,
        CatalogProductStatus status = CatalogProductStatus.Published,
        bool isSalesActive = true,
        bool isFeatured = true,
        bool hideFromProductsPage = false,
        bool requireAccessToken = false,
        CatalogProductType productType = CatalogProductType.Standard)
    {
        var product = new CatalogProduct
        {
            Name = slug,
            Slug = slug,
            ProductType = productType,
            WorkflowType = CatalogWorkflowType.FormThenStripeCheckout,
            Status = status,
            IsSalesActive = isSalesActive,
            IsFeatured = isFeatured,
            HideFromProductsPage = hideFromProductsPage,
            RequireAccessToken = requireAccessToken,
            SortOrder = sortOrder,
            HomepageDescription = $"{slug} homepage description",
            HomepageSubtitle = $"{slug} subtitle",
            HomepageHighlights = "First benefit\nSecond benefit",
            HomepageButtonText = "View this course",
            HomepageHighlightText = "Popular",
            IsHomepageHighlighted = slug == "first"
        };

        if (dutchName != null)
        {
            product.Translations.Add(new CatalogProductTranslation
            {
                Culture = "nl-NL",
                Name = dutchName,
                HomepageDescription = "Nederlandse kaartomschrijving",
                HomepageSubtitle = "Voor elk niveau",
                HomepageHighlights = "Eerste voordeel\nTweede voordeel",
                HomepageButtonText = "Bekijk deze cursus",
                HomepageHighlightText = "Populair"
            });
        }

        return product;
    }
}
