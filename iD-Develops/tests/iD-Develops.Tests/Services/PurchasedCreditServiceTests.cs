using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace iD_Develops.Tests.Services;

public sealed class PurchasedCreditServiceTests
{
    [Fact]
    public async Task GrantPurchasedCreditsAsync_CreatesScopedExpiringLotOnce()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var user = new ApplicationUser
        {
            Id = "buyer",
            UserName = "buyer@example.com",
            NormalizedUserName = "BUYER@EXAMPLE.COM",
            Email = "buyer@example.com",
            NormalizedEmail = "BUYER@EXAMPLE.COM",
            EmailConfirmed = true
        };
        var course = new Course { Name = "DIY Course" };
        var creditType = new CreditType
        {
            Name = "15 minute meeting",
            NormalizedName = "15 MINUTE MEETING",
            SingularLabel = "meeting",
            PluralLabel = "meetings"
        };
        var product = new CatalogProduct
        {
            Name = "DIY meeting top-up",
            Slug = "diy-meeting-top-up"
        };
        db.AddRange(user, course, creditType, product);
        await db.SaveChangesAsync();
        var grant = new CatalogProductCreditGrant
        {
            CatalogProductId = product.Id,
            CreditTypeId = creditType.Id,
            Quantity = 2,
            ValidityValue = 3,
            ValidityUnit = CreditValidityUnit.Months,
            Scope = CreditGrantScope.Course,
            CourseId = course.Id
        };
        db.CatalogProductCreditGrants.Add(grant);
        await db.SaveChangesAsync();
        var service = new PurchasedCreditService(db, NullLogger<PurchasedCreditService>.Instance);

        await service.GrantPurchasedCreditsAsync(
            product.Id,
            user.Id,
            null,
            "checkout-session-1",
            2);
        await service.GrantPurchasedCreditsAsync(
            product.Id,
            user.Id,
            null,
            "checkout-session-1",
            2);

        var lot = await db.UserCreditLots.SingleAsync();
        Assert.Equal(4, lot.GrantedQuantity);
        Assert.Equal(4, lot.RemainingQuantity);
        Assert.Equal(CreditGrantScope.Course, lot.Scope);
        Assert.Equal(course.Id, lot.CourseId);
        Assert.Null(lot.CourseClassId);
        Assert.NotNull(lot.ExpiresAtUtc);
        Assert.InRange(lot.ExpiresAtUtc!.Value, lot.GrantedAtUtc.AddMonths(3).AddSeconds(-1), lot.GrantedAtUtc.AddMonths(3).AddSeconds(1));
        var transaction = await db.UserCreditTransactions.SingleAsync();
        Assert.Equal(CreditTransactionType.Grant, transaction.TransactionType);
        Assert.Equal(4, transaction.QuantityDelta);
    }

    [Fact]
    public async Task GrantPurchasedCreditsAsync_GrantsCreditsFromIncludedCreditProductsOnce()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var user = new ApplicationUser
        {
            Id = "bundle-buyer",
            UserName = "bundle@example.com",
            NormalizedUserName = "BUNDLE@EXAMPLE.COM",
            Email = "bundle@example.com",
            NormalizedEmail = "BUNDLE@EXAMPLE.COM",
            EmailConfirmed = true
        };
        var creditType = new CreditType
        {
            Name = "Private lesson",
            NormalizedName = "PRIVATE LESSON",
            SingularLabel = "lesson",
            PluralLabel = "lessons"
        };
        var creditProduct = new CatalogProduct
        {
            Name = "Two private lessons",
            Slug = "two-private-lessons",
            ProductType = CatalogProductType.Credit,
            HideFromProductsPage = true
        };
        var courseProduct = new CatalogProduct
        {
            Name = "Course bundle",
            Slug = "course-bundle",
            ProductType = CatalogProductType.Standard
        };
        db.AddRange(user, creditType, creditProduct, courseProduct);
        await db.SaveChangesAsync();
        var grant = new CatalogProductCreditGrant
        {
            CatalogProductId = creditProduct.Id,
            CreditTypeId = creditType.Id,
            Quantity = 2
        };
        db.AddRange(
            grant,
            new CatalogProductIncludedCreditProduct
            {
                CatalogProductId = courseProduct.Id,
                IncludedCreditProductId = creditProduct.Id
            });
        await db.SaveChangesAsync();
        var service = new PurchasedCreditService(db, NullLogger<PurchasedCreditService>.Instance);

        await service.GrantPurchasedCreditsAsync(
            courseProduct.Id,
            user.Id,
            null,
            "checkout-bundle-1",
            1);
        await service.GrantPurchasedCreditsAsync(
            courseProduct.Id,
            user.Id,
            null,
            "checkout-bundle-1",
            1);

        var lot = await db.UserCreditLots.SingleAsync();
        Assert.Equal(2, lot.GrantedQuantity);
        Assert.Equal(courseProduct.Id, lot.CatalogProductId);
        Assert.Equal(grant.Id, lot.CatalogProductCreditGrantId);
    }
}
