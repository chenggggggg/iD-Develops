using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("EventBookingCreditAllocations")]
    public class EventBookingCreditAllocation
    {
        public int Id { get; set; }

        public int EventBookingId { get; set; }

        public EventBooking EventBooking { get; set; } = null!;

        public int UserCreditLotId { get; set; }

        public UserCreditLot UserCreditLot { get; set; } = null!;

        public int Amount { get; set; }

        public bool IsReturned { get; set; }

        public bool IsConsumed { get; set; }
    }
}
