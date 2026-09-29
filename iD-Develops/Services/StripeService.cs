using iD_Develops.Models;
using Stripe;
using Stripe.Checkout;

namespace iD_Develops.Services
{
    public class StripeService : IStripeService
    {
        public async Task<CheckoutSessionDetails> GetCheckoutSessionDetailsAsync(string sessionId)
        {
            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(sessionId, new SessionGetOptions
            {
                Expand = new List<string> { "payment_intent" }
            });

            if (session == null)
            {
                return null!;
            }

            var lineItemService = new SessionLineItemService();
            var lineItems = await lineItemService.ListAsync(sessionId);
            var firstItem = lineItems.Data.Count > 0 ? lineItems.Data[0] : null;

            var paymentMethodDetails = string.Empty;
            if (!string.IsNullOrWhiteSpace(session.PaymentIntentId))
            {
                var paymentIntentService = new PaymentIntentService();
                var paymentIntent = await paymentIntentService.GetAsync(session.PaymentIntentId);

                if (paymentIntent?.PaymentMethodId != null)
                {
                    var paymentMethodService = new PaymentMethodService();
                    var paymentMethod = await paymentMethodService.GetAsync(paymentIntent.PaymentMethodId);
                    if (paymentMethod.Card != null)
                    {
                        paymentMethodDetails = $"{paymentMethod.Card.Brand.ToUpper()} **** {paymentMethod.Card.Last4}";
                    }
                }
            }

            return new CheckoutSessionDetails
            {
                IsSessionValid = true,
                SessionStatus = session.Status,
                PaymentStatus = session.PaymentStatus,
                ProductName = firstItem?.Description ?? "Unknown Product",
                Quantity = firstItem?.Quantity ?? 1,
                AmountTotal = (firstItem?.AmountTotal ?? 0) / 100m,
                DateOfPurchase = ((DateTimeOffset)session.Created).ToString("dd-MM-yyyy"),
                PaymentMethod = paymentMethodDetails
            };
        }
    }
}
