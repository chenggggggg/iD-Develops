using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace iD_Develops.Utilities
{
    public sealed class DatabaseUnavailableExceptionFilter : IAsyncExceptionFilter
    {
        private readonly ILogger<DatabaseUnavailableExceptionFilter> _logger;

        public DatabaseUnavailableExceptionFilter(ILogger<DatabaseUnavailableExceptionFilter> logger)
        {
            _logger = logger;
        }

        public Task OnExceptionAsync(ExceptionContext context)
        {
            if (!DatabaseAvailability.IsDatabaseUnavailable(context.Exception))
            {
                return Task.CompletedTask;
            }

            var httpContext = context.HttpContext;
            var path = httpContext.Request.Path.Value ?? string.Empty;

            if (path.StartsWith("/health/db", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/stripe-webhook", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                context.ExceptionHandled = true;
                return Task.CompletedTask;
            }

            if (DatabaseAvailability.IsAjaxOrApiRequest(httpContext))
            {
                context.Result = new JsonResult(new { error = "database_unavailable" })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
                httpContext.Response.Headers["Retry-After"] = "60";
                context.ExceptionHandled = true;
                return Task.CompletedTask;
            }

            var errorCode = DatabaseAvailability.GetDatabaseErrorCode(context.Exception);
            var original = (httpContext.Request.PathBase + httpContext.Request.Path + httpContext.Request.QueryString).ToString();
            var redirectUrl = DatabaseAvailability.BuildErrorUrl(httpContext.Request.Path, httpContext.Request.QueryString, errorCode);

            _logger.LogWarning(context.Exception,
                "Database unavailable during MVC/Razor execution; redirecting to {RedirectUrl}. Original: {Original}",
                redirectUrl,
                original);

            context.Result = new RedirectResult(redirectUrl);
            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }
    }
}
