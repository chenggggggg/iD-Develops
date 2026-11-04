using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

// Financial history outlives temporary checkout attempts and product edits/deletion.
public sealed class CatalogPurchase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(200)] public string StripeSessionId { get; set; } = string.Empty;
    [MaxLength(200)] public string? PaymentIntentId { get; set; }
    public Guid? CheckoutAttemptId { get; set; }
    [MaxLength(450)] public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public int? CatalogProductId { get; set; }
    public CatalogProduct? CatalogProduct { get; set; }
    [MaxLength(200)] public string ProductName { get; set; } = string.Empty;
    [MaxLength(3)] public string Currency { get; set; } = "eur";
    public long AmountMinorUnits { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IsLocal { get; set; }
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FulfilledAtUtc { get; set; }
    public bool HasUntrackedBenefits { get; set; } = true;
    public int ExamEntitlementCount { get; set; }
    public int CreditLotCount { get; set; }
    public int? GrantedCourseId { get; set; }
    public DateTime? OwnedCourseAccessGrantedAtUtc { get; set; }
    public bool RefundBlocked { get; set; }
    public long RefundedMinorUnits { get; set; }
    [MaxLength(40)] public string RefundStatus { get; set; } = "None";
    public ICollection<CatalogRefund> Refunds { get; set; } = new List<CatalogRefund>();
}
