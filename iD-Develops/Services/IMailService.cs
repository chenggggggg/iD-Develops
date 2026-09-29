using iD_Develops.Models;
using iD_Develops.Utilities;

namespace iD_Develops.Services
{
    public interface IMailService
    {
        Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default);
        Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject, string? fromEmail, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default);
    }
}
