using System.Text;
using Microsoft.AspNetCore.Authorization;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using iD_Develops.Services;
using iD_Develops.Configuration;
using Microsoft.Extensions.Options;

namespace iD_Develops.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMailService _mailService;
        private readonly MailSettings _mailSettings;

        public RegisterConfirmationModel(
            UserManager<ApplicationUser> userManager,
            IMailService mailService,
            IOptions<MailSettings> mailSettings)
        {
            _userManager = userManager;
            _mailService = mailService;
            _mailSettings = mailSettings.Value;
        }

        [BindProperty]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        public string? ReturnUrl { get; set; }

        public async Task<IActionResult> OnGetAsync(string email, string? returnUrl = null)
        {
            if (email == null)
            {
                return RedirectToPage("/Index");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return NotFound($"Unable to load user with email '{email}'.");
            }

            Email = email;
            ReturnUrl = returnUrl;

            return Page();
        }

        public async Task<IActionResult> OnPostResendEmailConfirmationAsync()
        {
            var email = Email;
            if (email == null)
            {
                return RedirectToPage("/Index");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Verification email sent. Please check your email.");
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = userId, code = code, returnUrl = ReturnUrl },
                protocol: Request.Scheme);

            // Prepare the model for the email template
            var emailModel = new EmailConfirmationModel
            {
                DisplayName = user.FullName,
                ConfirmationLink = callbackUrl,
                Branding = HttpContext.RequestServices.GetRequiredService<EmailBrandingFactory>()
                    .Create($"{Request.Scheme}://{Request.Host}")
            };

            // Send the email using your custom mail service
            var emailResult = await _mailService.SendAsync("wwwroot/templates/EmailConfirmationTemplate.cshtml",
                emailModel, email, "Confirm your email", _mailSettings.NoReplyEmail);

            if (!emailResult.Success)
            {
                ModelState.AddModelError(string.Empty, "There was an error sending the confirmation email. Please try again later.");
            }

            ModelState.AddModelError(string.Empty, "Verification email sent. Please check your email.");
            return Page();
        }
    }
}
