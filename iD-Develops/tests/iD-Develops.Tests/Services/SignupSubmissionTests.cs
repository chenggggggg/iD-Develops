using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using iD_Develops.Enums;
using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class SignupSubmissionTests
{
    [Fact]
    public void MvcRequiresContactIdentityButLeavesOptionalDetailsOptional()
    {
        using var services = new ServiceCollection().AddLogging().AddMvcCore().AddDataAnnotations().Services.BuildServiceProvider();
        var metadata = services.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(SignupPersonalDetails));
        Assert.True(metadata.Properties[nameof(SignupPersonalDetails.FullName)]!.IsRequired);
        Assert.True(metadata.Properties[nameof(SignupPersonalDetails.Email)]!.IsRequired);
        foreach (var optional in SignupPersonalDetails.OptionalFields)
            Assert.False(metadata.Properties[optional.Property]!.IsRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignupSavesOnceAndQueuesBothEmailsWithoutContactingTheMailProvider(bool includeOptionalDetails)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var options = Options.Create(new MailSettings { To = "owner@example.test" });
        var service = new CatalogProductSubmissionService(new ProductFormSubmissionService(db), options, new(options));
        var product = new CatalogProduct { Name = "Signup", Slug = "signup", ProductType = CatalogProductType.Signup, ConfirmationEmailBodyHtml = " " };
        var personal = new SignupPersonalDetails
        {
            FullName = "Customer",
            Email = "customer@example.test",
            Phone = includeOptionalDetails ? "+31 20 123 4567" : string.Empty,
            StreetAddress = includeOptionalDetails ? "Example Street 10" : string.Empty,
            PostalCode = includeOptionalDetails ? "1234 AB" : string.Empty,
            City = includeOptionalDetails ? "Amsterdam" : string.Empty,
            Country = includeOptionalDetails ? "Netherlands" : string.Empty
        };
        for (var index = 0; index < 2; index++)
            await service.SubmitAsync(product, personal.ToValues(),
                "customer@example.test", 1, 1, requestKey: "same-request");
        Assert.Single(await db.ProductFormSubmissions.ToListAsync());
        var queued = await db.CatalogCheckoutMails.ToListAsync();
        Assert.Equal(2, queued.Count);
        Assert.All(queued, mail => Assert.Null(mail.SentAtUtc));
        Assert.Contains(queued, mail => mail.Recipient == "customer@example.test");
        Assert.Contains(queued, mail => mail.Recipient == "owner@example.test");
        foreach (var mail in queued)
        {
            var model = JsonSerializer.Deserialize<CatalogProductSubmissionEmailModel>(mail.ModelJson)!;
            Assert.Equal("Customer", model.CustomerName);
            Assert.Equal("customer@example.test", model.CustomerEmail);
            Assert.NotNull(model.PersonalDetails);
            Assert.Equal(personal.Phone, model.PersonalDetails.Phone);
            Assert.Equal(personal.StreetAddress, model.PersonalDetails.StreetAddress);
            Assert.Equal(personal.PostalCode, model.PersonalDetails.PostalCode);
            Assert.Equal(personal.City, model.PersonalDetails.City);
            Assert.Equal(personal.Country, model.PersonalDetails.Country);
            Assert.DoesNotContain(model.SubmittedValues.Keys, SignupPersonalDetails.IsPersonalKey);
            Assert.False(string.IsNullOrWhiteSpace(model.IntroHtml));
        }
        var submission = await db.ProductFormSubmissions.SingleAsync();
        Assert.Contains("customer@example.test", submission.SubmissionDataJson);
        var saved = JsonSerializer.Deserialize<Dictionary<string, object>>(submission.SubmissionDataJson)!;
        Assert.Equal(personal.StreetAddress, saved["signup_street_address"].ToString());
    }

    [Theory]
    [InlineData("", "customer@example.test", "owner@example.test")]
    [InlineData("Customer", "", "owner@example.test")]
    [InlineData("Customer", "invalid", "owner@example.test")]
    [InlineData("Customer", "customer@example.test", "")]
    [InlineData("Customer", "customer@example.test", "invalid")]
    public async Task SignupCannotBeAcceptedWithoutPersonalDetailsAndBothRecipients(string name, string email, string company)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var options = Options.Create(new MailSettings { To = company });
        var service = new CatalogProductSubmissionService(new ProductFormSubmissionService(db), options, new(options));
        var error = await Xunit.Record.ExceptionAsync(() => service.SubmitAsync(
            new CatalogProduct { Name = "Signup", ProductType = CatalogProductType.Signup },
            new Dictionary<string, string> { [SignupPersonalDetails.FullNameKey] = name, [SignupPersonalDetails.EmailKey] = email },
            email, 1, 1));
        if (string.IsNullOrWhiteSpace(name) || !SignupPersonalDetails.IsValidEmail(email))
            Assert.IsType<ArgumentException>(error);
        else
            Assert.IsType<InvalidOperationException>(error);
        Assert.Empty(await db.ProductFormSubmissions.ToListAsync());
        Assert.Empty(await db.CatalogCheckoutMails.ToListAsync());
    }

    [Theory]
    [InlineData("name", "Your name", CatalogFormFieldType.Text, true)]
    [InlineData("field_123", "Volledige naam", CatalogFormFieldType.Text, true)]
    [InlineData("customer_email", "Your email", CatalogFormFieldType.Email, true)]
    [InlineData("phone", "Phone number", CatalogFormFieldType.Tel, true)]
    [InlineData("field_124", "Straat en huisnummer", CatalogFormFieldType.Text, true)]
    [InlineData("postcode", "Postal code", CatalogFormFieldType.Text, true)]
    [InlineData("city", "City", CatalogFormFieldType.Text, true)]
    [InlineData("country", "Country", CatalogFormFieldType.Select, true)]
    [InlineData("office", "Office phone", CatalogFormFieldType.Tel, false)]
    [InlineData("manager", "Manager name", CatalogFormFieldType.Text, false)]
    [InlineData("emergency", "Emergency contact email", CatalogFormFieldType.Email, false)]
    [InlineData("goals", "Learning goals", CatalogFormFieldType.Textarea, false)]
    public void OnlyClearlyIdentifiedPersonalQuestionsAreReplaced(string key, string label, CatalogFormFieldType type, bool expected)
        => Assert.Equal(expected, SignupPersonalDetails.IsPersonalField(new() { Key = key, Label = label, FieldType = type }));

    [Theory]
    [InlineData("signup_phone", 51)]
    [InlineData("signup_street_address", 201)]
    [InlineData("signup_postal_code", 21)]
    [InlineData("signup_city", 101)]
    [InlineData("signup_country", 101)]
    [InlineData("signup_phone", 0)]
    public async Task InvalidOptionalDetailsCannotBeSavedOrQueued(string key, int length)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var options = Options.Create(new MailSettings { To = "owner@example.test" });
        var service = new CatalogProductSubmissionService(new ProductFormSubmissionService(db), options, new(options));
        var values = new SignupPersonalDetails { FullName = "Customer", Email = "customer@example.test" }.ToValues();
        values[key] = length == 0 ? "123\n456" : new string('x', length);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(
            new CatalogProduct { Name = "Signup", ProductType = CatalogProductType.Signup }, values, "customer@example.test", 1, 1));
        Assert.Empty(await db.ProductFormSubmissions.ToListAsync());
        Assert.Empty(await db.CatalogCheckoutMails.ToListAsync());
    }

    [Fact]
    public async Task QueueFailureRollsBackSubmissionAndCanBeRetried()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var service = new ProductFormSubmissionService(db);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.SaveWithNotificationsAsync("Signup", new(), "customer@example.test",
            "request", [new CatalogCheckoutMail { ModelJson = null! }]));
        Assert.Empty(await db.ProductFormSubmissions.AsNoTracking().ToListAsync());
        await service.SaveWithNotificationsAsync("Signup", new(), "customer@example.test", "request", []);
        Assert.Single(await db.ProductFormSubmissions.ToListAsync());
    }

    [Fact]
    public async Task CancelledMailDoesNotRenderTemplatesOrEnterRetryDelays()
    {
        using var http = new HttpClient();
        var settings = Options.Create(new MailSettings());
        var client = new ResendMailClient(http, settings, NullLogger<ResendMailClient>.Instance);
        var service = new MailService(client, settings, NullLogger<MailService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendAsync("missing-template", new object(),
            "customer@example.test", "Subject", ct: cancellation.Token));
    }
}
