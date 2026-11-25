using System.Globalization;
using System.Text.Json;
using HtmlAgilityPack;
using iD_Develops.Configuration;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.Extensions.Options;
using Razor.Templating.Core;

namespace iD_Develops.Tests.Services;

public sealed class ProductEmailPresentationTests
{
    [Theory]
    [InlineData("en-US", "Learning goals", "Preferred days")]
    [InlineData("nl-NL", "Leerdoelen", "Voorkeursdagen")]
    public async Task QueuedEmailsSnapshotLabelsOrderLanguageAndBothRecipients(string culture, string first, string second)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var options = Options.Create(new MailSettings { To = "company@example.test" });
            var service = new CatalogProductSubmissionService(null!, options, new(options));
            var product = new CatalogProduct
            {
                Name = "Speaking course",
                ProductType = CatalogProductType.Signup,
                OwnerNotificationBodyHtml = "<p>Company introduction</p>",
                ConfirmationEmailBodyHtml = "<p>Customer introduction</p>",
                FormFields =
                [
                    new() { Key = "preferred_days", Label = "Preferred days", SortOrder = 2,
                        Translations = [new() { Culture = "nl-NL", Label = "Voorkeursdagen" }] },
                    new() { Key = "learning_goals", Label = "Learning goals", SortOrder = 1,
                        Translations = [new() { Culture = "nl-NL", Label = "Leerdoelen" }] },
                    new() { Key = "additional_remarks", Label = "Additional remarks", SortOrder = 3 }
                ]
            };
            var values = new SignupPersonalDetails { FullName = "Customer", Email = "customer@example.test" }.ToValues();
            values["learning_goals"] = "<script>alert('test')</script>";
            values["preferred_days"] = "Monday\nWednesday";
            values["additional_remarks"] = " ";
            values["order_quantity"] = "1";
            var queued = service.CreateCheckoutNotifications(product, values, "customer@example.test", 1, 1,
                "https://public.example.test", "signup:test").ToArray();
            product.FormFields.Clear();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(2, queued.Length);
            foreach (var notification in queued)
            {
                var model = JsonSerializer.Deserialize<CatalogProductSubmissionEmailModel>(notification.ModelJson)!;
                Assert.Equal(culture, model.Culture);
                Assert.Equal(new[] { first, second }, model.Answers!.Select(answer => answer.Label));
                Assert.Equal(notification.Recipient == "company@example.test", model.IsCompanyNotification);
                Assert.Contains(model.IsCompanyNotification ? "Company introduction" : "Customer introduction", model.IntroHtml);
                var html = await RazorTemplateEngine.RenderAsync("/wwwroot/templates/ProductEmailTemplate.cshtml", model);
                Assert.DoesNotContain("learning_goals", html);
                Assert.DoesNotContain("order_quantity", html);
                Assert.DoesNotContain("additional_remarks", html);
                Assert.DoesNotContain("<script>", html);
                Assert.Contains("&lt;script&gt;", html);
                Assert.DoesNotContain("fonts.googleapis.com", html);
                Assert.Contains("https://public.example.test/images/iddevelops-icon", html);
                Assert.Contains("mailto:customer@example.test", html);
                var document = new HtmlDocument();
                document.LoadHtml(html);
                Assert.Equal(new[] { "Monday", "Wednesday" }, document.DocumentNode.SelectNodes("//li").Select(node => node.InnerText));
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task LegacyQueuedPayloadStillRendersWithoutSystemFieldsOrEmptyRows()
    {
        var model = JsonSerializer.Deserialize<CatalogProductSubmissionEmailModel>("""
            {"Product":{"Name":"Old signup"},"SubmittedValues":{"learning_goals":"Conversation","blank":" ","order_quantity":"1","participant_count":"1","product_slug":"internal","workflow_type":"Signup","signup_full_name":"Customer"}}
            """)!;
        var html = await RazorTemplateEngine.RenderAsync("/wwwroot/templates/ProductEmailTemplate.cshtml", model);
        Assert.Contains("learning goals", html);
        Assert.Contains("Conversation", html);
        Assert.DoesNotContain("learning_goals", html);
        Assert.DoesNotContain("order_quantity", html);
        Assert.DoesNotContain("internal", html);
        Assert.DoesNotContain("signup_full_name", html);
        Assert.DoesNotContain(">blank<", html);
    }
}
