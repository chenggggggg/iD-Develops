using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

public sealed class CatalogCheckoutReceipt
{
    [Key, MaxLength(200)] public string StripeSessionId { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
}
