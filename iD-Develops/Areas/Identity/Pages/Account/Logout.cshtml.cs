// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using iD_Develops.Services;
using iD_Develops.Enums;
using System.Security.Claims;

namespace iD_Develops.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LogoutModel> _logger;
        private readonly IApplicationUrlService _applicationUrls;
        private readonly IApplicationAuditService _auditService;

        public LogoutModel(
            SignInManager<ApplicationUser> signInManager,
            ILogger<LogoutModel> logger,
            IApplicationUrlService applicationUrls,
            IApplicationAuditService auditService)
        {
            _signInManager = signInManager;
            _logger = logger;
            _applicationUrls = applicationUrls;
            _auditService = auditService;
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await _signInManager.SignOutAsync();
            await _auditService.RecordAsync(new ApplicationAuditEntry(
                ApplicationAuditEventTypes.Logout,
                AuditEventCategory.Authentication,
                AuditEventOutcome.Success,
                "User signed out.",
                ActorUserId: userId,
                SubjectUserId: userId,
                EntityType: nameof(ApplicationUser),
                EntityId: userId));
            _logger.LogInformation("User logged out.");
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return Redirect(_applicationUrls.PublicUrl("/en-us/"));
        }
    }
}
