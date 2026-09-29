using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages
{
    public class PaymentResultModel : PageModel
    {
        private readonly IStripeService _stripeService;

        public PaymentResultModel(IStripeService stripeService)
        {
            _stripeService = stripeService;
        }

        public CheckoutSessionDetails SessionDetails { get; private set; } = new();
        public bool IsLocalSuccess { get; private set; }
        public string ResultStatus { get; private set; } = "unknown";
        public string ResultTitle { get; private set; } = "Payment Status";
        public string ResultMessage { get; private set; } = "We could not determine the payment status.";
        public string ResultIcon { get; private set; } = "fa-circle-question";
        public string ResultTone { get; private set; } = "secondary";

        public async Task OnGetAsync(
            [FromQuery(Name = "session_id")] string? sessionId,
            [FromQuery] bool local = false,
            [FromQuery] string? product = null,
            [FromQuery] string? status = null)
        {
            if (local)
            {
                IsLocalSuccess = true;
                SessionDetails = new CheckoutSessionDetails
                {
                    IsSessionValid = true,
                    SessionStatus = "complete",
                    PaymentStatus = "local",
                    ProductName = string.IsNullOrWhiteSpace(product) ? "Local checkout" : product,
                    Quantity = 1,
                    DateOfPurchase = DateTime.Now.ToString("dd-MM-yyyy"),
                    PaymentMethod = "Local bypass"
                };
                SetResult("success");
                return;
            }

            ResultStatus = string.IsNullOrWhiteSpace(status) ? "unknown" : status.Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var details = await _stripeService.GetCheckoutSessionDetailsAsync(sessionId);
                if (details?.IsSessionValid == true)
                {
                    SessionDetails = details;
                    ResultStatus = ResolveStatus(details, ResultStatus);
                    SetResult(ResultStatus);
                    return;
                }
            }

            SetResult(ResultStatus);
        }

        private static string ResolveStatus(CheckoutSessionDetails details, string fallback)
        {
            if (string.Equals(details.SessionStatus, "complete", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(details.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                return "success";
            }

            if (string.Equals(details.SessionStatus, "expired", StringComparison.OrdinalIgnoreCase))
            {
                return "expired";
            }

            if (string.Equals(details.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase))
            {
                return "failed";
            }

            if (string.Equals(details.SessionStatus, "open", StringComparison.OrdinalIgnoreCase))
            {
                return "processing";
            }

            return string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback;
        }

        private void SetResult(string status)
        {
            ResultStatus = status;

            (ResultTitle, ResultMessage, ResultIcon, ResultTone) = status switch
            {
                "success" => ("Payment Success", "Thank you. Your payment has been completed.", "fa-circle-check", "success"),
                "processing" or "open" => ("Payment Processing", "Your payment is not complete yet. If this takes too long, please restart checkout.", "fa-clock", "warning"),
                "failed" => ("Payment Failed", "The payment was not completed. Please try again or use another payment method.", "fa-circle-xmark", "danger"),
                "cancelled" or "canceled" => ("Payment Cancelled", "Checkout was cancelled before payment was completed.", "fa-ban", "secondary"),
                "expired" => ("Payment Expired", "The checkout session expired. Please restart checkout if you still want to purchase this product.", "fa-hourglass-end", "warning"),
                _ => ("Payment Status Unknown", "We could not determine the payment status. Please check your email receipt or contact support.", "fa-circle-question", "secondary")
            };
        }
    }
}
