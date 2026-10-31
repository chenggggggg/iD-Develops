using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages
{
    public class PaymentProcessingModel : PageModel
    {
        private readonly IStripeService _stripeService;
        private readonly CatalogCheckoutAuditService _audit;

        public PaymentProcessingModel(IStripeService stripeService, CatalogCheckoutAuditService audit)
        {
            _stripeService = stripeService;
            _audit = audit;
        }

        public async Task<IActionResult> OnGetAsync([FromQuery(Name = "session_id")] string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return RedirectToPage("/PaymentResult", new { status = "cancelled" });
            }

            await _audit.BrowserReturnedAsync(sessionId, HttpContext.RequestAborted);
            var details = await _stripeService.GetCheckoutSessionDetailsAsync(sessionId);
            if (details == null || !details.IsSessionValid)
            {
                return RedirectToPage("/PaymentResult", new { status = "unknown" });
            }

            if (details.IsCompleted)
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
