using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using iD_Develops.Services;
using Microsoft.Extensions.Localization;

namespace iD_Develops.Pages
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
        public string Heading { get; private set; } = string.Empty;
        public string Summary { get; private set; } = string.Empty;
        public string Detail { get; private set; } = string.Empty;
        public string PrimaryActionLabel { get; private set; } = string.Empty;
        public string PrimaryActionUrl { get; private set; } = "/";
        public string SecondaryActionLabel { get; private set; } = string.Empty;
        public string SecondaryActionUrl { get; private set; } = "/";
        public string? ImagePath { get; private set; }
        public string ImageAlt { get; private set; } = string.Empty;
        public string HomeUrl { get; private set; } = "/";
        public string? OriginalPath { get; private set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        private readonly ILogger<ErrorModel> _logger;
        private readonly IApplicationUrlService _applicationUrls;
        private readonly IStringLocalizer<ErrorModel> _localizer;

        public ErrorModel(
            ILogger<ErrorModel> logger,
            IApplicationUrlService applicationUrls,
            IStringLocalizer<ErrorModel> localizer)
        {
            _logger = logger;
            _applicationUrls = applicationUrls;
            _localizer = localizer;
        }

        public void OnGet(int? statusCode = null, string? code = null, string? returnUrl = null)
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            const bool isPortalRequest = false;
            var culture = (RouteData.Values.TryGetValue("culture", out var c) ? c?.ToString() : null) ?? "en-us";
            HomeUrl = isPortalRequest ? "/" : $"/{culture}/";
            var localReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : HomeUrl;
            PrimaryActionUrl = localReturnUrl;
            SecondaryActionUrl = HomeUrl;
            OriginalPath = localReturnUrl != HomeUrl ? localReturnUrl : null;
            Heading = _localizer["ServerErrorHeading"];
            Summary = _localizer["ServerErrorSummary"];
            Detail = _localizer["ServerErrorDetail"];
            PrimaryActionLabel = _localizer["TryAgainAction"];
            SecondaryActionLabel = _localizer["GoHomeAction"];
            ImageAlt = _localizer["ServerErrorImageAlt"];

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
                    Heading = _localizer["DatabaseHeading"];
                    Summary = ErrorCode switch
                    {
                        "db-timeout" => _localizer["DatabaseTimeoutSummary"],
                        "db-login-failed" => _localizer["DatabaseLoginSummary"],
                        "db-connection-refused" => _localizer["DatabaseRefusedSummary"],
                        _ => _localizer["DatabaseUnavailableSummary"]
                    };
                    Detail = ErrorCode switch
                    {
                        "db-timeout" => _localizer["DatabaseTimeoutDetail"],
                        "db-login-failed" => _localizer["DatabaseLoginDetail"],
                        "db-connection-refused" => _localizer["DatabaseRefusedDetail"],
                        _ => _localizer["DatabaseUnavailableDetail"]
                    };
                    ThemeClass = "error-theme-info";
                    IconText = "DB";
                    ImagePath = "/images/error-connectivity.png";
                    ImageAlt = _localizer["DatabaseImageAlt"];
                    PrimaryActionLabel = _localizer["TryAgainAction"];
                    PrimaryActionUrl = localReturnUrl;
                    SecondaryActionLabel = _localizer["GoHomeAction"];
                    SecondaryActionUrl = HomeUrl;
                    return;
                case "bad-request":
                    StatusCodeValue = StatusCodes.Status400BadRequest;
                    Heading = _localizer["BadRequestHeading"];
                    Summary = _localizer["BadRequestSummary"];
                    Detail = _localizer["BadRequestDetail"];
                    ThemeClass = "error-theme-warning";
                    IconText = "400";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = _localizer["BadRequestImageAlt"];
                    PrimaryActionLabel = _localizer["TryAgainAction"];
                    PrimaryActionUrl = localReturnUrl;
                    SecondaryActionLabel = _localizer["GoHomeAction"];
                    SecondaryActionUrl = HomeUrl;
                    return;
                case "unauthorized":
                    StatusCodeValue = StatusCodes.Status401Unauthorized;
                    Heading = _localizer["UnauthorizedHeading"];
                    Summary = _localizer["UnauthorizedSummary"];
                    Detail = _localizer["UnauthorizedDetail"];
                    ThemeClass = "error-theme-primary";
                    IconText = "401";
                    ImagePath = "/images/error-authentication.png";
                    ImageAlt = _localizer["UnauthorizedImageAlt"];
                    PrimaryActionLabel = _localizer["SignInAction"];
                    PrimaryActionUrl = BuildLoginUrl(localReturnUrl, isPortalRequest);
                    SecondaryActionLabel = _localizer["GoHomeAction"];
                    SecondaryActionUrl = HomeUrl;
                    return;
                case "forbidden":
                    StatusCodeValue = StatusCodes.Status403Forbidden;
                    Heading = _localizer["ForbiddenHeading"];
                    Summary = _localizer["ForbiddenSummary"];
                    Detail = _localizer["ForbiddenDetail"];
                    ThemeClass = "error-theme-violet";
                    IconText = "403";
                    ImagePath = "/images/error-authorization.png";
                    ImageAlt = _localizer["ForbiddenImageAlt"];
                    PrimaryActionLabel = _localizer["GoHomeAction"];
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = _localizer["PreviousPageAction"];
                    SecondaryActionUrl = localReturnUrl;
                    return;
                case "not-found":
                    StatusCodeValue = StatusCodes.Status404NotFound;
                    Heading = _localizer["NotFoundHeading"];
                    Summary = _localizer["NotFoundSummary"];
                    Detail = _localizer["NotFoundDetail"];
                    ThemeClass = "error-theme-slate";
                    IconText = "404";
                    ImagePath = "/images/error-not-found.png";
                    ImageAlt = _localizer["NotFoundImageAlt"];
                    PrimaryActionLabel = _localizer["GoHomeAction"];
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = _localizer["PreviousPageAction"];
                    SecondaryActionUrl = localReturnUrl;
                    return;
                case "conflict":
                    StatusCodeValue = StatusCodes.Status409Conflict;
                    Heading = _localizer["ConflictHeading"];
                    Summary = _localizer["ConflictSummary"];
                    Detail = _localizer["ConflictDetail"];
                    ThemeClass = "error-theme-warning";
                    IconText = "409";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = _localizer["ConflictImageAlt"];
                    PrimaryActionLabel = _localizer["RefreshAction"];
                    PrimaryActionUrl = localReturnUrl;
                    SecondaryActionLabel = _localizer["GoHomeAction"];
                    SecondaryActionUrl = HomeUrl;
                    return;
            }

            StatusCodeValue = statusCode ?? StatusCodes.Status500InternalServerError;

            switch (StatusCodeValue)
            {
                case StatusCodes.Status400BadRequest:
                    ErrorCode = "bad-request";
                    Heading = _localizer["BadRequestHeading"];
                    Summary = _localizer["BadRequestSummary"];
                    Detail = _localizer["BadRequestDetail"];
                    ThemeClass = "error-theme-warning";
                    IconText = "400";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = _localizer["BadRequestImageAlt"];
                    break;
                case StatusCodes.Status401Unauthorized:
                    ErrorCode = "unauthorized";
                    Heading = _localizer["UnauthorizedHeading"];
                    Summary = _localizer["UnauthorizedSummary"];
                    Detail = _localizer["UnauthorizedDetail"];
                    ThemeClass = "error-theme-primary";
                    IconText = "401";
                    ImagePath = "/images/error-authentication.png";
                    ImageAlt = _localizer["UnauthorizedImageAlt"];
                    PrimaryActionLabel = _localizer["SignInAction"];
                    PrimaryActionUrl = BuildLoginUrl(localReturnUrl, isPortalRequest);
                    break;
                case StatusCodes.Status403Forbidden:
                    ErrorCode = "forbidden";
                    Heading = _localizer["ForbiddenHeading"];
                    Summary = _localizer["ForbiddenSummary"];
                    Detail = _localizer["ForbiddenDetail"];
                    ThemeClass = "error-theme-violet";
                    IconText = "403";
                    ImagePath = "/images/error-authorization.png";
                    ImageAlt = _localizer["ForbiddenImageAlt"];
                    PrimaryActionLabel = _localizer["GoHomeAction"];
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = _localizer["PreviousPageAction"];
                    SecondaryActionUrl = localReturnUrl;
                    break;
                case StatusCodes.Status404NotFound:
                    ErrorCode = "not-found";
                    Heading = _localizer["NotFoundHeading"];
                    Summary = _localizer["NotFoundSummary"];
                    Detail = _localizer["NotFoundDetail"];
                    ThemeClass = "error-theme-slate";
                    IconText = "404";
                    ImagePath = "/images/error-not-found.png";
                    ImageAlt = _localizer["NotFoundImageAlt"];
                    PrimaryActionLabel = _localizer["GoHomeAction"];
                    PrimaryActionUrl = HomeUrl;
                    SecondaryActionLabel = _localizer["PreviousPageAction"];
                    SecondaryActionUrl = localReturnUrl;
                    break;
                case StatusCodes.Status409Conflict:
                    ErrorCode = "conflict";
                    Heading = _localizer["ConflictHeading"];
                    Summary = _localizer["ConflictSummary"];
                    Detail = _localizer["ConflictDetail"];
                    ThemeClass = "error-theme-warning";
                    IconText = "409";
                    ImagePath = "/images/error-invalid-request.png";
                    ImageAlt = _localizer["ConflictImageAlt"];
                    PrimaryActionLabel = _localizer["RefreshAction"];
                    PrimaryActionUrl = localReturnUrl;
                    break;
                case StatusCodes.Status503ServiceUnavailable:
                    ErrorCode = "service-unavailable";
                    Heading = _localizer["ServiceUnavailableHeading"];
                    Summary = _localizer["ServiceUnavailableSummary"];
                    Detail = _localizer["ServiceUnavailableDetail"];
                    ThemeClass = "error-theme-info";
                    IconText = "503";
                    ImagePath = "/images/error-unavailable.png";
                    ImageAlt = _localizer["ServiceUnavailableImageAlt"];
                    break;
                default:
                    ErrorCode = "server-error";
                    StatusCodeValue = StatusCodes.Status500InternalServerError;
                    Heading = _localizer["ServerErrorHeading"];
                    Summary = _localizer["ServerErrorSummary"];
                    Detail = _localizer["ServerErrorDetail"];
                    ThemeClass = "error-theme-danger";
                    IconText = "500";
                    ImagePath = "/images/error-unavailable.png";
                    ImageAlt = _localizer["ServerErrorImageAlt"];
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
