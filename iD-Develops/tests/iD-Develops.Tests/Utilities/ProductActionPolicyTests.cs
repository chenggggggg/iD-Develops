using iD_Develops.Configuration;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Utilities;

public sealed class ProductActionPolicyTests
{
    public static IEnumerable<object[]> ProductActions()
    {
        foreach (var mode in Enum.GetValues<SiteMode>())
            foreach (var workflow in Enum.GetValues<CatalogWorkflowType>())
                foreach (var preview in new[] { false, true })
                    foreach (var type in new[] { CatalogProductType.Standard, CatalogProductType.Signup, CatalogProductType.Credit })
                        yield return [mode, workflow, preview, type];
    }

    [Theory]
    [MemberData(nameof(ProductActions))]
    public void ProductPolicySeparatesPublicFormsFromAccountsAndCheckout(
        SiteMode mode, CatalogWorkflowType workflow, bool preview, CatalogProductType type)
    {
        var policy = new SiteAccessPolicy(Options.Create(new SiteAccessOptions { SiteMode = mode }));
        var product = new CatalogProduct { WorkflowType = workflow, IsPreviewOnly = preview, ProductType = type };
        var expected = !preview && (mode == SiteMode.Full ||
            type != CatalogProductType.Credit && workflow != CatalogWorkflowType.FormThenStripeCheckout);
        Assert.Equal(expected, policy.CanUseProductActions(product));
        Assert.Equal(mode == SiteMode.Full, policy.PublicAccountsEnabled);
    }

    [Theory]
    [InlineData("course")]
    [InlineData("exam")]
    [InlineData("credit")]
    [InlineData("included-credit")]
    [InlineData("account")]
    public void MarketingDoesNotExposeAccountDependentBooking(string dependency)
    {
        var product = new CatalogProduct { WorkflowType = CatalogWorkflowType.ExternalBookingOnly };
        switch (dependency)
        {
            case "course": product.GrantedCourseId = 1; break;
            case "exam": product.GrantedExamId = 1; break;
            case "credit": product.CreditGrants.Add(new CatalogProductCreditGrant()); break;
            case "included-credit": product.IncludedCreditProducts.Add(new CatalogProductIncludedCreditProduct()); break;
            case "account": product.RequiresAccountCreation = true; break;
        }
        foreach (var mode in Enum.GetValues<SiteMode>())
        {
            var policy = new SiteAccessPolicy(Options.Create(new SiteAccessOptions { SiteMode = mode }));
            Assert.Equal(mode == SiteMode.Full, policy.CanUseProductActions(product));
        }
    }
}