using iD_Develops.Services;
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
            if (publicAccountEntry || !isStaff && !StaffSignInPages.Contains(path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }
        else if (urls.IsPortalRequest(context.Request))
        {
            if (!isStaff)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        else if (!isRead)
        {
            // A product's unnamed POST starts signup/checkout, even for a staff browser.
            var productSubmission = path.Equals("/Product", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(context.Request.Query["handler"]);
            if (productSubmission || !isStaff && !PublicForms.Contains(path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next(context);
    }
}
