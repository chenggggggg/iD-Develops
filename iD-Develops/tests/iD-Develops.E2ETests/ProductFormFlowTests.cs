using iD_Develops.Enums;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ProductFormFlowTests : DiagnosticPageTest
{
    [Fact]
    public async Task StaffCanInspectAFormDownloadWithoutCreatingACustomerSubmission()
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        const string fileUrl = "https://downloads.example.invalid/guide.pdf";
        product.ProductType = CatalogProductType.FreeDownload;
        product.IncludedBookingBenefitUrl = fileUrl;
        product.FormFields = [new CatalogProductFormField { Key = "goals", Label = "Goals", FieldType = CatalogFormFieldType.Text }];
        await db.SaveChangesAsync();
        var currentFile = Page.Locator($"form[data-product-id='{product.Id}'] [data-current-download-link]");
        await BrowserLogin.SignInAsync(Page, data.User.Email!, data.Password, "/products/free-downloads/edit", currentFile);
        var href = await currentFile.GetAttributeAsync("href");
        Assert.StartsWith(LocalTestApplication.PortalBaseUrl + "/products/free-downloads/edit?", href);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.EvaluateAsync("async url => { await fetch(url, {redirect:'manual'}); }", href),
            response => response.Url == href);
        Assert.Equal(302, response.Status);
        Assert.Equal(fileUrl, await response.HeaderValueAsync("location"));
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
    }

    [Theory]
    [InlineData("en-us", true)]
    [InlineData("nl-nl", true)]
    [InlineData("en-us", false)]
    [InlineData("nl-nl", false)]
    public async Task DownloadCardsAndOlderLinksRespectConfiguredForms(string culture, bool hasForm)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        const string fileUrl = "https://downloads.example.invalid/guide.pdf";
        product.ProductType = CatalogProductType.FreeDownload;
        product.WorkflowType = CatalogWorkflowType.ExternalBookingOnly;
        product.IncludedBookingBenefitUrl = fileUrl;
        product.FormFields = hasForm
            ? [new CatalogProductFormField { Key = "goals", Label = "Goals", FieldType = CatalogFormFieldType.Text }]
            : [];
        await db.SaveChangesAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/free-downloads");
        var card = Page.GetByRole(AriaRole.Article).Filter(new()
        {
            Has = Page.GetByRole(AriaRole.Heading, new() { Name = product.Name, Exact = true })
        });
        var action = card.GetByRole(AriaRole.Link);
        var handlerUrl = $"{LocalTestApplication.PublicBaseUrl}/{culture}/free-downloads?handler=Download&id={product.Id}";
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.EvaluateAsync("async url => { await fetch(url, {redirect:'manual'}); }", handlerUrl),
            response => response.Url == handlerUrl);
        Assert.Equal(302, response.Status);
        var destination = await response.HeaderValueAsync("location");
        if (hasForm)
        {
            var expected = $"/{culture}/products/{product.Slug}";
            await Expect(action).ToHaveAttributeAsync("href", expected);
            Assert.Equal(expected, destination);
            await action.ClickAsync();
            await Expect(Page.Locator("[data-signup-step='0']")).ToBeVisibleAsync();
            await Expect(Page).ToHaveURLAsync(LocalTestApplication.PublicBaseUrl + expected);
        }
        else
        {
            Assert.Equal(fileUrl, destination);
            await Expect(action).ToHaveAttributeAsync("target", "_blank");
        }
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
    }

    [Fact]
    public async Task StaffEditorAndPreviewUseTheSamePersonalDetailsAndQuestionsForEveryProductType()
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync(administrator: true);
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        product.FormFields =
        [
            new() { Key = "name", Label = "Your name", FieldType = CatalogFormFieldType.Text },
            new() { Key = "email", Label = "Your email", FieldType = CatalogFormFieldType.Email },
            new() { Key = "goals", Label = "Learning goals", FieldType = CatalogFormFieldType.Textarea }
        ];
        await db.SaveChangesAsync();
        var editor = $"/products/{product.Slug}/edit";
        await BrowserLogin.SignInAsync(Page, data.User.Email!, data.Password, editor, Page.Locator("#product-edit-form"));
        foreach (var type in Enum.GetValues<CatalogProductType>())
        {
            Console.WriteLine($"Checking editor and preview for {type}.");
            product.ProductType = type;
            product.WorkflowType = type is CatalogProductType.Signup or CatalogProductType.Enrollment
                ? CatalogWorkflowType.FormSubmission : type is CatalogProductType.Booking or CatalogProductType.ExternalBooking or CatalogProductType.FreeDownload
                    ? CatalogWorkflowType.ExternalBookingOnly : CatalogWorkflowType.FormThenStripeCheckout;
            await db.SaveChangesAsync();
            await Page.GotoAsync(LocalTestApplication.PortalBaseUrl + editor);
            await Expect(Page.Locator("[data-product-fixed-personal-details]")).ToContainTextAsync("Full name and email address are always required.");
            await Expect(Page.Locator("[data-form-field-item]")).ToHaveCountAsync(1);
            await Expect(Page.Locator("#EditInput_FormFields_0__Label")).ToHaveValueAsync("Learning goals");
            await Page.GotoAsync($"{LocalTestApplication.PortalBaseUrl}{editor}?preview=true");
            await Expect(Page.Locator("[data-signup-step='0']")).ToBeVisibleAsync();
            await ProductFormSteps.PersonalAsync(Page, "Preview customer", data.User.Email!);
            await ProductFormSteps.QuestionsAsync(Page);
            await Expect(Page.GetByLabel("Learning goals", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.Locator("#product-submit-form button[type=submit]")).ToBeDisabledAsync();
        }
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
    }

    [Theory]
    [InlineData(CatalogFormFieldType.Checkbox)]
    [InlineData(CatalogFormFieldType.NumberedList)]
    public async Task GroupedQuestionsShowInlineErrorsAndClearThemWhenAnswered(CatalogFormFieldType fieldType)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        product.ProductType = CatalogProductType.Enrollment;
        product.WorkflowType = CatalogWorkflowType.FormSubmission;
        product.FormFields = [new CatalogProductFormField
        {
            Key = "topics",
            Label = "Topics",
            FieldType = fieldType,
            IsRequired = true,
            OptionsText = "plain\tConversation\nplain\tGrammar"
        }];
        await db.SaveChangesAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}");
        await ProductFormSteps.PersonalAsync(Page, "Form customer", data.User.Email!);
        await ProductFormSteps.QuestionsAsync(Page);
        await Page.Locator("#product-submit-form button[type=submit]").ClickAsync();
        var error = Page.Locator("[data-valmsg-for='topics']");
        await Expect(error).ToBeVisibleAsync();
        await Expect(error).ToContainTextAsync("Topics");
        if (fieldType == CatalogFormFieldType.Checkbox)
            await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Conversation", Exact = true }).CheckAsync();
        else
            await Page.Locator("[name='topics_0']").FillAsync("Conversation");
        await Expect(error).ToBeEmptyAsync();
        await Expect(Page.Locator("[data-required-question] [aria-invalid=true]")).ToHaveCountAsync(0);
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
    }

    [Theory]
    [InlineData(CatalogProductType.Signup, "en-us", false)]
    [InlineData(CatalogProductType.Standard, "en-us", false)]
    [InlineData(CatalogProductType.Credit, "en-us", false)]
    [InlineData(CatalogProductType.Booking, "en-us", false)]
    [InlineData(CatalogProductType.ExternalBooking, "en-us", false)]
    [InlineData(CatalogProductType.Enrollment, "en-us", false)]
    [InlineData(CatalogProductType.DigitalCourse, "en-us", false)]
    [InlineData(CatalogProductType.BookingAddOn, "en-us", false)]
    [InlineData(CatalogProductType.FreeDownload, "en-us", false)]
    [InlineData(CatalogProductType.Signup, "nl-nl", true)]
    [InlineData(CatalogProductType.Standard, "nl-nl", true)]
    [InlineData(CatalogProductType.Credit, "nl-nl", true)]
    [InlineData(CatalogProductType.Booking, "nl-nl", true)]
    [InlineData(CatalogProductType.ExternalBooking, "nl-nl", true)]
    [InlineData(CatalogProductType.Enrollment, "nl-nl", true)]
    [InlineData(CatalogProductType.DigitalCourse, "nl-nl", true)]
    [InlineData(CatalogProductType.BookingAddOn, "nl-nl", true)]
    [InlineData(CatalogProductType.FreeDownload, "nl-nl", true)]
    public async Task EveryProductFormRequiresPersonalDetailsAndPreservesAnswers(CatalogProductType type, string culture, bool mobile)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        product.ProductType = type;
        product.WorkflowType = type is CatalogProductType.Signup or CatalogProductType.Enrollment
            ? CatalogWorkflowType.FormSubmission : type is CatalogProductType.Booking or CatalogProductType.ExternalBooking or CatalogProductType.FreeDownload
                ? CatalogWorkflowType.ExternalBookingOnly : CatalogWorkflowType.FormThenStripeCheckout;
        product.FormFields =
        [
            new() { Key = "name", Label = "Your name", FieldType = CatalogFormFieldType.Text },
            new() { Key = "email", Label = "Your email", FieldType = CatalogFormFieldType.Email },
            new() { Key = "learning_goals", Label = "Learning goals", FieldType = CatalogFormFieldType.Textarea, IsRequired = true }
        ];
        await db.SaveChangesAsync();
        await Page.SetViewportSizeAsync(mobile ? 390 : 1440, mobile ? 844 : 1000);
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/{culture}/products/{product.Slug}");
        var mode = await Page.EvaluateAsync<string>("async () => (await (await fetch('/health/version')).json()).siteMode");
        var restrictedInMarketing = type is CatalogProductType.Credit or CatalogProductType.Standard or
            CatalogProductType.DigitalCourse or CatalogProductType.BookingAddOn;
        if (restrictedInMarketing && mode == "Marketing")
        {
            // Marketing already disables Stripe checkout and account-dependent products.
            // Verify that restriction instead of collecting a dead-end form.
            await Expect(Page.Locator("[data-marketing-product]")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-signup-flow]")).ToHaveCountAsync(0);
            await Expect(Page.Locator("[data-marketing-product] a")).ToHaveAttributeAsync("href", $"/{culture}/contact");
            Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
            return;
        }
        await Expect(Page.Locator("[data-signup-step='0']")).ToBeVisibleAsync();
        await CaptureAsync("product");
        await Expect(Page.Locator("[data-signup-step='1']")).ToBeHiddenAsync();
        await ProductFormSteps.PersonalAsync(Page);
        await ProductFormSteps.QuestionsAsync(Page);
        await Expect(Page.Locator("[data-signup-step='1']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[name='Signup.FullName']")).ToBeFocusedAsync();
        await Expect(Page.Locator("[name='Signup.FullName']")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(Page.Locator("[data-valmsg-for='Signup.FullName']")).Not.ToBeEmptyAsync();
        await Page.Locator("[name='Signup.FullName']").FillAsync("Form customer");
        await Page.Locator("[name='Signup.Email']").FillAsync("invalid");
        await ProductFormSteps.QuestionsAsync(Page);
        await Expect(Page.Locator("[data-signup-step='1']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[name='Signup.Email']")).ToHaveAttributeAsync("aria-invalid", "true");
        await Page.Locator("[name='Signup.Email']").FillAsync(data.User.Email!);
        await CaptureAsync("personal");
        await ProductFormSteps.QuestionsAsync(Page);
        await Expect(Page.Locator("[data-signup-step='2']")).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Your name", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Your email", new() { Exact = true })).ToHaveCountAsync(0);
        await Page.Locator("#product-submit-form button[type=submit]").ClickAsync();
        await Expect(Page.GetByLabel("Learning goals", new() { Exact = true })).ToHaveAttributeAsync("aria-invalid", "true");
        await Page.GetByLabel("Learning goals", new() { Exact = true }).FillAsync("Keep this answer");
        await CaptureAsync("questions");
        await Page.Locator("[data-signup-step='2'] [data-signup-go='1']").ClickAsync();
        await Expect(Page.Locator("[name='Signup.FullName']")).ToHaveValueAsync("Form customer");
        await Expect(Page.Locator("[name='Signup.Email']")).ToHaveValueAsync(data.User.Email!);
        await ProductFormSteps.QuestionsAsync(Page);
        await Expect(Page.GetByLabel("Learning goals", new() { Exact = true })).ToHaveValueAsync("Keep this answer");
        Assert.False(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > innerWidth"));
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
        await Expect(Page.Locator("[data-signup-confirmation]")).ToHaveCountAsync(0);

        async Task CaptureAsync(string step)
        {
            if (type != CatalogProductType.Standard) return;
            var directory = Path.Combine(AppContext.BaseDirectory, "test-results", "product-form-layout");
            Directory.CreateDirectory(directory);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{step}-{culture}.png"), FullPage = true });
        }
    }

    [Theory]
    [InlineData(CatalogProductType.Standard, CatalogWorkflowType.FormThenStripeCheckout)]
    [InlineData(CatalogProductType.Booking, CatalogWorkflowType.FormThenExternalBooking)]
    [InlineData(CatalogProductType.FreeDownload, CatalogWorkflowType.ExternalBookingOnly)]
    public async Task ServerRejectsMissingPersonalDetailsBeforeAnyProductAction(CatalogProductType type, CatalogWorkflowType workflow)
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        product.ProductType = type;
        product.WorkflowType = workflow;
        product.FormFields = [new CatalogProductFormField { Key = "goals", Label = "Goals", FieldType = CatalogFormFieldType.Text }];
        await db.SaveChangesAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}");
        var mode = await Page.EvaluateAsync<string>("async () => (await (await fetch('/health/version')).json()).siteMode");
        await PublicProductSubmissionPacing.ReserveAsync();
        if (type == CatalogProductType.Standard && mode == "Marketing")
        {
            await Expect(Page.Locator("[data-marketing-product]")).ToBeVisibleAsync();
            await Expect(Page.Locator("#product-submit-form")).ToHaveCountAsync(0);
            var status = await Page.EvaluateAsync<int>("""
                async () => {
                    const contact = await (await fetch('/en-us/contact')).text();
                    const token = new DOMParser().parseFromString(contact, 'text/html')
                        .querySelector('[name=__RequestVerificationToken]').value;
                    return (await fetch(location.href, {method:'POST',
                        body:new URLSearchParams({__RequestVerificationToken:token})})).status;
                }
                """);
            Assert.Equal(403, status);
            Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
            Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
            return;
        }
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.EvaluateAsync("() => HTMLFormElement.prototype.submit.call(document.getElementById('product-submit-form'))"),
            response => response.Request.Method == "POST" && response.Url.Contains(product.Slug));
        Assert.Equal(200, response.Status);
        await Expect(Page.Locator("[data-signup-step='1']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-product-errors]")).ToContainTextAsync("Enter your full name");
        await Expect(Page.Locator("[data-product-errors]")).ToContainTextAsync("Enter a valid email address");
        Assert.False(await db.ProductFormSubmissions.AnyAsync(item => item.FormName == product.Name));
        Assert.False(await db.CatalogCheckoutAttempts.AnyAsync(item => item.CatalogProductId == product.Id));
    }

    [Fact]
    public async Task NonSignupSubmissionShowsConfirmationAndQueuesBothContactEmails()
    {
        await using var data = await CatalogBrowsingFixture.CreateAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        var product = await db.CatalogProducts.SingleAsync(item => item.Id == data.Standard.Id);
        product.ProductType = CatalogProductType.Enrollment;
        product.WorkflowType = CatalogWorkflowType.FormSubmission;
        product.FormFields = [new CatalogProductFormField { Key = "goals", Label = "Goals", FieldType = CatalogFormFieldType.Text }];
        await db.SaveChangesAsync();
        try
        {
            await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/products/{product.Slug}");
            await ProductFormSteps.PersonalAsync(Page, "Enrollment customer", data.User.Email!);
            await ProductFormSteps.QuestionsAsync(Page);
            await Page.GetByLabel("Goals", new() { Exact = true }).FillAsync("Practise conversation");
            await PublicProductSubmissionPacing.ReserveAsync();
            await Page.Locator("#product-submit-form button[type=submit]").ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Request received", Exact = true })).ToBeVisibleAsync();
            var submission = await db.ProductFormSubmissions.SingleAsync(item => item.FormName == product.Name);
            Assert.Equal(data.User.Email, submission.UserEmail);
            Assert.Contains("Enrollment customer", submission.SubmissionDataJson);
            Assert.Contains("Practise conversation", submission.SubmissionDataJson);
            Assert.Equal(2, await db.CatalogCheckoutMails.CountAsync(item => item.StripeSessionId == "signup:" + submission.RequestKey));
            await Page.ReloadAsync();
            await Expect(Page.Locator("[data-signup-confirmation]")).ToBeVisibleAsync();
            await Expect(Page.Locator("#product-submit-form")).ToHaveCountAsync(0);
            Assert.Equal(1, await db.ProductFormSubmissions.CountAsync(item => item.FormName == product.Name));
        }
        finally
        {
            var keys = await db.ProductFormSubmissions.Where(item => item.FormName == product.Name)
                .Select(item => "signup:" + item.RequestKey).ToListAsync();
            await db.ApplicationAuditEvents.Where(item => item.EntityType == "CheckoutSession" && item.EntityId != null && keys.Contains(item.EntityId)).ExecuteDeleteAsync();
            await db.CatalogCheckoutMails.Where(item => keys.Contains(item.StripeSessionId)).ExecuteDeleteAsync();
            await db.ProductFormSubmissions.Where(item => item.FormName == product.Name).ExecuteDeleteAsync();
            var directory = Environment.GetEnvironmentVariable("IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY");
            if (directory != null && Directory.Exists(directory))
                FileMailTestStore.DeleteMatching(directory, product.Name);
        }
    }
}
