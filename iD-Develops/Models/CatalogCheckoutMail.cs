using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

public sealed class CatalogCheckoutMail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(200)] public string StripeSessionId { get; set; } = string.Empty;
    [MaxLength(254)] public string Recipient { get; set; } = string.Empty;
    [MaxLength(500)] public string Subject { get; set; } = string.Empty;
    public string ModelJson { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}
