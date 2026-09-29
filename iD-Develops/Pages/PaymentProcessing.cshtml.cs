using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages
{
    public class PaymentProcessingModel : PageModel
    {
        private readonly IStripeService _stripeService;

        public PaymentProcessingModel(IStripeService stripeService)
        {
            _stripeService = stripeService;
        }

        public async Task<IActionResult> OnGetAsync([FromQuery(Name = "session_id")] string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return RedirectToPage("/PaymentResult", new { status = "cancelled" });
            }

            var details = await _stripeService.GetCheckoutSessionDetailsAsync(sessionId);
            if (details == null || !details.IsSessionValid)
            {
                return RedirectToPage("/PaymentResult", new { status = "unknown" });
            }

            if (string.Equals(details.SessionStatus, "complete", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(details.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToPage("/PaymentResult", new { session_id = sessionId });
            }

            return RedirectToPage("/PaymentResult", new
            {
                session_id = sessionId,
                status = string.Equals(details.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase)
                    ? "failed"
                    : details.SessionStatus
            });
        }
    }
}
