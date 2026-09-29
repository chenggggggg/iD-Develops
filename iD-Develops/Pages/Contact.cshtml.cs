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
        [Required(ErrorMessage = "Name is required"), StringLength(60, MinimumLength = 2, ErrorMessage = "Please enter a name with minimal 2 letters and max 60.")]
        public string DisplayName { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Email is invalid.")]
        public string From { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "Subject is required."), MaxLength(100, ErrorMessage = "Subject cannot exceed 100 characters.")]
        public string Subject { get; set; }
        [BindProperty]
        [Required(ErrorMessage = "Message is required."), MaxLength(500, ErrorMessage = "Message cannot exceed 500 characters.")]
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
