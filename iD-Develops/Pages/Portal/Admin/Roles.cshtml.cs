using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Admin
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public class RolesModel : PageModel
    {
        public static readonly IReadOnlyList<string> FixedRoles = ["SuperAdmin", "Admin", "Teacher", "Student"];

        private readonly RoleManager<IdentityRole> _roleManager;

        public RolesModel(RoleManager<IdentityRole> roleManager)
        {
            _roleManager = roleManager;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostCreateRoleAsync(string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                return Page();
            }

            if (!FixedRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Only the built-in roles SuperAdmin, Admin, Teacher, and Student are supported.");
                return Page();
            }

            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                await _roleManager.CreateAsync(new IdentityRole(roleName));
            }

            return Page();
        }
    }
}
