using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Localization;
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
        private readonly IStringLocalizer<ContactModel> _localizer;
        private readonly ILogger<ContactModel> _logger;

        public ContactModel(
            IMailService mailService,
            IOptions<MailSettings> mailSettings,
            ITurnstileService turnstileService,
            IStringLocalizer<ContactModel> localizer,
            ILogger<ContactModel> logger)
        {
            _mailService = mailService;
            _mailSettings = mailSettings;
            _turnstileService = turnstileService;
            _localizer = localizer;
            _logger = logger;
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

            var mailSettings = _mailSettings.Value;
            string subject = "Contact Form | " + Subject;

            if (string.IsNullOrWhiteSpace(mailSettings.To))
            {
                EmailIsSent = "failed";
                return Page();
            }

            OperationResult result = await _mailService.SendAsync("/wwwroot/templates/ContactFormTemplate.cshtml", contactFormData, mailSettings.To, subject, mailSettings.ContactEmail);
            if (!result.Success)
            {
                EmailIsSent = "failed";
                return Page();
            }

            var confirmation = await _mailService.SendAsync(
                "/wwwroot/templates/ContactAcknowledgementTemplate.cshtml",
                contactFormData,
                From,
                _localizer["AcknowledgementSubject"],
                mailSettings.ContactEmail);

            EmailIsSent = confirmation.Success ? "success" : "confirmation-failed";
            if (!confirmation.Success)
            {
                _logger.LogWarning("Contact message was sent to the team, but its acknowledgement email failed.");
            }

            return Page();
        }
    }
}
