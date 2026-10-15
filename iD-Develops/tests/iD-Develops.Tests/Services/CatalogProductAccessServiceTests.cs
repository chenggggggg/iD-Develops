using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class CatalogProductAccessServiceTests
{
    [Fact]
    public async Task ExpirePendingUsesAsync_OnlyExpiresElapsedReservations()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();
        var now = DateTime.UtcNow;
        var invite = new CatalogProductInvite
        {
            Token = "background-cleanup-test",
            CatalogProduct = new CatalogProduct
            {
                Name = "Cleanup test product",
                Slug = "cleanup-test-product",
                ProductType = CatalogProductType.Standard,
                WorkflowType = CatalogWorkflowType.FormThenStripeCheckout,
                Status = CatalogProductStatus.Published
            },
            Uses =
            {
                new CatalogProductInviteUse
                {
                    Status = CatalogInviteUseStatus.Pending,
                    ExpiresAtUtc = now.AddMinutes(-1)
                },
                new CatalogProductInviteUse
                {
                    Status = CatalogInviteUseStatus.Pending,
                    ExpiresAtUtc = now.AddMinutes(10)
                },
                new CatalogProductInviteUse
                {
                    Status = CatalogInviteUseStatus.Pending,
                    ExpiresAtUtc = null
                },
                new CatalogProductInviteUse
                {
                    Status = CatalogInviteUseStatus.Completed,
                    ExpiresAtUtc = now.AddMinutes(-1),
                    CompletedAtUtc = now.AddMinutes(-2)
                }
            }
        };
        dbContext.CatalogProductInvites.Add(invite);
        await dbContext.SaveChangesAsync();

        var service = new CatalogProductAccessService(dbContext);

        var expiredCount = await service.ExpirePendingUsesAsync();

        Assert.Equal(1, expiredCount);
        dbContext.ChangeTracker.Clear();
        var statuses = await dbContext.CatalogProductInviteUses
            .OrderBy(item => item.Id)
            .Select(item => item.Status)
            .ToListAsync();
        Assert.Equal(
            [
                CatalogInviteUseStatus.Expired,
                CatalogInviteUseStatus.Pending,
                CatalogInviteUseStatus.Pending,
                CatalogInviteUseStatus.Completed
            ],
            statuses);
        Assert.Equal(0, await service.ExpirePendingUsesAsync());
    }
}
