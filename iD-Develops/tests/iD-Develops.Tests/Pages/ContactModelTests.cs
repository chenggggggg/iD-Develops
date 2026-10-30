using System.Globalization;
using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Pages;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Pages;

public sealed class ContactModelTests
{
    [Theory]
    [InlineData("en-US", "We have received your message")]
    [InlineData("nl-NL", "We hebben je bericht ontvangen")]
    public async Task SuccessfulNotification_SendsLocalizedAcknowledgementToSender(string culture, string expectedSubject)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var harness = new Harness();

            Assert.IsType<PageResult>(await harness.Page.OnPostAsync());

            Assert.Equal("success", harness.Page.EmailIsSent);
            Assert.Equal(2, harness.Mail.Sent.Count);
            var notification = harness.Mail.Sent[0];
            Assert.Equal("team@example.test", notification.Recipient);
            Assert.Equal("/wwwroot/templates/ContactFormTemplate.cshtml", notification.Template);
            Assert.Equal("Contact Form | Course question", notification.Subject);
            Assert.Equal("Sender Name", notification.Model.Name);
            Assert.Equal("sender@example.test", notification.Model.Email);
            Assert.Equal("Tell me more about your courses.", notification.Model.Message);
            var receipt = harness.Mail.Sent[1];
            Assert.Equal("sender@example.test", receipt.Recipient);
            Assert.Equal("/wwwroot/templates/ContactAcknowledgementTemplate.cshtml", receipt.Template);
            Assert.StartsWith(expectedSubject, receipt.Subject, StringComparison.Ordinal);
            Assert.All(harness.Mail.Sent, mail => Assert.Equal("contact@example.test", mail.From));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task FailedTeamNotification_DoesNotSendAcknowledgement()
    {
        using var harness = new Harness(false);

        await harness.Page.OnPostAsync();

        Assert.Equal("failed", harness.Page.EmailIsSent);
        Assert.Equal("team@example.test", Assert.Single(harness.Mail.Sent).Recipient);
    }

    [Fact]
    public async Task FailedAcknowledgement_DoesNotReportTheReceivedMessageAsFailed()
    {
        using var harness = new Harness(true, false);

        await harness.Page.OnPostAsync();

        Assert.Equal("confirmation-failed", harness.Page.EmailIsSent);
        Assert.Equal(2, harness.Mail.Sent.Count);
    }

    [Fact]
    public async Task MissingTeamRecipient_DoesNotSendAnyEmail()
    {
        using var harness = new Harness();
        harness.Settings.To = " ";

        await harness.Page.OnPostAsync();

        Assert.Equal("failed", harness.Page.EmailIsSent);
        Assert.Empty(harness.Mail.Sent);
    }

    [Fact]
    public async Task InvalidForm_DoesNotSendEitherEmail()
    {
        using var harness = new Harness();
        harness.Page.ModelState.AddModelError("From", "Enter a valid email address.");

        await harness.Page.OnPostAsync();

        Assert.Null(harness.Page.EmailIsSent);
        Assert.Empty(harness.Mail.Sent);
    }

    [Fact]
    public async Task FailedSecurityVerification_DoesNotSendEitherEmail()
    {
        using var harness = new Harness();
        harness.Turnstile.Accept = false;

        await harness.Page.OnPostAsync();

        Assert.False(harness.Page.ModelState.IsValid);
        Assert.Empty(harness.Mail.Sent);
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _services;
        public MailSettings Settings { get; } = new()
        {
            To = "team@example.test",
            ContactEmail = "contact@example.test"
        };
        public RecordingMailService Mail { get; }
        public StubTurnstile Turnstile { get; } = new();
        public ContactModel Page { get; }

        public Harness(params bool[] results)
        {
            Mail = new RecordingMailService(results);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddSingleton<IOptions<MailSettings>>(Options.Create(Settings));
            services.AddSingleton<EmailBrandingFactory>();
            _services = services.BuildServiceProvider();
            Page = new ContactModel(Mail, Options.Create(Settings), Turnstile,
                _services.GetRequiredService<IStringLocalizer<ContactModel>>(), NullLogger<ContactModel>.Instance)
            {
                DisplayName = "Sender Name",
                From = "sender@example.test",
                Subject = "Course question",
                Body = "Tell me more about your courses.",
                PageContext = new PageContext
                {
                    HttpContext = new DefaultHttpContext { RequestServices = _services }
                }
            };
            Page.Request.Scheme = "https";
            Page.Request.Host = new HostString("example.test");
        }

        public void Dispose() => _services.Dispose();
    }

    private sealed record SentMail(string Template, ContactFormData Model, string Recipient, string Subject, string? From);

    private sealed class RecordingMailService(bool[] results) : IMailService
    {
        public List<SentMail> Sent { get; } = new();

        public Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject,
            byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default, string? idempotencyKey = null)
            => SendAsync(templatePath, model, recipientEmail, subject, null, attachment, attachmentFileName, ct);

        public Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject,
            string? fromEmail, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default, string? idempotencyKey = null)
        {
            var success = Sent.Count >= results.Length || results[Sent.Count];
            Sent.Add(new SentMail(templatePath, Assert.IsType<ContactFormData>(model), recipientEmail, subject, fromEmail));
            return Task.FromResult(new OperationResult { Success = success });
        }
    }

    private sealed class StubTurnstile : ITurnstileService
    {
        public bool Accept { get; set; } = true;
        public bool IsEnabled => true;

        public Task<bool> ValidateAsync(HttpContext httpContext, ModelStateDictionary modelState, CancellationToken ct = default)
        {
            if (!Accept)
                modelState.AddModelError(string.Empty, "Security verification failed.");
            return Task.FromResult(Accept);
        }
    }
}
