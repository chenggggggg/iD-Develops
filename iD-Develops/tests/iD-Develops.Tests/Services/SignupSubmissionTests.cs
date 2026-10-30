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
    public async Task SignupSavesOnceAndQueuesBothEmailsWithoutContactingTheMailProvider()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var options = Options.Create(new MailSettings { To = "owner@example.test" });
        var service = new CatalogProductSubmissionService(new ProductFormSubmissionService(db), options, new(options));
        var product = new CatalogProduct { Name = "Signup", Slug = "signup" };
        for (var index = 0; index < 2; index++)
            await service.SubmitAsync(product, new Dictionary<string, string> { ["name"] = "Customer" },
                "customer@example.test", 1, 1, requestKey: "same-request");
        Assert.Single(await db.ProductFormSubmissions.ToListAsync());
        var queued = await db.CatalogCheckoutMails.ToListAsync();
        Assert.Equal(2, queued.Count);
        Assert.All(queued, mail => Assert.Null(mail.SentAtUtc));
        Assert.Contains(queued, mail => mail.Recipient == "customer@example.test");
        Assert.Contains(queued, mail => mail.Recipient == "owner@example.test");
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
