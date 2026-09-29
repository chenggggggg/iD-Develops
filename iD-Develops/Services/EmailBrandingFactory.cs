using iD_Develops.Configuration;
using iD_Develops.Models;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services
{
    public class EmailBrandingFactory
    {
        private readonly MailSettings _mailSettings;

        public EmailBrandingFactory(IOptions<MailSettings> mailSettings)
        {
            _mailSettings = mailSettings.Value;
        }

        public EmailBrandingModel Create(string? baseUrl = null)
        {
            var resolvedBaseUrl = (baseUrl ?? _mailSettings.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
            {
                resolvedBaseUrl = "https://localhost:5001";
            }

            var supportEmail = FirstConfigured(
                _mailSettings.SupportEmail,
                _mailSettings.ContactEmail,
                _mailSettings.From,
                "info@id-develops.com");

            return new EmailBrandingModel
            {
                PublicBaseUrl = resolvedBaseUrl,
                LogoUrl = $"{resolvedBaseUrl}/images/iddevelops-icon+text.png",
                SupportEmail = supportEmail,
                LinkedInUrl = string.IsNullOrWhiteSpace(_mailSettings.LinkedInUrl)
                    ? "https://www.linkedin.com/in/dominiqueheemels/"
                    : _mailSettings.LinkedInUrl!,
                FacebookUrl = string.IsNullOrWhiteSpace(_mailSettings.FacebookUrl)
                    ? "https://www.facebook.com/profile.php?id=61553944244076"
                    : _mailSettings.FacebookUrl!
            };
        }

        private static string FirstConfigured(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))!;
    }
}
