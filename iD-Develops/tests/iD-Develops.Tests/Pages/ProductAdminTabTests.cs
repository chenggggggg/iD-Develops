using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Admin.Products;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Pages;

public sealed class ProductAdminTabTests
{
    [Fact]
    public void BuildProductTabs_SeparatesArchivedProductsAndGroupsCurrentTypes()
    {
        var products = new[]
        {
            Product(1, CatalogProductType.Standard, CatalogProductStatus.Published),
            Product(2, CatalogProductType.Booking, CatalogProductStatus.Draft),
            Product(3, CatalogProductType.ExternalBooking, CatalogProductStatus.Published),
            Product(4, CatalogProductType.Enrollment, CatalogProductStatus.Draft),
            Product(5, CatalogProductType.Credit, CatalogProductStatus.Published),
            Product(6, CatalogProductType.FreeDownload, CatalogProductStatus.Draft),
            Product(7, CatalogProductType.Standard, CatalogProductStatus.Archived)
        };

        var tabs = IndexModel.BuildProductTabs(products).ToDictionary(tab => tab.Key);

        Assert.Equal([1, 2, 3, 4, 5, 6], tabs[ProductAdminTab.AllKey].Products.Select(product => product.Id));
        Assert.Equal([1], tabs["standard"].Products.Select(product => product.Id));
        Assert.Equal([2, 3], tabs["booking"].Products.Select(product => product.Id));
        Assert.Equal([4], tabs["signup"].Products.Select(product => product.Id));
        Assert.Equal([5], tabs["credit"].Products.Select(product => product.Id));
        Assert.Equal([6], tabs["free-download"].Products.Select(product => product.Id));
        Assert.Equal([7], tabs[ProductAdminTab.ArchivedKey].Products.Select(product => product.Id));
    }

    [Theory]
    [InlineData("ARCHIVED", "archived")]
    [InlineData("booking", "booking")]
    [InlineData("unknown", "all")]
    [InlineData(null, "all")]
    public void NormalizeTabKey_OnlyAllowsKnownProductTabs(string? requestedTab, string expectedTab)
    {
        Assert.Equal(expectedTab, IndexModel.NormalizeTabKey(requestedTab));
    }

    [Fact]
    public async Task ArchiveAndRestore_MoveProductBetweenCurrentAndArchivedTabs()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var product = Product(0, CatalogProductType.Booking, CatalogProductStatus.Published);
        dbContext.CatalogProducts.Add(product);
        await dbContext.SaveChangesAsync();

        var model = CreateModel(new CatalogProductService(dbContext));
        var archiveResult = Assert.IsType<RedirectToPageResult>(
            await model.OnPostArchiveAsync(product.Id, "booking", CancellationToken.None));

        Assert.Equal("booking", archiveResult.RouteValues!["tab"]);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            CatalogProductStatus.Archived,
            (await dbContext.CatalogProducts.SingleAsync(item => item.Id == product.Id)).Status);

        var restoreResult = Assert.IsType<RedirectToPageResult>(
            await model.OnPostRestoreAsync(product.Id, "archived", CancellationToken.None));

        Assert.Equal("archived", restoreResult.RouteValues!["tab"]);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            CatalogProductStatus.Draft,
            (await dbContext.CatalogProducts.SingleAsync(item => item.Id == product.Id)).Status);
    }

    private static CatalogProduct Product(int id, CatalogProductType type, CatalogProductStatus status)
        => new()
        {
            Id = id,
            Name = $"Product {id}",
            Slug = $"product-{id}",
            ProductType = type,
            Status = status
        };

    private static IndexModel CreateModel(ICatalogProductService service)
        => new(service, new CatalogProductTemplateService())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider())
        };

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
            => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
