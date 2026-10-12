using iD_Develops.Services;
using Microsoft.AspNetCore.Routing;

namespace iD_Develops.Utilities
{
    /// <summary>
    /// Converts unhandled browser errors into the appropriate public or portal
    /// error page without redirecting away from the failed URL.
    /// </summary>
    public sealed class ApplicationErrorMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApplicationErrorMiddleware> _logger;

        public ApplicationErrorMiddleware(
            RequestDelegate next,
            ILogger<ApplicationErrorMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IApplicationUrlService applicationUrls)
        {
            if (IsErrorPageRequest(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var originalPath = context.Request.Path;
            var originalQuery = context.Request.QueryString;

            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                var databaseUnavailable = DatabaseAvailability.IsDatabaseUnavailable(exception);
                var statusCode = databaseUnavailable
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status500InternalServerError;
                var errorCode = databaseUnavailable
                    ? DatabaseAvailability.GetDatabaseErrorCode(exception)
                    : "server-error";

                LogException(context, exception, statusCode, errorCode, databaseUnavailable);

                if (IsDirectResponseRequest(context))
                {
                    await WriteDirectErrorAsync(context, statusCode, errorCode, databaseUnavailable);
                    return;
                }

                await ReExecuteErrorPageAsync(
                    context,
                    applicationUrls,
                    originalPath,
                    originalQuery,
                    statusCode,
                    errorCode);
                return;
            }

            if (!ShouldRenderStatusPage(context))
            {
                return;
            }

            var responseStatusCode = context.Response.StatusCode;
            await ReExecuteErrorPageAsync(
                context,
                applicationUrls,
                originalPath,
                originalQuery,
                responseStatusCode,
                GetErrorCode(responseStatusCode));
        }

        private static bool ShouldRenderStatusPage(HttpContext context)
        {
            var statusCode = context.Response.StatusCode;
            return statusCode is >= 400 and <= 599
                && !context.Response.HasStarted
                && context.Response.ContentLength == null
                && string.IsNullOrEmpty(context.Response.ContentType)
                && !IsDirectResponseRequest(context)
                && !IsErrorPageRequest(context.Request.Path);
        }

        private static bool IsDirectResponseRequest(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            return DatabaseAvailability.IsAjaxOrApiRequest(context)
                || path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/stripe-webhook", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/zoom/webhook", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsErrorPageRequest(PathString path)
        {
            var value = path.Value ?? string.Empty;
            return value.Equals("/error", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/en-us/error", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/nl-nl/error", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetErrorCode(int statusCode) => statusCode switch
        {
            StatusCodes.Status400BadRequest => "bad-request",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "not-found",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status503ServiceUnavailable => "service-unavailable",
            _ => "server-error"
        };

        private async Task ReExecuteErrorPageAsync(
            HttpContext context,
            IApplicationUrlService applicationUrls,
            PathString originalPath,
            QueryString originalQuery,
            int statusCode,
            string errorCode)
        {
            var originalTarget = (context.Request.PathBase + originalPath + originalQuery).ToString();
            var errorPath = applicationUrls.IsPortalRequest(context.Request)
                ? new PathString("/error")
                : new PathString($"/{DatabaseAvailability.GetCultureFromPath(originalPath)}/error");
            var errorQuery = QueryString.Create(
            [
                new KeyValuePair<string, string?>("code", errorCode),
                new KeyValuePair<string, string?>("statusCode", statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string?>("returnUrl", originalTarget)
            ]);

            var originalEndpoint = context.GetEndpoint();
            var originalRouteValues = context.Request.RouteValues;
            var originalMethod = context.Request.Method;

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            if (statusCode == StatusCodes.Status503ServiceUnavailable)
            {
                context.Response.Headers.RetryAfter = "60";
            }

            context.SetEndpoint(null);
            context.Request.RouteValues = new RouteValueDictionary();
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = errorPath;
            context.Request.QueryString = errorQuery;

            try
            {
                await _next(context);
            }
            catch (Exception exception) when (!context.Response.HasStarted)
            {
                _logger.LogError(
                    exception,
                    "The error page failed while rendering HTTP {StatusCode} for {OriginalTarget}.",
                    statusCode,
                    originalTarget);
                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "text/plain; charset=utf-8";
                if (statusCode == StatusCodes.Status503ServiceUnavailable)
                {
                    context.Response.Headers.RetryAfter = "60";
                }

                await context.Response.WriteAsync("The request could not be completed. Please try again later.");
            }
            finally
            {
                context.Request.Path = originalPath;
                context.Request.QueryString = originalQuery;
                context.Request.RouteValues = originalRouteValues;
                context.Request.Method = originalMethod;
                context.SetEndpoint(originalEndpoint);
            }
        }

        private static async Task WriteDirectErrorAsync(
            HttpContext context,
            int statusCode,
            string errorCode,
            bool databaseUnavailable)
        {
            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            if (databaseUnavailable)
            {
                context.Response.Headers.RetryAfter = "60";
            }

            if (DatabaseAvailability.IsAjaxOrApiRequest(context))
            {
                await context.Response.WriteAsJsonAsync(new { error = errorCode });
            }
        }

        private void LogException(
            HttpContext context,
            Exception exception,
            int statusCode,
            string errorCode,
            bool databaseUnavailable)
        {
            var requestTarget = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            if (databaseUnavailable)
            {
                _logger.LogWarning(
                    exception,
                    "Database unavailable while processing {Method} {RequestTarget}. Returning HTTP {StatusCode} ({ErrorCode}).",
                    context.Request.Method,
                    requestTarget,
                    statusCode,
                    errorCode);
                return;
            }

            _logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {RequestTarget}. Returning HTTP {StatusCode} ({ErrorCode}).",
                context.Request.Method,
                requestTarget,
                statusCode,
                errorCode);
        }
    }
}
