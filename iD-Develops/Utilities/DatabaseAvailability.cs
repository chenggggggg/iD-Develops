using System.Data.Common;
using Microsoft.AspNetCore.Http;

namespace iD_Develops.Utilities
{
    public static class DatabaseAvailability
    {
        public static string GetDatabaseErrorCode(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is DbException dbEx)
                {
                    var message = dbEx.Message ?? string.Empty;
                    if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        return "db-timeout";
                    }

                    if (message.Contains("login failed", StringComparison.OrdinalIgnoreCase))
                    {
                        return "db-login-failed";
                    }
                }
            }

            return "db-offline";
        }

        public static bool IsDatabaseUnavailable(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is DbException dbEx && IsConnectivityDbException(dbEx))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAjaxOrApiRequest(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var accept = context.Request.Headers.Accept.ToString();
            if (!string.IsNullOrWhiteSpace(accept) &&
                accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var contentType = context.Request.ContentType ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(contentType) &&
                contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (context.Request.Headers.TryGetValue("X-Requested-With", out var xrw) &&
                xrw.ToString().Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static string GetCultureFromPath(PathString path)
        {
            var value = path.Value ?? string.Empty;
            if (value.StartsWith('/'))
            {
                value = value[1..];
            }

            var firstSegment = value.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            return firstSegment?.ToLowerInvariant() switch
            {
                "en-us" => "en-us",
                "nl-nl" => "nl-nl",
                _ => "en-us"
            };
        }

        public static string BuildErrorUrl(PathString path, QueryString queryString, string code)
        {
            var culture = GetCultureFromPath(path);
            var original = path + queryString;
            return $"/{culture}/error?code={Uri.EscapeDataString(code)}&returnUrl={Uri.EscapeDataString(original)}";
        }

        private static bool IsConnectivityDbException(DbException ex)
        {
            var message = ex.Message ?? string.Empty;

            return message.Contains("network-related", StringComparison.OrdinalIgnoreCase)
                || message.Contains("server was not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection was not closed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("transport-level error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timeout expired", StringComparison.OrdinalIgnoreCase)
                || message.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("failed to connect", StringComparison.OrdinalIgnoreCase)
                || message.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("database", StringComparison.OrdinalIgnoreCase) && message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }
    }
}
