using System.Text;
using iD_Develops.Configuration;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;
using Razor.Templating.Core;

namespace iD_Develops.Services
{
    public class FileMailService : IMailService
    {
        private readonly MailSettings _settings;
        private readonly ILogger<FileMailService> _logger;
        private readonly IWebHostEnvironment _environment;

        public FileMailService(
            IOptions<MailSettings> settings,
            ILogger<FileMailService> logger,
            IWebHostEnvironment environment)
        {
            _settings = settings.Value;
            _logger = logger;
            _environment = environment;
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
                var pickupDirectory = ResolvePickupDirectory();
                Directory.CreateDirectory(pickupDirectory);

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
                var safeRecipient = SanitizeFileSegment(recipientEmail);
                var baseName = $"{timestamp}-{safeRecipient}";

                var htmlBody = await RazorTemplateEngine.RenderAsync(templatePath, model);
                var htmlPath = Path.Combine(pickupDirectory, $"{baseName}.html");

                var header = new StringBuilder()
                    .AppendLine($"To: {recipientEmail}")
                    .AppendLine($"From: {ResolveFromAddress(fromEmail)}")
                    .AppendLine($"Subject: {subject}")
                    .AppendLine($"GeneratedUtc: {DateTime.UtcNow:O}")
                    .AppendLine()
                    .ToString();

                await File.WriteAllTextAsync(htmlPath, header + htmlBody, ct);

                if (attachment != null && !string.IsNullOrWhiteSpace(attachmentFileName))
                {
                    var attachmentPath = Path.Combine(
                        pickupDirectory,
                        $"{baseName}-{SanitizeFileSegment(attachmentFileName)}");
                    await File.WriteAllBytesAsync(attachmentPath, attachment, ct);
                }

                _logger.LogDebug("Mock email written to {Path}.", htmlPath);

                return new OperationResult
                {
                    Success = true,
                    ErrorMessage = $"Mock email written to {htmlPath}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Writing a mock email failed.");
                return new OperationResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private string ResolvePickupDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_settings.PickupDirectory))
            {
                return Path.IsPathRooted(_settings.PickupDirectory)
                    ? _settings.PickupDirectory
                    : Path.Combine(_environment.ContentRootPath, _settings.PickupDirectory);
            }

            return Path.Combine(_environment.ContentRootPath, "App_Data", "Mail");
        }

        private static string SanitizeFileSegment(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "message" : sanitized;
        }

        private string ResolveFromAddress(string? fromEmail)
            => string.IsNullOrWhiteSpace(fromEmail)
                ? (_settings.From ?? "local@example.test")
                : fromEmail;
    }
}
