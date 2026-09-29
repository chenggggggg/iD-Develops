using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class CreditConfigurationServiceTests
{
    [Fact]
    public async Task CreditTypes_AreNormalizedAndCaseInsensitiveDuplicatesAreRejected()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CreditConfigurationService(dbContext);

        var created = await service.CreateCreditTypeAsync(new CreditType
        {
            Name = "  Private session  ",
            SingularLabel = "session",
            PluralLabel = "sessions",
            DefaultValidityValue = 3,
            DefaultValidityUnit = CreditValidityUnit.Months
        });
        var duplicate = await service.CreateCreditTypeAsync(new CreditType
        {
            Name = "private SESSION",
            SingularLabel = "credit",
            PluralLabel = "credits"
        });

        Assert.True(created.Success);
        Assert.False(duplicate.Success);
        var stored = await dbContext.CreditTypes.SingleAsync();
        Assert.Equal("Private session", stored.Name);
        Assert.Equal("PRIVATE SESSION", stored.NormalizedName);
    }

    [Fact]
    public async Task CreditType_RejectsIncompleteValidityConfiguration()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CreditConfigurationService(dbContext);

        var result = await service.CreateCreditTypeAsync(new CreditType
        {
            Name = "Q&A credit",
            SingularLabel = "credit",
            PluralLabel = "credits",
            DefaultValidityValue = 30
        });

        Assert.False(result.Success);
        Assert.Empty(await dbContext.CreditTypes.ToListAsync());
    }

    [Fact]
    public async Task Policies_AreCreatedAndCaseInsensitiveDuplicatesAreRejected()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CreditConfigurationService(dbContext);

        var created = await service.CreatePolicyAsync(new CreditConsumptionPolicy
        {
            Name = "  24-hour cancellation  ",
            CancellationWindowHours = 24,
            AttendedAction = CreditResolutionAction.Consume,
            NoShowAction = CreditResolutionAction.Consume,
            EarlyCancellationAction = CreditResolutionAction.Return,
            LateCancellationAction = CreditResolutionAction.Consume,
            StaffCancellationAction = CreditResolutionAction.Return
        });
        var duplicate = await service.CreatePolicyAsync(new CreditConsumptionPolicy
        {
            Name = "24-HOUR CANCELLATION",
            CancellationWindowHours = 12
        });

        Assert.True(created.Success);
        Assert.False(duplicate.Success);
        var stored = await dbContext.CreditConsumptionPolicies.SingleAsync();
        Assert.Equal("24-hour cancellation", stored.Name);
        Assert.Equal("24-HOUR CANCELLATION", stored.NormalizedName);
    }

    [Fact]
    public async Task ProductGrants_SupportGlobalCourseAndClassScopes()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CreditConfigurationService(dbContext);
        var creditType = await CreateCreditTypeAsync(service, dbContext);
        var course = new Course { Name = "Speaking course" };
        var section = new CourseSection { Course = course, Title = "Live practice" };
        var courseClass = new CourseClass { CourseSection = section, Title = "Speaking class" };
        var product = new CatalogProduct
        {
            Name = "Speaking package",
            Slug = "speaking-package",
            ProductType = CatalogProductType.Enrollment,
            WorkflowType = CatalogWorkflowType.FormSubmission
        };
        dbContext.AddRange(course, section, courseClass, product);
        await dbContext.SaveChangesAsync();

        var result = await service.ReplaceProductGrantsAsync(product.Id,
        [
            new ProductCreditGrantInput { CreditTypeId = creditType.Id, Quantity = 8, Scope = CreditGrantScope.Global },
            new ProductCreditGrantInput { CreditTypeId = creditType.Id, Quantity = 2, Scope = CreditGrantScope.Course, CourseId = course.Id },
            new ProductCreditGrantInput { CreditTypeId = creditType.Id, Quantity = 1, Scope = CreditGrantScope.CourseClass, CourseClassId = courseClass.Id }
        ]);

        Assert.True(result.Success);
        var grants = await dbContext.CatalogProductCreditGrants.OrderBy(item => item.Quantity).ToListAsync();
        Assert.Equal(3, grants.Count);
        Assert.Contains(grants, item => item.Scope == CreditGrantScope.Global && item.Quantity == 8);
        Assert.Contains(grants, item => item.Scope == CreditGrantScope.Course && item.CourseId == course.Id);
        Assert.Contains(grants, item => item.Scope == CreditGrantScope.CourseClass && item.CourseClassId == courseClass.Id);
    }

    [Fact]
    public async Task ProductUpdate_RollsBackWhenGrantScopesAreDuplicated()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var creditService = new CreditConfigurationService(dbContext);
        var productService = new CatalogProductService(dbContext, creditService);
        var creditType = await CreateCreditTypeAsync(creditService, dbContext);
        var product = await productService.CreateProductAsync(new CatalogProduct
        {
            Name = "Original name",
            Slug = "original-name",
            ProductType = CatalogProductType.Credit,
            WorkflowType = CatalogWorkflowType.FormSubmission
        });

        product.Name = "Changed name";
        var action = () => productService.UpdateProductWithCreditGrantsAsync(product,
        [
            new ProductCreditGrantInput { CreditTypeId = creditType.Id, Quantity = 1 },
            new ProductCreditGrantInput { CreditTypeId = creditType.Id, Quantity = 2 }
        ], []);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.CatalogProducts.SingleAsync();
        Assert.Equal("Original name", stored.Name);
        Assert.Empty(await dbContext.CatalogProductCreditGrants.ToListAsync());
    }

    private static async Task<CreditType> CreateCreditTypeAsync(
        CreditConfigurationService service,
        DbContext dbContext)
    {
        var creditType = new CreditType
        {
            Name = "Private session",
            SingularLabel = "session",
            PluralLabel = "sessions"
        };
        var result = await service.CreateCreditTypeAsync(creditType);
        Assert.True(result.Success);
        await dbContext.Entry(creditType).ReloadAsync();
        return creditType;
    }
}
