using iD_Develops.Configuration;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;
using Razor.Templating.Core;

namespace iD_Develops.Services;

public sealed class MailService(ResendMailClient client, IOptions<MailSettings> options, ILogger<MailService> logger) : IMailService
{
    public Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject,
        byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default, string? idempotencyKey = null)
        => SendAsync(templatePath, model, recipientEmail, subject, null, attachment, attachmentFileName, ct, idempotencyKey);

    public async Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject,
        string? fromEmail, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default,
        string? idempotencyKey = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var from = string.IsNullOrWhiteSpace(fromEmail) ? options.Value.From : fromEmail;
            if (string.IsNullOrWhiteSpace(from))
                throw new InvalidOperationException("Mail:From must be configured for Resend mail.");
            var html = await RazorTemplateEngine.RenderAsync(templatePath, model);
            return await client.SendAsync(from, recipientEmail, subject, html, attachment, attachmentFileName, idempotencyKey, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Email delivery failed using template {TemplatePath}.", templatePath);
            return new OperationResult { Success = false, ErrorMessage = "Email delivery could not be confirmed." };
        }
    }
}
