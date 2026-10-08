using System.Security.Claims;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace iD_Develops.Pages.Portal.Auth
{
    [Authorize(Policy = "PortalUser")]
    public sealed class ContinueModel : PageModel
    {
        private readonly IApplicationUrlService _applicationUrls;
        private readonly IPortalAuthenticationHandoffService _handoffService;

        public ContinueModel(
            IApplicationUrlService applicationUrls,
            IPortalAuthenticationHandoffService handoffService)
        {
            _applicationUrls = applicationUrls;
            _handoffService = handoffService;
        }

        public IActionResult OnGet(string? returnPath)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId) ||
                !PortalAuthenticationHandoffService.IsSafePublicReturnPath(returnPath))
            {
                return BadRequest();
            }

            var token = _handoffService.CreateToken(userId, returnPath!);
            var destination = QueryHelpers.AddQueryString(returnPath!, "portalAccess", token);
            return Redirect(_applicationUrls.PublicUrl(destination));
        }
    }
}
