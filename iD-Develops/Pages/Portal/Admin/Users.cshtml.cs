using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Admin
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public class UsersModel : PageModel
    {
        private readonly IAdminUserService _adminUserService;

        public IReadOnlyList<AdminUserListItem> Users { get; private set; } = Array.Empty<AdminUserListItem>();
        public List<string> AllRoles { get; private set; } = new();

        public UsersModel(IAdminUserService adminUserService)
        {
            _adminUserService = adminUserService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var pageData = await _adminUserService.GetUsersPageDataAsync(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
            Users = pageData.Users;
            AllRoles = pageData.AssignableRoles.ToList();

            return Page();
        }

        public async Task<IActionResult> OnPostSaveRoleAsync(string userId, string roleName)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(roleName))
            {
                return RedirectToPage("/Portal/Admin/Users");
            }

            await _adminUserService.SaveRoleAsync(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, userId, roleName);
            return RedirectToPage("/Portal/Admin/Users");
        }
    }
}
