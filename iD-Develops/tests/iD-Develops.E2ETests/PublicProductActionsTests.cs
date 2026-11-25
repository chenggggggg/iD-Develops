using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

// Run these flows against both Full and Marketing using the existing CI mode switch.
public sealed class PublicProductActionsTests : DiagnosticPageTest
{
    private const string BookingUrl = "https://koalendar.com/e/product-policy-test";

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task BookingLinkRespectsPreviewOnlyAndSalesStatus(bool preview, bool active)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        product.WorkflowType = CatalogWorkflowType.ExternalBookingOnly;
        product.ExternalBookingUrl = BookingUrl;
        product.ExternalBookingButtonText = "Book a time";
        product.IsPreviewOnly = preview;
        product.IsSalesActive = active;
        await db.SaveChangesAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = product.Name, Exact = true })).ToBeVisibleAsync();
        var booking = Page.GetByRole(AriaRole.Link, new() { Name = "Book a time", Exact = true });
        if (!preview && active)
        {
            await Expect(booking).ToBeVisibleAsync();
            await Expect(booking).ToHaveAttributeAsync("href", BookingUrl);
        }
        else
        {
            await Expect(booking).ToHaveCountAsync(0);
            if (!preview)
                await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Book a time", Exact = true })).ToBeDisabledAsync();
        }
        await Expect(Page.Locator("[data-product-preview-only=true]")).ToHaveCountAsync(preview ? 1 : 0);
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ProductFormsSaveBeforeOpeningBookingOrDownloadDestination(bool staff, bool download)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync(administrator: staff);
        try
        {
            await using var db = LocalTestApplication.CreateDbContext();
            var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
            var destination = download ? "https://downloads.example.invalid/guide.pdf" : BookingUrl;
            if (download) product.ProductType = CatalogProductType.FreeDownload;
            product.WorkflowType = download ? CatalogWorkflowType.ExternalBookingOnly : CatalogWorkflowType.FormThenExternalBooking;
            product.ExternalBookingUrl = download ? null : destination;
            product.IncludedBookingBenefitUrl = download ? destination : null;
            product.FormFields = [new CatalogProductFormField
            {
                Key = "email", Label = "Booking email", FieldType = CatalogFormFieldType.Email, IsRequired = true
            }];
            await db.SaveChangesAsync();
            if (staff)
                await BrowserLogin.SignInAsync(Page, data.User.Email!, data.Password, "/profile", Page.Locator("[data-portal-page-content]"));

            await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}");
            await ProductFormSteps.PersonalAsync(Page, "Booking customer", data.User.Email!);
            await ProductFormSteps.QuestionsAsync(Page);
            await PublicProductSubmissionPacing.ReserveAsync();
            await Expect(Page.Locator("#product-submit-form button[type=submit]")).ToBeEnabledAsync();
            // Inspect the real POST and Location header without visiting a third-party
            // calendar. Browser routing does not intercept every redirect-chain hop.
            var response = await Page.RunAndWaitForResponseAsync(() => Page.EvaluateAsync("""
                async () => {
                    const form = document.getElementById('product-submit-form');
                    await fetch(form.action, {method:'POST', body:new FormData(form), redirect:'manual'});
                }
                """), response => response.Request.Method == "POST" && response.Url.Contains("/products/"));
            Assert.Equal(302, response.Status);
            Assert.Equal(destination, await response.HeaderValueAsync("location"));
            var submission = await db.ProductFormSubmissions.SingleAsync(item => item.FormName == product.Name);
            Assert.Contains(data.User.Email!, submission.SubmissionDataJson);
            Assert.Equal(2, await db.CatalogCheckoutMails.CountAsync(item => item.StripeSessionId == "signup:" + submission.RequestKey));
            Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
        }
        finally
        {
            await using var db = LocalTestApplication.CreateDbContext();
            var keys = await db.ProductFormSubmissions.Where(item => item.FormName == data.Standard.Name)
                .Select(item => "signup:" + item.RequestKey).ToListAsync();
            await db.ApplicationAuditEvents.Where(item => item.EntityType == "CheckoutSession" && item.EntityId != null && keys.Contains(item.EntityId)).ExecuteDeleteAsync();
            await db.CatalogCheckoutMails.Where(item => keys.Contains(item.StripeSessionId)).ExecuteDeleteAsync();
            await db.ProductFormSubmissions.Where(item => item.FormName == data.Standard.Name).ExecuteDeleteAsync();
            var directory = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY");
            if (directory != null && Directory.Exists(directory))
                foreach (var path in Directory.EnumerateFiles(directory, "*.html").Where(path => File.ReadAllText(path).Contains(data.Standard.Name, StringComparison.Ordinal)))
                    File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StalePublicFormsCannotBypassPreviewOnlyOrPausedSales(bool preview)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}");
        var token = await Page.Locator("#product-submit-form input[name='__RequestVerificationToken']").InputValueAsync();
        product.IsPreviewOnly = preview;
        product.IsSalesActive = preview;
        await db.SaveChangesAsync();
        await PublicProductSubmissionPacing.ReserveAsync();
        var response = await Page.EvaluateAsync<string[]>("""
            async token => {
                const result = await fetch(location.href, {method:'POST',
                    headers:{'RequestVerificationToken':token}, body:new URLSearchParams()});
                return [String(result.status), await result.text()];
            }
            """, token);
        Assert.Equal(preview ? "403" : "200", response[0]);
        if (!preview) Assert.Contains("sales are currently paused", response[1]);
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
    }
}
