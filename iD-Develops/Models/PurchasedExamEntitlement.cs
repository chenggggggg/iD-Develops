using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

public sealed class PurchasedExamEntitlement
{
    public int Id { get; set; }
    [MaxLength(200)] public string StripeSessionId { get; set; } = string.Empty;
    public Guid CheckoutAttemptId { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public int? CatalogProductId { get; set; }
    public CatalogProduct? CatalogProduct { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public int ExamVersionId { get; set; }
    public ExamVersion ExamVersion { get; set; } = null!;
    public int TotalAttempts { get; set; }
    public int UsedAttempts { get; set; }
    public bool RefundBlocked { get; set; }
    public DateTime PurchasedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Record> Records { get; set; } = new List<Record>();
}
