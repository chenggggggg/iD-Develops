using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class ProductNotificationSenderTests
{
    [Theory]
    [InlineData("info@example.test", "info@example.test")]
    [InlineData(null, "default@example.test")]
    [InlineData(" ", "default@example.test")]
    public async Task PendingNotificationsUseContactSenderWithDefaultFallbackRatherThanBillingAlias(string? contact, string expectedSender)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var settings = Options.Create(new MailSettings
        {
            From = "default@example.test",
            ContactEmail = contact,
            BillingEmail = "unauthorized-billing@example.test",
            To = "company@example.test"
        });
        var service = new CatalogProductSubmissionService(new ProductFormSubmissionService(db), settings, new(settings));
        await service.SubmitAsync(new CatalogProduct { Name = "Signup", Slug = "signup" },
            new Dictionary<string, string> { ["name"] = "Customer" }, "private@example.test", 1, 1, requestKey: "signup-request");
        // These notifications already failed before the sender configuration was corrected.
        await db.CatalogCheckoutMails.ExecuteUpdateAsync(update => update
            .SetProperty(mail => mail.Attempts, 2).SetProperty(mail => mail.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        var transport = new SenderCheckingTransport(settings.Value.From!, expectedSender);
        var dispatcher = new CatalogCheckoutMailDispatcher(db, transport, settings, NullLogger<CatalogCheckoutMailDispatcher>.Instance);
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        Assert.Equal(2, transport.Deliveries.Count);
        var notificationKeys = await db.CatalogCheckoutMails.Select(mail => mail.Id).ToListAsync();
        Assert.Equal(notificationKeys.Select(id => $"product-notification/{id:N}").Order(), transport.Keys.Order());
        Assert.All(transport.Deliveries, delivery => Assert.Equal(expectedSender, delivery.Sender));
        Assert.Contains(transport.Deliveries, delivery => delivery.Recipient == "company@example.test");
        Assert.Contains(transport.Deliveries, delivery => delivery.Recipient == "private@example.test");
        Assert.Equal(2, await db.CatalogCheckoutMails.CountAsync(mail => mail.SentAtUtc != null));
        Assert.Single(await db.ProductFormSubmissions.ToListAsync());
    }

    private sealed class SenderCheckingTransport(string defaultSender, string authorizedSender) : IMailService
    {
        public List<(string Sender, string Recipient)> Deliveries { get; } = [];
        public List<string?> Keys { get; } = [];
        public Task<OperationResult> SendAsync<T>(string templatePath, T model, string recipientEmail, string subject,
            byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default, string? idempotencyKey = null)
            => SendAsync(templatePath, model, recipientEmail, subject, null, attachment, attachmentFileName, ct);
        public Task<OperationResult> SendAsync<T>(string templatePath, T model, string recipientEmail, string subject, string? fromEmail,
            byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default, string? idempotencyKey = null)
        {
            var sender = string.IsNullOrWhiteSpace(fromEmail) ? defaultSender : fromEmail;
            Deliveries.Add((sender, recipientEmail));
            Keys.Add(idempotencyKey);
            return Task.FromResult(new OperationResult { Success = sender == authorizedSender });
        }
    }
}
