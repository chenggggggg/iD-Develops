namespace iD_Develops.Services
{
    public sealed record AdminUserListItem(
        string Id,
        string? FirstName,
        string? LastName,
        string? Email,
        string? UserName,
        string? CurrentRole,
        bool CanManageRole);

    public sealed record AdminUsersPageData(
        IReadOnlyList<AdminUserListItem> Users,
        IReadOnlyList<string> AssignableRoles);

    public interface IAdminUserService
    {
        Task<AdminUsersPageData> GetUsersPageDataAsync(string? actorUserId, CancellationToken cancellationToken = default);
        Task SaveRoleAsync(string? actorUserId, string userId, string roleName);
    }
}
