using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class AdminUserService : IAdminUserService
    {
        private const string SuperAdminRole = "SuperAdmin";
        private const string AdminRole = "Admin";
        private static readonly string[] DefaultAssignableRoles = [SuperAdminRole, AdminRole, "Teacher", "Student"];

        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminUserService(
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<AdminUsersPageData> GetUsersPageDataAsync(string? actorUserId, CancellationToken cancellationToken = default)
        {
            await EnsureDefaultRolesExistAsync();
            var actorIsSuperAdmin = await IsUserInRoleAsync(actorUserId, SuperAdminRole);
            var actorIsAdmin = actorIsSuperAdmin || await IsUserInRoleAsync(actorUserId, AdminRole);

            var users = await _dbContext.Users
                .AsNoTracking()
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .ThenBy(u => u.Email)
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.UserName
                })
                .ToListAsync(cancellationToken);

            var roleAssignments = await (
                from userRole in _dbContext.UserRoles.AsNoTracking()
                join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                select new
                {
                    userRole.UserId,
                    role.Name
                })
                .ToListAsync(cancellationToken);

            var currentRolesByUserId = roleAssignments
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Name!).OrderBy(GetRoleSortIndex).ThenBy(x => x).FirstOrDefault());

            var userItems = users
                .Select(u => new AdminUserListItem(
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.UserName,
                    currentRolesByUserId.GetValueOrDefault(u.Id),
                    actorIsSuperAdmin ||
                    (actorIsAdmin && !IsPrivilegedRole(currentRolesByUserId.GetValueOrDefault(u.Id)))))
                .ToList();

            var assignableRoles = await _dbContext.Roles
                .AsNoTracking()
                .Select(r => r.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToListAsync(cancellationToken);

            assignableRoles = assignableRoles
                .Where(name => actorIsSuperAdmin || (actorIsAdmin && !IsPrivilegedRole(name)))
                .OrderBy(name =>
                {
                    return GetRoleSortIndex(name);
                })
                .ThenBy(name => name)
                .ToList();

            return new AdminUsersPageData(userItems, assignableRoles);
        }

        public async Task SaveRoleAsync(string? actorUserId, string userId, string roleName)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(roleName))
                return;

            await EnsureDefaultRolesExistAsync();
            var actorIsSuperAdmin = await IsUserInRoleAsync(actorUserId, SuperAdminRole);
            var actorIsAdmin = actorIsSuperAdmin || await IsUserInRoleAsync(actorUserId, AdminRole);
            if (!actorIsAdmin || (!actorIsSuperAdmin && IsPrivilegedRole(roleName)))
                return;

            var user = await _userManager.FindByIdAsync(userId);
            var role = await _roleManager.FindByNameAsync(roleName);

            if (user == null || role?.Name == null)
                return;

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!actorIsSuperAdmin && currentRoles.Any(IsPrivilegedRole))
                return;

            if (currentRoles.Count == 1 && string.Equals(currentRoles[0], role.Name, StringComparison.OrdinalIgnoreCase))
                return;

            if (currentRoles.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, currentRoles);

            await _userManager.AddToRoleAsync(user, role.Name);
        }

        private async Task EnsureDefaultRolesExistAsync()
        {
            foreach (var roleName in DefaultAssignableRoles)
            {
                if (await _roleManager.RoleExistsAsync(roleName))
                    continue;

                await _roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        private async Task<bool> IsUserInRoleAsync(string? userId, string roleName)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var user = await _userManager.FindByIdAsync(userId);
            return user != null && await _userManager.IsInRoleAsync(user, roleName);
        }

        private static int GetRoleSortIndex(string? roleName)
        {
            var index = Array.FindIndex(DefaultAssignableRoles, role => string.Equals(role, roleName, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : int.MaxValue;
        }

        private static bool IsPrivilegedRole(string? roleName)
        {
            return string.Equals(roleName, SuperAdminRole, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(roleName, AdminRole, StringComparison.OrdinalIgnoreCase);
        }
    }
}
