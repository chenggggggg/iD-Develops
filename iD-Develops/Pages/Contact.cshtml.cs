using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Pages
{
    public class ContactModel : PageModel
    {
        [BindProperty]
        [Required(ErrorMessage = "NameRequiredError"), StringLength(60, MinimumLength = 2, ErrorMessage = "NameLengthError")]
        public string DisplayName { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "EmailRequiredError"), EmailAddress(ErrorMessage = "EmailInvalidError")]
        public string From { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "SubjectRequiredError"), MaxLength(100, ErrorMessage = "SubjectLengthError")]
        public string Subject { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "MessageRequiredError"), MaxLength(500, ErrorMessage = "MessageLengthError")]
        public string Body { get; set; }
        public string EmailIsSent { get; set; }

        private readonly IMailService _mailService;
        private readonly IOptions<MailSettings> _mailSettings;
        private readonly ITurnstileService _turnstileService;

        public ContactModel(
            IMailService mailService,
            IOptions<MailSettings> mailSettings,
            ITurnstileService turnstileService)
        {
            _mailService = mailService;
            _mailSettings = mailSettings;
            _turnstileService = turnstileService;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await _turnstileService.ValidateAsync(HttpContext, ModelState);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var contactFormData = new ContactFormData
            {
                Name = DisplayName,
                Email = From,
                Message = Body,
                Subject = Subject,
                Branding = HttpContext.RequestServices.GetRequiredService<EmailBrandingFactory>()
                    .Create($"{Request.Scheme}://{Request.Host}")
            };

            // Get email settings from _mailSettings.Value
            var mailSettings = _mailSettings.Value;
            string subject = "Contact Form | " + Subject;

            // Check if recipientEmail is not null
            if (mailSettings != null && !string.IsNullOrWhiteSpace(mailSettings.To))
            {
                // Use the settings in your SendAsync method
                OperationResult result = await _mailService.SendAsync("/wwwroot/templates/ContactFormTemplate.cshtml", contactFormData, mailSettings.To, subject, mailSettings.ContactEmail);

                if (result.Success)
                {
                    EmailIsSent = "success";
                }
                else
                {
                    EmailIsSent = "failed";
                }
            }
            else
            {
                EmailIsSent = "failed";
            }

            return Page();
        }
    }
}
