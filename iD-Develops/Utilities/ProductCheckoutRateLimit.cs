using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using iD_Develops.Services;

namespace iD_Develops.Utilities;

public static class ProductCheckoutRateLimit
{
    public const string Policy = "product-checkout";

    public static RateLimitPartition<string> Partition(HttpContext context)
    {
        var urls = context.RequestServices.GetRequiredService<IApplicationUrlService>();
        if (urls.IsPortalRequest(context.Request) ||
            !(HttpMethods.IsPost(context.Request.Method) || bool.TryParse(context.Request.Query["resumeCheckout"], out var resume) && resume))
            return RateLimitPartition.GetNoLimiter("other-product-requests");
        // IP fallback also covers anonymous requests that rotate their cookies.
        var key = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
