using iD_Develops.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Utilities;

// Signed callbacks and health probes remain operational when public features are disabled.
public sealed class MarketingSystemEndpoint { }

public sealed class SiteAccessMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> StaffSignInPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "/Account/Login", "/Account/Logout", "/Account/AccessDenied", "/Account/Lockout",
        "/Account/LoginWith2fa", "/Account/LoginWithRecoveryCode",
        "/Account/ForgotPassword", "/Account/ForgotPasswordConfirmation",
        "/Account/ResetPassword", "/Account/ResetPasswordConfirmation",
        "/Account/ConfirmEmail", "/Account/ConfirmEmailChange"
    };

    private static readonly HashSet<string> PublicForms = new(StringComparer.OrdinalIgnoreCase)
    {
        "/Contact", "/Portal/Examination/Level-Test-Introduction",
        "/Portal/Examination/Level-Test", "/Examination/Completed"
    };

    public async Task InvokeAsync(HttpContext context, ISiteAccessPolicy access, IApplicationUrlService urls)
    {
        if (!access.IsMarketing)
        {
            await next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        var page = endpoint?.Metadata.GetMetadata<PageActionDescriptor>();
        var path = page?.ViewEnginePath ?? string.Empty;
        var isStaff = access.CanUsePortal(context.User);
        var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        var systemEndpoint = endpoint?.Metadata.GetMetadata<MarketingSystemEndpoint>() != null;

        if (path.Equals("/Examination/Completed", StringComparison.OrdinalIgnoreCase) &&
            context.Request.Query["handler"].ToString().Equals("ClaimLevelTestResult", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (systemEndpoint || isRead && context.Request.Path.StartsWithSegments("/health") ||
            path is "/Error" or "/Portal/Error" or "/BounceWebhook")
        {
            await next(context);
            return;
        }

        if (string.Equals(page?.AreaName, "Identity", StringComparison.OrdinalIgnoreCase))
        {
            // Public registration and external-provider account creation stay unavailable,
            // including when an administrator opens their direct URLs.
            var publicAccountEntry = path.StartsWith("/Account/Register", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/Account/ExternalLogin", StringComparison.OrdinalIgnoreCase);
            if (publicAccountEntry)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (!isStaff && !StaffSignInPages.Contains(path))
            {
                await DenyPortalAccessAsync(context);
                return;
            }
        }
        else if (urls.IsPortalRequest(context.Request))
        {
            if (!isStaff)
            {
                await DenyPortalAccessAsync(context);
                return;
            }
        }
        else if (!isRead)
        {
            // Product handlers load the current product and enforce its action policy.
            // Named management handlers remain staff-only.
            var productSubmission = path.Equals("/Product", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(context.Request.Query["handler"]);
            if (!productSubmission && !isStaff && !PublicForms.Contains(path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next(context);
    }

    private static Task DenyPortalAccessAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        if (DatabaseAvailability.IsAjaxOrApiRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        // Use the configured cookie handler so it preserves the original path/query
        // and applies the same login flow as protected pages in Full mode.
        return context.ChallengeAsync(IdentityConstants.ApplicationScheme);
    }
}
