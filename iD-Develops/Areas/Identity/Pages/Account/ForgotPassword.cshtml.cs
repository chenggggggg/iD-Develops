// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using iD_Develops.Configuration;
using iD_Develops.Services;
using Microsoft.Extensions.Options;

namespace iD_Develops.Areas.Identity.Pages.Account
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ForgotPasswordModel> _logger;
        private readonly IMailService _mailService;
        private readonly MailSettings _mailSettings;

        public ForgotPasswordModel(UserManager<ApplicationUser> userManager,
            ILogger<ForgotPasswordModel> logger,
            IMailService mailService,
            IOptions<MailSettings> mailSettings)
        {
            _userManager = userManager;
            _logger = logger;
            _mailService = mailService;
            _mailSettings = mailSettings.Value;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
            {
                // Don't reveal that the user does not exist or is not confirmed
                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code },
                protocol: Request.Scheme);

            var emailModel = new EmailConfirmationModel
            {
                DisplayName = user.FullName,
                ConfirmationLink = callbackUrl,
                Branding = HttpContext.RequestServices.GetRequiredService<EmailBrandingFactory>()
                    .Create($"{Request.Scheme}://{Request.Host}")
            };

            var emailResult = await _mailService.SendAsync("/wwwroot/templates/EmailConfirmationTemplate.cshtml",
                emailModel, Input.Email, "Reset Password", _mailSettings.NoReplyEmail);

            if (!emailResult.Success)
            {
                _logger.LogError("Error sending password reset email: {ErrorMessage}", emailResult.ErrorMessage);
                ModelState.AddModelError(string.Empty, "There was an error sending the password reset email. Please try again later.");
                return Page();
            }

            return RedirectToPage("./ForgotPasswordConfirmation");
        }
    }
}
