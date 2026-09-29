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
        public string DateOfPurchase { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
    }
}
