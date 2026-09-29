using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface ICatalogCheckoutService
    {
        Task<string> CreateCheckoutSessionAsync(
            HttpContext httpContext,
            CatalogProduct product,
            CatalogProductVariant variant,
            int quantity,
            IDictionary<string, string> formValues,
            string? customerEmail,
            int? inviteUseId,
            string? accessToken,
            int participantCount,
            string? authenticatedUserId,
            CancellationToken ct = default);
    }
}
