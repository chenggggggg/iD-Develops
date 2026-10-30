using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

public sealed class CatalogCheckoutAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(64)] public string RequestKey { get; set; } = string.Empty;
    public int CatalogProductId { get; set; }
    public CatalogProduct CatalogProduct { get; set; } = null!;
    public int VariantId { get; set; }
    [MaxLength(450)] public string? AuthenticatedUserId { get; set; }
    [MaxLength(254)] public string? CustomerEmail { get; set; }
    public string FormDataJson { get; set; } = "{}";
    public int Quantity { get; set; }
    public int ParticipantCount { get; set; }
    public int? InviteUseId { get; set; }
    public long AmountTotalMinorUnits { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "eur";
    [MaxLength(200)] public string? StripeSessionId { get; set; }
    [MaxLength(2000)] public string? CheckoutUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);
    public DateTime? CompletedAtUtc { get; set; }
}
