using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace iD_Develops.Services
{
    public interface IPortalAuthenticationHandoffService
    {
        string CreateToken(string userId, string returnPath);
        PortalAuthenticationHandoff? ValidateToken(string? token);
    }

    public sealed record PortalAuthenticationHandoff(string UserId, string ReturnPath);

    public sealed class PortalAuthenticationHandoffService : IPortalAuthenticationHandoffService
    {
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
        private readonly ITimeLimitedDataProtector _protector;

        public PortalAuthenticationHandoffService(IDataProtectionProvider dataProtectionProvider)
        {
            _protector = dataProtectionProvider
                .CreateProtector("PortalAuthenticationHandoff:v1")
                .ToTimeLimitedDataProtector();
        }

        public string CreateToken(string userId, string returnPath)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("A user ID is required.", nameof(userId));
            }

            if (!IsSafePublicReturnPath(returnPath))
            {
                throw new ArgumentException("The public return path is invalid.", nameof(returnPath));
            }

            var payload = JsonSerializer.Serialize(new PortalAuthenticationHandoff(userId, returnPath));
            return _protector.Protect(payload, TokenLifetime);
        }

        public PortalAuthenticationHandoff? ValidateToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            try
            {
                var payload = JsonSerializer.Deserialize<PortalAuthenticationHandoff>(_protector.Unprotect(token));
                return payload != null &&
                       !string.IsNullOrWhiteSpace(payload.UserId) &&
                       IsSafePublicReturnPath(payload.ReturnPath)
                    ? payload
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsSafePublicReturnPath(string? returnPath)
        {
            if (string.IsNullOrWhiteSpace(returnPath) ||
                !returnPath.StartsWith('/') ||
                returnPath.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            return returnPath.StartsWith("/en-us/products/", StringComparison.OrdinalIgnoreCase) ||
                   returnPath.StartsWith("/nl-nl/products/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
