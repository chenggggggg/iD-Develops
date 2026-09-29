namespace iD_Develops.Services
{
    public interface IStripeService
    {
        Task<Models.CheckoutSessionDetails> GetCheckoutSessionDetailsAsync(string sessionId);
    }
}
