using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("UserCreditTransactions")]
    public class UserCreditTransaction
    {
        public long Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public int CreditTypeId { get; set; }

        public CreditType CreditType { get; set; } = null!;

        public int UserCreditLotId { get; set; }

        public UserCreditLot UserCreditLot { get; set; } = null!;

        public int? EventBookingId { get; set; }

        public EventBooking? EventBooking { get; set; }

        public CreditTransactionType TransactionType { get; set; }

        public int QuantityDelta { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
