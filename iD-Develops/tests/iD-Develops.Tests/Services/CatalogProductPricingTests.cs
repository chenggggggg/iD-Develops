using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;

namespace iD_Develops.Tests.Services;

public sealed class CatalogProductPricingTests
{
    [Theory]
    [InlineData(CatalogDiscountType.None, 0, 100)]
    [InlineData(CatalogDiscountType.Percentage, 20, 80)]
    [InlineData(CatalogDiscountType.FixedAmount, 15, 85)]
    [InlineData(CatalogDiscountType.Percentage, 100, 0)]
    public void DiscountsReduceEachUnitBeforeVatAndLeaveFeesUnchanged(CatalogDiscountType type, int value, int expectedUnit)
    {
        var product = Product(type, value);
        var price = CatalogPricing.Calculate(product, 3);
        Assert.Equal(expectedUnit, price.UnitPrice);
        Assert.Equal(10m, price.Fees);
        Assert.Equal((expectedUnit * 3 + 10) * 1.21m, price.Total);
        Assert.Equal(375.10m, price.OriginalTotal);
        var items = CatalogStripeCheckoutService.BuildLineItems(product,
            new() { Price = 133.10m, Currency = "EUR" }, "EUR", ["tax-test"], 3);
        Assert.Equal(4, items.Count);
        Assert.Equal(expectedUnit * 100, items[0].PriceData.UnitAmount);
        Assert.Equal(3, items[0].Quantity);
        Assert.Equal(new long?[] { 500, 300, 200 }, items.Skip(1).Select(item => item.PriceData.UnitAmount));
        Assert.All(items.Skip(1), item => Assert.Equal(1, item.Quantity));
        Assert.All(items, item => Assert.Equal("tax-test", Assert.Single(item.TaxRates)));
    }

    [Fact]
    public void DiscountsRoundToCentsBeforeQuantityAndVat()
    {
        var product = new CatalogProduct { BasePrice = 19.99m, DiscountType = CatalogDiscountType.Percentage, DiscountValue = 15 };
        var price = CatalogPricing.Calculate(product, 3);
        Assert.Equal(16.99m, price.UnitPrice);
        Assert.Equal(50.97m, price.Total);
    }

    [Theory]
    [InlineData(CatalogDiscountType.Percentage, -1)]
    [InlineData(CatalogDiscountType.Percentage, 0)]
    [InlineData(CatalogDiscountType.Percentage, 101)]
    [InlineData(CatalogDiscountType.FixedAmount, 101)]
    [InlineData((CatalogDiscountType)99, 1)]
    public async Task InvalidDiscountsCannotBePersisted(CatalogDiscountType type, int value)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var service = new CatalogProductService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(Product(type, value)));
        Assert.Empty(db.CatalogProducts);
    }

    [Fact]
    public async Task DiscountCanBeSavedReloadedAndRemoved()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var service = new CatalogProductService(db);
        var created = await service.CreateProductAsync(Product(CatalogDiscountType.Percentage, 20));
        var reloaded = (await service.GetProductByIdAsync(created.Id))!;
        Assert.Equal(108.90m, reloaded.DisplayPrice);
        Assert.Equal(133.10m, reloaded.OriginalDisplayPrice);
        reloaded.DiscountType = CatalogDiscountType.None;
        await service.UpdateProductAsync(reloaded);
        var restored = (await service.GetProductByIdAsync(created.Id))!;
        Assert.Null(restored.DiscountValue);
        Assert.Equal(restored.OriginalDisplayPrice, restored.DisplayPrice);
    }

    [Fact]
    public async Task SlugChangesPreserveOldLinksAndReserveAllPreviouslyUsedSlugs()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var service = new CatalogProductService(db);
        var product = await service.CreateProductAsync(Product(CatalogDiscountType.None, 0));
        product.Name = "Updated product";
        product.Slug = "Custom Product Link!";
        await service.UpdateProductAsync(product);
        Assert.Equal("custom-product-link", product.Slug);
        Assert.Equal(product.Id, (await service.GetProductBySlugAsync("offer"))!.Id);
        var other = await service.CreateProductAsync(Product(CatalogDiscountType.None, 0));
        Assert.Equal("offer-2", other.Slug);
        product.Slug = "offer";
        await service.UpdateProductAsync(product);
        Assert.Equal(product.Id, (await service.GetProductBySlugAsync("custom-product-link"))!.Id);
        Assert.Equal("offer", (await service.GetProductByIdAsync(product.Id))!.Slug);
    }

    [Fact]
    public async Task GeneratedDuplicateSlugsStayWithinTheDatabaseLengthLimit()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var service = new CatalogProductService(db);
        var name = new string('a', 200);
        var first = await service.CreateProductAsync(new() { Name = name });
        var second = await service.CreateProductAsync(new() { Name = name });
        Assert.Equal(200, first.Slug.Length);
        Assert.Equal(200, second.Slug.Length);
        Assert.EndsWith("-2", second.Slug);
    }

    [Theory]
    [InlineData("paid", "complete", 100, true)]
    [InlineData("no_payment_required", "complete", 0, true)]
    [InlineData("no_payment_required", "complete", 10, false)]
    [InlineData("unpaid", "complete", 0, false)]
    [InlineData("no_payment_required", "open", 0, false)]
    public void FullyDiscountedCheckoutOnlyCompletesWhenStripeConfirmsTheSession(string payment, string session, int total, bool completed)
    {
        var details = new CheckoutSessionDetails { IsSessionValid = true, PaymentStatus = payment, SessionStatus = session, AmountTotal = total };
        Assert.Equal(completed, details.IsCompleted);
        details.IsSessionValid = false;
        Assert.False(details.IsCompleted);
    }

    private static CatalogProduct Product(CatalogDiscountType type, decimal value) => new()
    {
        Name = "Offer",
        Slug = "offer",
        BasePrice = 100,
        DiscountType = type,
        DiscountValue = value,
        RegistrationFee = 5,
        TransactionFee = 3,
        ServiceFee = 2,
        VatPercentage = 21
    };
}
