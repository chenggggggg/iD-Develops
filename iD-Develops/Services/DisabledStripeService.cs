namespace iD_Develops.Services
{
    public class DisabledStripeService : IStripeService
    {
        public Task<Models.CheckoutSessionDetails> GetCheckoutSessionDetailsAsync(string sessionId)
            => Task.FromResult<Models.CheckoutSessionDetails>(null!);
    }
}
