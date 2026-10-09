using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace iD_Develops.Pages
{
    public class PaymentResultModel : PageModel
    {
        private readonly IStripeService _stripeService;
        private readonly IStringLocalizer<PaymentResultModel> _localizer;

        public PaymentResultModel(
            IStripeService stripeService,
            IStringLocalizer<PaymentResultModel> localizer)
        {
            _stripeService = stripeService;
            _localizer = localizer;
        }

        public CheckoutSessionDetails SessionDetails { get; private set; } = new();
        public bool IsLocalSuccess { get; private set; }
        public string ResultStatus { get; private set; } = "unknown";
        public string ResultTitle { get; private set; } = string.Empty;
        public string ResultMessage { get; private set; } = string.Empty;
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
                    ProductName = string.IsNullOrWhiteSpace(product) ? _localizer["LocalCheckout"] : product,
                    Quantity = 1,
                    DateOfPurchase = DateTime.Now.ToString("dd-MM-yyyy"),
                    PaymentMethod = _localizer["LocalBypass"]
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

            var presentation = status switch
            {
                "success" => (TitleKey: "SuccessTitle", MessageKey: "SuccessMessage", Icon: "fa-circle-check", Tone: "success"),
                "processing" or "open" => (TitleKey: "ProcessingTitle", MessageKey: "ProcessingMessage", Icon: "fa-clock", Tone: "warning"),
                "failed" => (TitleKey: "FailedTitle", MessageKey: "FailedMessage", Icon: "fa-circle-xmark", Tone: "danger"),
                "cancelled" or "canceled" => (TitleKey: "CancelledTitle", MessageKey: "CancelledMessage", Icon: "fa-ban", Tone: "secondary"),
                "expired" => (TitleKey: "ExpiredTitle", MessageKey: "ExpiredMessage", Icon: "fa-hourglass-end", Tone: "warning"),
                _ => (TitleKey: "UnknownTitle", MessageKey: "UnknownMessage", Icon: "fa-circle-question", Tone: "secondary")
            };

            ResultTitle = _localizer[presentation.TitleKey];
            ResultMessage = _localizer[presentation.MessageKey];
            ResultIcon = presentation.Icon;
            ResultTone = presentation.Tone;
        }
    }
}
