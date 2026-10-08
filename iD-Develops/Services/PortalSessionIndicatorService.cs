using System.Net;
using iD_Develops.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services
{
    public interface IPortalSessionIndicatorService
    {
        bool HasActiveSession(HttpRequest request);
        void MarkSignedIn(HttpContext context, bool isPersistent);
        void Clear(HttpContext context);
    }

    /// <summary>
    /// Maintains a protected, non-authorizing cookie that lets the public site
    /// display a Portal link while the real authentication cookie remains
    /// restricted to the portal host.
    /// </summary>
    public sealed class PortalSessionIndicatorService : IPortalSessionIndicatorService
    {
        public const string CookieName = ".iDDevelops.PortalSession";
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

        private const string ActiveValue = "active";
        private readonly ITimeLimitedDataProtector _protector;
        private readonly string? _sharedCookieDomain;

        public PortalSessionIndicatorService(
            IDataProtectionProvider dataProtectionProvider,
            IOptions<ApplicationUrlOptions> applicationUrlOptions)
        {
            ArgumentNullException.ThrowIfNull(dataProtectionProvider);
            ArgumentNullException.ThrowIfNull(applicationUrlOptions);

            _protector = dataProtectionProvider
                .CreateProtector("PortalSessionIndicator:v1")
                .ToTimeLimitedDataProtector();
            _sharedCookieDomain = ResolveSharedCookieDomain(applicationUrlOptions.Value);
        }

        public bool HasActiveSession(HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!request.Cookies.TryGetValue(CookieName, out var protectedValue) ||
                string.IsNullOrWhiteSpace(protectedValue))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    _protector.Unprotect(protectedValue),
                    ActiveValue,
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public void MarkSignedIn(HttpContext context, bool isPersistent)
        {
            ArgumentNullException.ThrowIfNull(context);

            var options = CreateCookieOptions(context);
            if (isPersistent)
            {
                options.Expires = DateTimeOffset.UtcNow.Add(Lifetime);
            }

            context.Response.Cookies.Append(
                CookieName,
                _protector.Protect(ActiveValue, Lifetime),
                options);
        }

        public void Clear(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Response.Cookies.Delete(CookieName, CreateCookieOptions(context));
        }

        private CookieOptions CreateCookieOptions(HttpContext context) => new()
        {
            Domain = _sharedCookieDomain,
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps
        };

        private static string? ResolveSharedCookieDomain(ApplicationUrlOptions options)
        {
            if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicUri) ||
                !Uri.TryCreate(options.PortalBaseUrl, UriKind.Absolute, out var portalUri))
            {
                return null;
            }

            var publicHost = publicUri.Host.Trim('.');
            var portalHost = portalUri.Host.Trim('.');
            if (string.Equals(publicHost, portalHost, StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(publicHost, out _) ||
                IPAddress.TryParse(portalHost, out _))
            {
                return null;
            }

            if (portalHost.EndsWith('.' + publicHost, StringComparison.OrdinalIgnoreCase))
            {
                return "." + publicHost;
            }

            if (publicHost.EndsWith('.' + portalHost, StringComparison.OrdinalIgnoreCase))
            {
                return "." + portalHost;
            }

            return null;
        }
    }
}
