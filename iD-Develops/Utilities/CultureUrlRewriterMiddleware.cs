using System.Globalization;
using iD_Develops.Services;
using Microsoft.AspNetCore.Http;

namespace iD_Develops.Utilities
{
    public sealed class CultureUrlRewriterMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IApplicationUrlService _applicationUrlService;

        // Internal canonical culture names (what .NET expects)
        private static readonly string[] SupportedCultures = { "en-US", "nl-NL" };

        // URL canonical form (what you want in the browser address bar)
        // -> lowercase
        private static readonly string[] SupportedCulturesUrl =
            SupportedCultures.Select(c => c.ToLowerInvariant()).ToArray();

        public CultureUrlRewriterMiddleware(RequestDelegate next, IApplicationUrlService applicationUrlService)
        {
            _next = next;
            _applicationUrlService = applicationUrlService;
        }

        public async Task Invoke(HttpContext context)
        {
            if (_applicationUrlService.IsPortalRequest(context.Request))
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? "/";
            var queryString = context.Request.QueryString.Value ?? string.Empty;

            // Skip obvious non-page requests (optional but helpful)
            // If you want, you can expand this list.
            if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_vs", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)) // <-- ADDED
            {
                await _next(context);
                return;
            }

            // Extract first URL segment
            // "/en-us/exams/manage/create" -> "en-us"
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var firstSegment = segments.Length > 0 ? segments[0] : null;

            // Determine default culture from cookie (fallback to en-US),
            // but URL should be lowercase.
            var cookieCulture = context.Request.Cookies["ASPNET_LANG"];
            var defaultCultureInternal = NormalizeToSupportedCulture(cookieCulture) ?? "en-US";
            var defaultCultureUrl = defaultCultureInternal.ToLowerInvariant();

            // Decide whether first segment is a supported culture (case-insensitive)
            var matchedInternalCulture = NormalizeToSupportedCulture(firstSegment);
            var hasValidCultureSegment = matchedInternalCulture != null;

            var isSafeMethod =
                HttpMethods.IsGet(context.Request.Method) ||
                HttpMethods.IsHead(context.Request.Method);

            // 1) No valid culture segment in URL -> add it (redirect for GET/HEAD; rewrite otherwise)
            if (!hasValidCultureSegment)
            {
                var targetPath = "/" + defaultCultureUrl + path; // keep original path intact
                if (isSafeMethod)
                {
                    context.Response.Redirect(targetPath + queryString, permanent: false);
                    return;
                }

                // Non-GET: rewrite in-place, do NOT redirect
                context.Request.Path = new PathString(targetPath);
                await _next(context);
                return;
            }

            // 2) Culture exists but not in lowercase canonical form -> normalize to lowercase
            var incomingCultureUrl = firstSegment!.ToLowerInvariant();
            var expectedCultureUrl = matchedInternalCulture!.ToLowerInvariant();

            if (!string.Equals(incomingCultureUrl, expectedCultureUrl, StringComparison.Ordinal))
            {
                // Replace only the first segment
                var remainder = path.Substring(firstSegment.Length + 1); // remove "/{firstSegment}"
                var targetPath = "/" + expectedCultureUrl + remainder;

                if (isSafeMethod)
                {
                    context.Response.Redirect(targetPath + queryString, permanent: false);
                    return;
                }

                // Non-GET: rewrite in-place, do NOT redirect
                context.Request.Path = new PathString(targetPath);
                await _next(context);
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// Normalizes an incoming culture value to one of the supported internal culture names (en-US / nl-NL),
        /// case-insensitively. Returns null if no match.
        /// </summary>
        private static string? NormalizeToSupportedCulture(string? culture)
        {
            if (string.IsNullOrWhiteSpace(culture))
                return null;

            // Handle both "en-us" and "en-US" etc.
            // Compare against SupportedCultures case-insensitively.
            foreach (var supported in SupportedCultures)
            {
                if (string.Equals(culture, supported, StringComparison.OrdinalIgnoreCase))
                    return supported;
            }

            // If someone passed "en" or "nl" etc, optionally map those here.
            // For now: no partial matches.
            return null;
        }
    }
}
