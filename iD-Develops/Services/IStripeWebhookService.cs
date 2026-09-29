namespace iD_Develops.Services
{
    public interface IStripeWebhookService
    {
        Task<IResult> CheckoutCompletedAsync(HttpRequest request);
    }
}
