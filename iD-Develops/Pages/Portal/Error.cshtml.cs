using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using iD_Develops.Services;

namespace iD_Develops.Pages.Portal
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }
        public int StatusCodeValue { get; private set; }
        public string ErrorCode { get; private set; } = "server-error";
        public string ThemeClass { get; private set; } = "error-theme-danger";
        public string IconText { get; private set; } = "!";
        public string Heading { get; private set; } = "Something went wrong";
        public string Summary { get; private set; } = "An unexpected error occurred while processing your request.";
        public string Detail { get; private set; } = "Please try again in a moment. If the problem continues, contact support.";
        public string PrimaryActionLabel { get; private set; } = "Try again";
        public string PrimaryActionUrl { get; private set; } = "/";
        public string SecondaryActionLabel { get; private set; } = "Go home";
        public string SecondaryActionUrl { get; private set; } = "/";
        public string? ImagePath { get; private set; }
        public string ImageAlt { get; private set; } = "Error illustration";
        public string HomeUrl { get; private set; } = "/";
        public string? OriginalPath { get; private set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        private readonly ILogger<ErrorModel> _logger;
        private readonly IApplicationUrlService _applicationUrls;

        public ErrorModel(ILogger<ErrorModel> logger, IApplicationUrlService applicationUrls)
        {
            _logger = logger;
            _applicationUrls = applicationUrls;
        }

        public void OnGet(int? statusCode = null, string? code = null, string? returnUrl = null)
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            const bool isPortalRequest = true;
            var culture = (RouteData.Values.TryGetValue("culture", out var c) ? c?.ToString() : null) ?? "en-us";
            HomeUrl = isPortalRequest ? "/" : $"/{culture}/";
            var localReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : HomeUrl;
            PrimaryActionUrl = localReturnUrl;
            SecondaryActionUrl = HomeUrl;
            OriginalPath = localReturnUrl != HomeUrl ? localReturnUrl : null;

            ApplyErrorState(statusCode, code, culture, localReturnUrl, isPortalRequest);
            Response.StatusCode = StatusCodeValue;
        }

        private void ApplyErrorState(int? statusCode, string? code, string culture, string localReturnUrl, bool isPortalRequest)
        {
            ErrorCode = string.IsNullOrWhiteSpace(code) ? "server-error" : code.Trim().ToLowerInvariant();

            switch (ErrorCode)
            {
                case "db-offline":
                case "db-connection-refused":
                case "db-timeout":
                case "db-login-failed":
                    StatusCodeValue = StatusCodes.Status503ServiceUnavailable;
                    Heading = "Database temporarily unavailable";
                    Summary = ErrorCode switch
                    {
                        "db-timeout" => "The application reached the database server, but the request timed out.",
                        "db-login-failed" => "The application could not authenticate with the database.",
                        "db-connection-refused" => "The database server refused the connection request.",
                        _ => "We could not reach the application database just now."
                    };
                    Detail = ErrorCode switch
                    {
                        "db-timeout" => "This can happen during a restart, heavy load, or a temporary network slowdown. Try again shortly.",
                        "db-login-failed" => "This usually points to a database configuration or credential problem and needs administrator attention.",
                        "db-connection-refused" => "This usually means the database service is stopped, restarting, or not accepting connections from the app.",
                        _ => "This usually means the database server is offline, restarting, or unreachable from the app. Try again shortly."
                    };
                    ThemeClass = "error-theme-info";
                    IconText = "DB";
                    ImagePath = "/images/error-connectivity.png";
                    ImageAlt = "Database or connectivity issue";
                    PrimaryActionLabel = "Try again";
                    PrimaryActionUrl = localReturnUrl;
                    SecondaryActionLabel = "Go home";
                    SecondaryActionUrl = HomeUrl;
                    return;
                case "bad-request":
                    StatusCodeValue = StatusCodes.Status400BadRequest;
                    Heading = "Invalid request";
                    Summary = "The request could not be processed in its current form.";
                    Detail = "Please check the input or try the action again from the previous page.";
                    ThemeClass = "error-theme-warning";
                    IconText = "400";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = "Invalid request";
                    PrimaryActionLabel = "Try again";
                    PrimaryActionUrl = localReturnUrl;
                    SecondaryActionLabel = "Go home";
                    SecondaryActionUrl = HomeUrl;
                    return;
                case "unauthorized":
                    StatusCodeValue = StatusCodes.Status401Unauthorized;
                    Heading = "Sign-in required";
                    Summary = "You need to sign in before accessing this page.";
                    Detail = "Please sign in and try again.";
                    ThemeClass = "error-theme-primary";
                    IconText = "401";
                    ImagePath = "/images/error-authentication.png";
                    ImageAlt = "Authentication required";
                    PrimaryActionLabel = "Sign in";
                    PrimaryActionUrl = BuildLoginUrl(localReturnUrl, isPortalRequest);
                    SecondaryActionLabel = "Go home";
                    SecondaryActionUrl = HomeUrl;
                    return;
                case "forbidden":
                    StatusCodeValue = StatusCodes.Status403Forbidden;
                    Heading = "Access denied";
                    Summary = "You do not have permission to access this resource.";
                    Detail = "If you believe this is a mistake, contact an administrator.";
                    ThemeClass = "error-theme-violet";
                    IconText = "403";
                    ImagePath = "/images/error-authorization.png";
                    ImageAlt = "Access denied";
                    PrimaryActionLabel = "Go home";
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = "Try previous page";
                    SecondaryActionUrl = localReturnUrl;
                    return;
                case "not-found":
                    StatusCodeValue = StatusCodes.Status404NotFound;
                    Heading = "Page not found";
                    Summary = "The page or resource you requested could not be found.";
                    Detail = "The link may be outdated, or the item may have been moved or removed.";
                    ThemeClass = "error-theme-slate";
                    IconText = "404";
                    ImagePath = "/images/error-not-found.png";
                    ImageAlt = "Page not found";
                    PrimaryActionLabel = "Go home";
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = "Try previous page";
                    SecondaryActionUrl = localReturnUrl;
                    return;
                case "conflict":
                    StatusCodeValue = StatusCodes.Status409Conflict;
                    Heading = "Request conflict";
                    Summary = "The request could not be completed because the current state no longer matches.";
                    Detail = "Refresh the page and try again.";
                    ThemeClass = "error-theme-warning";
                    IconText = "409";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = "Request conflict";
                    PrimaryActionLabel = "Refresh and try again";
                    PrimaryActionUrl = localReturnUrl;
                    SecondaryActionLabel = "Go home";
                    SecondaryActionUrl = HomeUrl;
                    return;
            }

            StatusCodeValue = statusCode ?? StatusCodes.Status500InternalServerError;

            switch (StatusCodeValue)
            {
                case StatusCodes.Status400BadRequest:
                    ErrorCode = "bad-request";
                    Heading = "Invalid request";
                    Summary = "The request could not be processed in its current form.";
                    Detail = "Please check the input or try the action again from the previous page.";
                    ThemeClass = "error-theme-warning";
                    IconText = "400";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = "Invalid request";
                    break;
                case StatusCodes.Status401Unauthorized:
                    ErrorCode = "unauthorized";
                    Heading = "Sign-in required";
                    Summary = "You need to sign in before accessing this page.";
                    Detail = "Please sign in and try again.";
                    ThemeClass = "error-theme-primary";
                    IconText = "401";
                    ImagePath = "/images/error-authentication.png";
                    ImageAlt = "Authentication required";
                    PrimaryActionLabel = "Sign in";
                    PrimaryActionUrl = BuildLoginUrl(localReturnUrl, isPortalRequest);
                    break;
                case StatusCodes.Status403Forbidden:
                    ErrorCode = "forbidden";
                    Heading = "Access denied";
                    Summary = "You do not have permission to access this resource.";
                    Detail = "If you believe this is a mistake, contact an administrator.";
                    ThemeClass = "error-theme-violet";
                    IconText = "403";
                    ImagePath = "/images/error-authorization.png";
                    ImageAlt = "Access denied";
                    PrimaryActionLabel = "Go home";
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = "Try previous page";
                    SecondaryActionUrl = localReturnUrl;
                    break;
                case StatusCodes.Status404NotFound:
                    ErrorCode = "not-found";
                    Heading = "Page not found";
                    Summary = "The page or resource you requested could not be found.";
                    Detail = "The link may be outdated, or the item may have been moved or removed.";
                    ThemeClass = "error-theme-slate";
                    IconText = "404";
                    ImagePath = "/images/error-not-found.png";
                    ImageAlt = "Page not found";
                    PrimaryActionLabel = "Go home";
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = "Try previous page";
                    SecondaryActionUrl = localReturnUrl;
                    break;
                case StatusCodes.Status409Conflict:
                    ErrorCode = "conflict";
                    Heading = "Request conflict";
                    Summary = "The request could not be completed because the current state no longer matches.";
                    Detail = "Refresh the page and try again.";
                    ThemeClass = "error-theme-warning";
                    IconText = "409";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = "Request conflict";
                    PrimaryActionLabel = "Refresh and try again";
                    PrimaryActionUrl = localReturnUrl;
                    break;
                case StatusCodes.Status503ServiceUnavailable:
                    ErrorCode = "service-unavailable";
                    Heading = "Service temporarily unavailable";
                    Summary = "The service is currently unavailable.";
                    Detail = "Try again shortly.";
                    ThemeClass = "error-theme-info";
                    IconText = "503";
                    ImagePath = "/images/error-unavailable.png";
                    ImageAlt = "Service unavailable";
                    break;
                default:
                    ErrorCode = "server-error";
                    StatusCodeValue = StatusCodes.Status500InternalServerError;
                    Heading = "Something went wrong";
                    Summary = "An unexpected server error occurred.";
                    Detail = "Please try again in a moment. If the problem continues, contact support.";
                    ThemeClass = "error-theme-danger";
                    IconText = "500";
                    ImagePath = "/images/error-unavailable.png";
                    ImageAlt = "System or service issue";
                    break;
            }
        }

        private string BuildLoginUrl(string returnUrl, bool isPortalRequest)
        {
            if (!isPortalRequest)
            {
                return _applicationUrls.PortalUrl("/login");
            }

            return $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        }
    }
}
