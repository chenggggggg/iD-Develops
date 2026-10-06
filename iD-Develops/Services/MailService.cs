using iD_Develops.Configuration;
using iD_Develops.Utilities;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Polly;
using Polly.Retry;
using Razor.Templating.Core;

namespace iD_Develops.Services
{
    public class MailService : IMailService
    {
        private readonly MailSettings _settings;
        private readonly ILogger<MailService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        public MailService(IOptions<MailSettings> settings, ILogger<MailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(2000 * Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(
                            "Email delivery attempt {RetryCount} failed with {ExceptionType}. Waiting {Delay} before retrying.",
                            retryCount,
                            exception.GetType().Name,
                            timeSpan);
                    });
        }

        public async Task<OperationResult> SendAsync<TModel>(
            string templatePath,
            TModel model,
            string recipientEmail,
            string subject,
            byte[]? attachment = null,
            string? attachmentFileName = null,
            CancellationToken ct = default)
            => await SendAsync(templatePath, model, recipientEmail, subject, null, attachment, attachmentFileName, ct);

        public async Task<OperationResult> SendAsync<TModel>(
            string templatePath,
            TModel model,
            string recipientEmail,
            string subject,
            string? fromEmail,
            byte[]? attachment = null,
            string? attachmentFileName = null,
            CancellationToken ct = default)
        {
            try
            {
                var message = await BuildMessageAsync(templatePath, model, recipientEmail, subject, fromEmail, attachment, attachmentFileName);

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogDebug("Sending email.");

                    using var client = new SmtpClient();
                    var host = RequireSetting(_settings.Host, "Mail:Host");
                    var port = _settings.Port is > 0 ? _settings.Port.Value : 587;

                    await client.ConnectAsync(host, port, ResolveSecureSocketOptions(), ct);

                    if (!string.IsNullOrWhiteSpace(_settings.Username))
                    {
                        var password = RequireSetting(_settings.Password, "Mail:Password");
                        await client.AuthenticateAsync(_settings.Username, password, ct);
                    }

                    await client.SendAsync(message, ct);
                    await client.DisconnectAsync(true, ct);

                    _logger.LogDebug("Email sent successfully.");
                    return new OperationResult { Success = true, ErrorMessage = "Email sent successfully" };
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email delivery failed.");
                return new OperationResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<MimeMessage> BuildMessageAsync<TModel>(
            string templatePath,
            TModel model,
            string recipientEmail,
            string subject,
            string? fromEmail,
            byte[]? attachment,
            string? attachmentFileName)
        {
            var fromAddress = ResolveFromAddress(fromEmail);
            var htmlBody = await RazorTemplateEngine.RenderAsync(templatePath, model);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("iD! Training & Development", fromAddress));
            message.To.Add(MailboxAddress.Parse(recipientEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };

            if (attachment != null && !string.IsNullOrWhiteSpace(attachmentFileName))
            {
                bodyBuilder.Attachments.Add(attachmentFileName, attachment);
            }

            message.Body = bodyBuilder.ToMessageBody();

            return message;
        }

        private SecureSocketOptions ResolveSecureSocketOptions()
        {
            if (!string.IsNullOrWhiteSpace(_settings.SecureSocketOptions) &&
                Enum.TryParse<SecureSocketOptions>(_settings.SecureSocketOptions, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            return _settings.UseSsl == true
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
        }

        private static string RequireSetting(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{name} must be configured for SMTP mail.");
            }

            return value;
        }

        private string ResolveFromAddress(string? fromEmail)
        {
            if (!string.IsNullOrWhiteSpace(fromEmail))
            {
                return fromEmail;
            }

            return RequireSetting(_settings.From, "Mail:From");
        }
    }
}
