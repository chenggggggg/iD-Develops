using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class CatalogProductTranslationTests
{
    [Fact]
    public async Task SaveTranslationsAsync_PreservesCanonicalEnglishContent()
    {
        using var factory = new SqliteTestDbFactory();
        int productId;
        int variantId;
        int fieldId;

        await using (var dbContext = factory.CreateDbContext())
        {
            var service = new CatalogProductService(dbContext);
            var product = await service.CreateProductAsync(CreateProduct());
            productId = product.Id;
            variantId = product.Variants.Single().Id;
            fieldId = product.FormFields.Single().Id;

            await service.SaveTranslationsAsync(
                productId,
                new CatalogProductTranslationInput(
                    "nl-nl",
                    "Nederlandse cursus",
                    "Nederlandse samenvatting",
                    "<p>Nederlandse beschrijving</p>",
                    null,
                    "Boek nu",
                    [new CatalogProductVariantTranslationInput(variantId, "Standaardoptie")],
                    [new CatalogProductFormFieldTranslationInput(
                        fieldId,
                        "E-mailadres",
                        "naam@voorbeeld.nl",
                        "We sturen de bevestiging hierheen.",
                        null)]));
        }

        await using var verificationContext = factory.CreateDbContext();
        var canonical = await verificationContext.CatalogProducts
            .Include(product => product.Variants)
            .Include(product => product.FormFields)
            .SingleAsync(product => product.Id == productId);

        Assert.Equal("English course", canonical.Name);
        Assert.Equal("English summary", canonical.Summary);
        Assert.Equal("Standard option", canonical.Variants.Single().Name);
        Assert.Equal("Email address", canonical.FormFields.Single().Label);

        var translation = await verificationContext.CatalogProductTranslations
            .SingleAsync(item => item.CatalogProductId == productId && item.Culture == "nl-NL");
        Assert.Equal("Nederlandse cursus", translation.Name);
    }

    [Fact]
    public async Task LocalizedRead_AppliesProductVariantAndFormFieldTranslations()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CatalogProductService(dbContext);
        var product = await service.CreateProductAsync(CreateProduct());
        var variant = product.Variants.Single();
        var field = product.FormFields.Single();

        await service.SaveTranslationsAsync(
            product.Id,
            new CatalogProductTranslationInput(
                "nl-NL",
                "Nederlandse cursus",
                "Nederlandse samenvatting",
                null,
                null,
                null,
                [new CatalogProductVariantTranslationInput(variant.Id, "Standaardoptie")],
                [new CatalogProductFormFieldTranslationInput(field.Id, "E-mailadres", null, null, null)]));

        var localized = await service.GetLocalizedProductBySlugAsync(
            product.Slug,
            "nl-NL",
            requirePublished: true);

        Assert.NotNull(localized);
        Assert.Equal("Nederlandse cursus", localized!.Name);
        Assert.Equal("Nederlandse samenvatting", localized.Summary);
        Assert.Equal("Standaardoptie", localized.Variants.Single().Name);
        Assert.Equal("E-mailadres", localized.FormFields.Single().Label);
    }

    [Fact]
    public async Task LocalizedRead_FallsBackToEnglishForMissingTranslatedValues()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CatalogProductService(dbContext);
        var product = await service.CreateProductAsync(CreateProduct());

        await service.SaveTranslationsAsync(
            product.Id,
            new CatalogProductTranslationInput(
                "nl-NL",
                "Nederlandse cursus",
                null,
                null,
                null,
                null,
                [],
                []));

        var localized = await service.GetLocalizedProductBySlugAsync(
            product.Slug,
            "nl-NL",
            requirePublished: true);

        Assert.NotNull(localized);
        Assert.Equal("Nederlandse cursus", localized!.Name);
        Assert.Equal("English summary", localized.Summary);
        Assert.Equal("Standard option", localized.Variants.Single().Name);
        Assert.Equal("Email address", localized.FormFields.Single().Label);
    }

    [Fact]
    public async Task LocalizedCatalogRead_UsesTranslationsForProductCards()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CatalogProductService(dbContext);
        var product = await service.CreateProductAsync(CreateProduct());

        await service.SaveTranslationsAsync(
            product.Id,
            new CatalogProductTranslationInput(
                "nl-NL",
                "Nederlandse cursus",
                "Nederlandse samenvatting",
                null,
                null,
                null,
                [],
                []));

        var products = await service.GetPublishedProductsForCultureAsync("nl-NL");

        var localized = Assert.Single(products);
        Assert.Equal("Nederlandse cursus", localized.Name);
        Assert.Equal("Nederlandse samenvatting", localized.Summary);
    }

    private static CatalogProduct CreateProduct()
        => new()
        {
            Name = "English course",
            Slug = "english-course",
            Summary = "English summary",
            ProductType = CatalogProductType.Standard,
            WorkflowType = CatalogWorkflowType.FormThenStripeCheckout,
            Status = CatalogProductStatus.Published,
            Variants =
            [
                new CatalogProductVariant
                {
                    Name = "Standard option",
                    Price = 10,
                    IsDefault = true,
                    IsActive = true
                }
            ],
            FormFields =
            [
                new CatalogProductFormField
                {
                    Key = "email",
                    Label = "Email address",
                    FieldType = CatalogFormFieldType.Email,
                    IsRequired = true
                }
            ]
        };
}
