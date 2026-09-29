using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class CatalogProductCourseTests
{
    [Fact]
    public async Task ProductCourseOptionsLoadAndAssignmentPersistsAcrossCreateAndUpdate()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var firstCourse = new Course { Name = "A course" };
        var secondCourse = new Course { Name = "B course" };
        dbContext.Courses.AddRange(firstCourse, secondCourse);
        await dbContext.SaveChangesAsync();

        var service = new CatalogProductService(dbContext);
        var courseOptions = await service.GetCourseOptionsAsync();

        Assert.Equal([firstCourse.Id, secondCourse.Id], courseOptions.Select(course => course.Id).ToArray());

        var product = await service.CreateProductAsync(new CatalogProduct
        {
            Name = "Course access",
            Slug = "course-access",
            ProductType = CatalogProductType.Enrollment,
            WorkflowType = CatalogWorkflowType.FormSubmission,
            GrantedCourseId = firstCourse.Id
        });

        var created = await service.GetProductByIdAsync(product.Id);
        Assert.NotNull(created);
        Assert.Equal(firstCourse.Id, created!.GrantedCourseId);
        Assert.Equal("A course", created.GrantedCourse!.Name);

        created.GrantedCourseId = secondCourse.Id;
        await service.UpdateProductAsync(created);

        var updated = await service.GetProductByIdAsync(product.Id);
        Assert.Equal(secondCourse.Id, updated!.GrantedCourseId);
        Assert.Equal("B course", updated.GrantedCourse!.Name);
    }

    [Fact]
    public async Task CreateProductAsync_RejectsUnknownCourse()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CatalogProductService(dbContext);

        var action = () => service.CreateProductAsync(new CatalogProduct
        {
            Name = "Invalid course access",
            Slug = "invalid-course-access",
            ProductType = CatalogProductType.Enrollment,
            WorkflowType = CatalogWorkflowType.FormSubmission,
            GrantedCourseId = 999
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal("Selected course does not exist.", exception.Message);
    }

    [Fact]
    public async Task HiddenPublishedProduct_IsExcludedFromCatalogButAvailableByDirectLink()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = new CatalogProductService(dbContext);

        await service.CreateProductAsync(new CatalogProduct
        {
            Name = "Public product",
            Slug = "public-product",
            ProductType = CatalogProductType.Standard,
            WorkflowType = CatalogWorkflowType.FormSubmission,
            Status = CatalogProductStatus.Published
        });

        var hiddenProduct = await service.CreateProductAsync(new CatalogProduct
        {
            Name = "Credit top-up",
            Slug = "credit-top-up",
            ProductType = CatalogProductType.Credit,
            WorkflowType = CatalogWorkflowType.FormSubmission,
            Status = CatalogProductStatus.Published,
            HideFromProductsPage = true
        });

        var catalog = await service.GetPublishedProductsAsync();

        Assert.Single(catalog);
        Assert.Equal("Public product", catalog[0].Name);
        Assert.NotNull(await service.GetPublishedProductBySlugAsync(hiddenProduct.Slug));
    }
}
