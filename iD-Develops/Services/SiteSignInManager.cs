using iD_Develops.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services;

public sealed class SiteSignInManager(
    UserManager<ApplicationUser> users,
    IHttpContextAccessor context,
    IUserClaimsPrincipalFactory<ApplicationUser> claims,
    IOptions<IdentityOptions> options,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    ISiteAccessPolicy access)
    : SignInManager<ApplicationUser>(users, context, claims, options, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user)
        => await access.CanSignInAsync(UserManager, user) && await base.CanSignInAsync(user);
}
