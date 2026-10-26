using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [NotMapped]
    public class CheckoutSessionDetails
    {
        public bool IsSessionValid { get; set; }
        public string SessionStatus { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public decimal AmountTotal { get; set; }
        public bool IsCompleted => IsSessionValid &&
            string.Equals(SessionStatus, "complete", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase) ||
             AmountTotal == 0 && string.Equals(PaymentStatus, "no_payment_required", StringComparison.OrdinalIgnoreCase));
        public string DateOfPurchase { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
    }
}
