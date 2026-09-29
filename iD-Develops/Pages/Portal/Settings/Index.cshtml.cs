using System.ComponentModel.DataAnnotations;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Settings
{
    [Authorize(Policy = "PortalUser")]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ISchedulingIntegrationService _schedulingIntegrationService;

        public IndexModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ISchedulingIntegrationService schedulingIntegrationService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _schedulingIntegrationService = schedulingIntegrationService;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string DisplayName { get; private set; } = string.Empty;
        public string RoleDisplay { get; private set; } = string.Empty;
        public string CreatedAtDisplay { get; private set; } = string.Empty;
        public bool CanManageSchedulingConnections =>
            User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        public IReadOnlyList<SchedulingProviderStatus> SchedulingProviders { get; private set; } =
            Array.Empty<SchedulingProviderStatus>();

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            user.FirstName = Input.FirstName.Trim();
            user.LastName = Input.LastName.Trim();

            if (!string.Equals(user.UserName, Input.UserName, StringComparison.Ordinal))
            {
                var setUserNameResult = await _userManager.SetUserNameAsync(user, Input.UserName.Trim());
                if (!setUserNameResult.Succeeded)
                {
                    AddErrors(setUserNameResult);
                    await LoadAsync(user);
                    return Page();
                }
            }

            if (!string.Equals(user.Email, Input.Email, StringComparison.OrdinalIgnoreCase))
            {
                var setEmailResult = await _userManager.SetEmailAsync(user, Input.Email.Trim());
                if (!setEmailResult.Succeeded)
                {
                    AddErrors(setEmailResult);
                    await LoadAsync(user);
                    return Page();
                }
            }

            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            if (!string.Equals(phoneNumber, Input.PhoneNumber, StringComparison.Ordinal))
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber?.Trim());
                if (!setPhoneResult.Succeeded)
                {
                    AddErrors(setPhoneResult);
                    await LoadAsync(user);
                    return Page();
                }
            }

            if (!string.IsNullOrWhiteSpace(Input.NewPassword)
                || !string.IsNullOrWhiteSpace(Input.ConfirmNewPassword)
                || !string.IsNullOrWhiteSpace(Input.CurrentPassword))
            {
                if (string.IsNullOrWhiteSpace(Input.CurrentPassword))
                {
                    ModelState.AddModelError("Input.CurrentPassword", "Current password is required to change your password.");
                    await LoadAsync(user);
                    return Page();
                }

                if (string.IsNullOrWhiteSpace(Input.NewPassword))
                {
                    ModelState.AddModelError("Input.NewPassword", "New password is required.");
                    await LoadAsync(user);
                    return Page();
                }

                var changePasswordResult = await _userManager.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
                if (!changePasswordResult.Succeeded)
                {
                    AddErrors(changePasswordResult);
                    await LoadAsync(user);
                    return Page();
                }
            }

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                AddErrors(updateResult);
                await LoadAsync(user);
                return Page();
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your settings have been updated.";
            return RedirectToPage();
        }

        public Task<IActionResult> OnPostConnectZoomAsync()
            => BeginConnectionAsync(SchedulingProvider.Zoom);

        public Task<IActionResult> OnPostConnectGoogleCalendarAsync()
            => BeginConnectionAsync(SchedulingProvider.GoogleCalendar);

        public Task<IActionResult> OnPostDisconnectZoomAsync(CancellationToken cancellationToken)
            => DisconnectAsync(SchedulingProvider.Zoom, cancellationToken);

        public Task<IActionResult> OnPostDisconnectGoogleCalendarAsync(CancellationToken cancellationToken)
            => DisconnectAsync(SchedulingProvider.GoogleCalendar, cancellationToken);

        public async Task<IActionResult> OnPostSyncGoogleCalendarAsync(CancellationToken cancellationToken)
        {
            if (!CanManageSchedulingConnections)
                return Forbid();
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();

            var result = await _schedulingIntegrationService.SyncGoogleCalendarAsync(user.Id, cancellationToken);
            var synchronizedCount = result.CreatedCount + result.UpdatedCount;
            if (result.Success)
            {
                StatusMessage = synchronizedCount == 0
                    ? "Google Calendar is already up to date."
                    : $"Google Calendar synchronized {synchronizedCount} future event{(synchronizedCount == 1 ? "" : "s")}.";
            }
            else
            {
                ErrorMessage = $"Google Calendar synchronized {synchronizedCount} event{(synchronizedCount == 1 ? "" : "s")}, but {result.FailedCount} failed. {result.ErrorMessage}";
            }
            return RedirectToPage();
        }

        private async Task<IActionResult> BeginConnectionAsync(SchedulingProvider provider)
        {
            if (!CanManageSchedulingConnections)
                return Forbid();
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();
            var redirectUri = BuildCallbackUri(provider);
            var result = _schedulingIntegrationService.CreateAuthorizationUrl(provider, user.Id, redirectUri);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
                return RedirectToPage();
            }
            return Redirect(result.AuthorizationUrl!);
        }

        private async Task<IActionResult> DisconnectAsync(SchedulingProvider provider, CancellationToken cancellationToken)
        {
            if (!CanManageSchedulingConnections)
                return Forbid();
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();
            var result = await _schedulingIntegrationService.DisconnectAsync(provider, user.Id, cancellationToken);
            if (result.Success)
                StatusMessage = $"{ProviderName(provider)} has been disconnected.";
            else
                ErrorMessage = result.ErrorMessage;
            return RedirectToPage();
        }

        private string BuildCallbackUri(SchedulingProvider provider)
        {
            var providerPath = provider == SchedulingProvider.Zoom ? "zoom" : "google-calendar";
            return $"{Request.Scheme}://{Request.Host}{Request.PathBase}/oauth/scheduling/{providerPath}/callback";
        }

        private async Task LoadAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);

            DisplayName = user.FullName;
            RoleDisplay = roles.Count > 0 ? string.Join(", ", roles) : "Member";
            CreatedAtDisplay = user.CreatedAt.ToLocalTime().ToString("dd MMM yyyy");

            Input = new InputModel
            {
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                PhoneNumber = await _userManager.GetPhoneNumberAsync(user)
            };

            SchedulingProviders = CanManageSchedulingConnections
                ? await _schedulingIntegrationService.GetStatusesAsync(user.Id, HttpContext.RequestAborted)
                : Array.Empty<SchedulingProviderStatus>();
        }

        private static string ProviderName(SchedulingProvider provider)
            => provider == SchedulingProvider.Zoom ? "Zoom" : "Google Calendar";

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        public class InputModel
        {
            [Required]
            [StringLength(100)]
            public string FirstName { get; set; } = string.Empty;

            [Required]
            [StringLength(100)]
            public string LastName { get; set; } = string.Empty;

            [Required]
            [StringLength(100)]
            public string UserName { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Phone]
            public string? PhoneNumber { get; set; }

            [DataType(DataType.Password)]
            public string? CurrentPassword { get; set; }

            [DataType(DataType.Password)]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            public string? NewPassword { get; set; }

            [DataType(DataType.Password)]
            [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
            public string? ConfirmNewPassword { get; set; }
        }
    }
}
