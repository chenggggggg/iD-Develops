using System.Security.Claims;
using iD_Develops.Configuration;
using iD_Develops.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public interface ISiteAccessPolicy
{
    bool IsMarketing { get; }
    bool PublicAccountsEnabled { get; }
    bool PublicTransactionsEnabled { get; }
    bool CanUsePortal(ClaimsPrincipal user);
    Task<bool> CanSignInAsync(UserManager<ApplicationUser> users, ApplicationUser user);
}

public sealed class SiteAccessPolicy(IOptions<SiteAccessOptions> options) : ISiteAccessPolicy
{
    public bool IsMarketing => options.Value.SiteMode == SiteMode.Marketing;
    public bool PublicAccountsEnabled => !IsMarketing;
    public bool PublicTransactionsEnabled => !IsMarketing;

    public bool CanUsePortal(ClaimsPrincipal user)
        => !IsMarketing || user.Identity?.IsAuthenticated == true &&
            (user.IsInRole("Admin") || user.IsInRole("SuperAdmin"));

    public async Task<bool> CanSignInAsync(UserManager<ApplicationUser> users, ApplicationUser user)
        => !IsMarketing || await users.IsInRoleAsync(user, "Admin") || await users.IsInRoleAsync(user, "SuperAdmin");
}

public static class SiteAccessRegistration
{
    public static IServiceCollection AddSiteAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SiteAccessOptions>()
            .Bind(configuration)
            .Validate(options => Enum.IsDefined(options.SiteMode), "SiteMode must be Marketing or Full.")
            .ValidateOnStart();
        services.AddSingleton<ISiteAccessPolicy, SiteAccessPolicy>();
        return services;
    }
}
