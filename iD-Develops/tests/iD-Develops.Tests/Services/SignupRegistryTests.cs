using System.Globalization;
using System.Text.Json;
using iD_Develops.Configuration;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class SignupRegistryTests
{
    [Theory]
    [InlineData("en-US", "Your goals")]
    [InlineData("nl-NL", "Je leerdoelen")]
    public async Task NewSubmissionsKeepIdentityAndOriginalLabelsAfterTheProductChanges(string culture, string label)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var options = Options.Create(new MailSettings { To = "company@example.test" });
        var service = new CatalogProductSubmissionService(new ProductFormSubmissionService(db), options, new(options));
        var product = new CatalogProduct
        {
            Name = "Speaking",
            Slug = "speaking",
            ProductType = CatalogProductType.Signup,
            FormFields = [new() { Key = "your_goals", Label = "Your goals", Translations = [new() { Culture = "nl-NL", Label = "Je leerdoelen" }] },
                new() { Key = "instructions", Label = "", FieldType = CatalogFormFieldType.Note },
                new() { Key = "product_id", Label = "Your reference", FieldType = CatalogFormFieldType.Text }]
        };
        db.CatalogProducts.Add(product);
        await db.SaveChangesAsync();
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var values = new SignupPersonalDetails { FullName = "A learner", Email = "learner@example.test" }.ToValues();
            values["your_goals"] = "Monday\nWednesday";
            values["instructions"] = string.Empty;
            values["product_id"] = "Customer reference";
            await service.SubmitAsync(product, values, "learner@example.test", 1, 1);
        }
        finally { CultureInfo.CurrentUICulture = originalCulture; }
        var saved = await db.ProductFormSubmissions.SingleAsync();
        Assert.Equal(product.Id, saved.CatalogProductId);
        Assert.Equal("speaking", saved.ProductSlug);
        Assert.Equal("A learner", saved.CustomerName);
        product.Name = "Renamed offer";
        product.Slug = "renamed";
        db.CatalogProducts.Update(product);
        await db.SaveChangesAsync();
        var snapshot = ProductSubmissionSnapshot.Parse(saved.SubmissionDataJson);
        var answers = snapshot.Answers().ToList();
        Assert.Equal(2, answers.Count);
        var answer = answers.Single(item => item.Label == label);
        Assert.Equal("Customer reference", answers.Single(item => item.Label == "Your reference").Value);
        Assert.Equal(label, answer.Label);
        Assert.Equal("Monday\nWednesday", answer.Value);
        Assert.Equal("Speaking", saved.FormName);
        var group = Assert.Single(await new SignupRegistryService(db).GroupsAsync(default));
        Assert.Equal("Renamed offer", group.Name);
        Assert.Equal("product-" + product.Id, group.Key);
        db.CatalogProducts.Remove(product);
        await db.SaveChangesAsync();
        Assert.Equal(product.Id, (await db.ProductFormSubmissions.SingleAsync()).CatalogProductId);
        Assert.Single(await new SignupRegistryService(db).GroupsAsync(default));
    }

    [Fact]
    public async Task EqualNamesDoNotMergeDifferentProductsOrHistoricalSlugs()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var first = Row("Same name", "a@example.test", "A", 11, "first");
        var second = Row("Same name", "b@example.test", "B", 12, "second");
        var historical = Row("Same name", "c@example.test", "C", null, "first");
        var anotherHistorical = Row("Same name", "d@example.test", "D", null, "other-old-slug");
        db.ProductFormSubmissions.AddRange(first, second, historical, anotherHistorical);
        await db.SaveChangesAsync();
        var service = new SignupRegistryService(db);
        var groups = await service.GroupsAsync(default);
        Assert.Equal(4, groups.Count);
        Assert.Equal(4, groups.Select(group => group.Key).Distinct().Count());
        Assert.Equal(4, groups.Select(group => group.Label).Distinct().Count());
        foreach (var group in groups)
            Assert.Single(await service.Query(group, null, null, null).ToListAsync());
        Assert.Equal(2, groups.Count(group => group.IsHistorical));
    }

    [Fact]
    public async Task FiltersAreCombinedAndDateRangesUseAmsterdamDaylightSavingBoundaries()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var start = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);
        var inside = Row("Form", "learner@example.test", "A learner", 1, "form");
        inside.SubmissionDate = start;
        var end = Row("Form", "learner2@example.test", "A learner", 1, "form");
        end.SubmissionDate = start.AddHours(23);
        var before = Row("Form", "before@example.test", "Before", 1, "form");
        before.SubmissionDate = start.AddTicks(-1);
        var other = Row("Other", "learner3@example.test", "A learner", 2, "other");
        other.SubmissionDate = start;
        inside.SubmissionDataJson = "{\"answer\":\"not-a-customer-name\"}";
        db.ProductFormSubmissions.AddRange(inside, end, before, other);
        await db.SaveChangesAsync();
        var service = new SignupRegistryService(db);
        var group = (await service.GroupsAsync(default)).Single(group => group.ProductId == 1);
        var date = new DateOnly(2026, 3, 29);
        var match = Assert.Single(await service.Query(group, "LEARNER", date, date).ToListAsync());
        Assert.Equal(inside.Id, match.Id);
        Assert.Empty(await service.Query(group, "not-a-customer-name", null, null).ToListAsync());
        Assert.Equal(2, await service.Query(null, "learner", date, date).CountAsync());
    }

    [Fact]
    public async Task SortingPagingAndMailStatusUseTheSavedRowsAndDoNotAssumeDelivery()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var first = Row("B", "b@example.test", "Beta", 1, "b");
        var second = Row("a", "a@example.test", "alpha", 2, "a");
        first.RequestKey = "registry-first";
        second.RequestKey = "registry-second";
        first.SubmissionDate = second.SubmissionDate = DateTime.UtcNow;
        db.ProductFormSubmissions.AddRange(first, second);
        db.CatalogCheckoutMails.AddRange(
            new() { StripeSessionId = "signup:registry-first", Recipient = "b@example.test", SentAtUtc = DateTime.UtcNow },
            new() { StripeSessionId = "signup:registry-first", Recipient = "company@example.test", Attempts = 2 },
            new() { StripeSessionId = "signup:registry-second", Recipient = "a@example.test", SentAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new SignupRegistryService(db);
        var query = service.Query(null, null, null, null);
        Assert.Equal(new[] { second.Id, first.Id }, await SignupRegistryService.Order(query, "newest").Select(row => row.Id).ToArrayAsync());
        Assert.Equal(new[] { second.Id, first.Id }, await SignupRegistryService.Order(query, "product").Select(row => row.Id).ToArrayAsync());
        var rows = await service.RowsAsync(SignupRegistryService.Order(query, "name"), 0, 1, default);
        Assert.Equal(second.Id, Assert.Single(rows).Submission.Id);
        Assert.Equal("Sent", rows[0].Notifications.Label);
        rows = await service.RowsAsync(SignupRegistryService.Order(query, "name"), 1, 1, default);
        Assert.Equal(first.Id, rows[0].Submission.Id);
        Assert.Equal("Retry scheduled", rows[0].Notifications.Label);
        Assert.Equal(1, rows[0].Notifications.Sent);
        Assert.Equal("Not available", new SignupMailStatus(0, 0, false).Label);
        Assert.Equal("Pending", new SignupMailStatus(2, 0, false).Label);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("[]")]
    [InlineData("null")]
    public void InvalidLegacyPayloadDoesNotCrashTheRegistry(string json)
        => Assert.False(ProductSubmissionSnapshot.Parse(json).IsValid);

    [Fact]
    public void PersonalDetailsOnlySnapshotDoesNotShowAnIncorrectHistoricalWarning()
    {
        var snapshot = ProductSubmissionSnapshot.Parse("""{"signup_full_name":"Learner","_form_fields":[]}""");
        Assert.True(snapshot.HasFieldSnapshot);
        Assert.Empty(SignupRegistryService.Answers(snapshot, [], out var notice));
        Assert.Null(notice);
    }

    [Fact]
    public void HistoricalAnswersUseCapturedMailLabelsWhenAvailableAndExplainFallbacks()
    {
        var snapshot = ProductSubmissionSnapshot.Parse("""{"legacy_question":["Monday","Friday"],"product_slug":"old","name":"Learner"}""");
        var fallback = SignupRegistryService.Answers(snapshot, [], out var notice);
        Assert.NotNull(notice);
        Assert.Equal("legacy question", Assert.Single(fallback).Label);
        Assert.Equal("Monday\nFriday", fallback[0].Value);
        var mails = new[] { new CatalogCheckoutMail { ModelJson = JsonSerializer.Serialize(new CatalogProductSubmissionEmailModel
        {
            Product = new(), Answers = [new() { Label = "Which days?", Value = "Monday\nFriday" }]
        }) } };
        var captured = SignupRegistryService.Answers(snapshot, mails, out notice);
        Assert.Null(notice);
        Assert.Equal("Which days?", Assert.Single(captured).Label);
    }

    private static ProductFormSubmission Row(string form, string email, string name, int? productId, string? slug) => new()
    {
        FormName = form,
        UserEmail = email,
        CustomerName = name,
        CatalogProductId = productId,
        ProductSlug = slug,
        SubmissionDataJson = "{}",
        SubmissionDate = DateTime.UtcNow
    };
}
