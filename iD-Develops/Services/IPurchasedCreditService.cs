namespace iD_Develops.Services
{
    public interface IPurchasedCreditService
    {
        Task GrantPurchasedCreditsAsync(
            int catalogProductId,
            string? authenticatedUserId,
            string? customerEmail,
            string externalReference,
            int orderQuantity,
            CancellationToken cancellationToken = default);
    }
}
