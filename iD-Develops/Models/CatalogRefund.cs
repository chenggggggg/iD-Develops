using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

public sealed class CatalogRefund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CatalogPurchaseId { get; set; }
    public CatalogPurchase CatalogPurchase { get; set; } = null!;
    [MaxLength(200)] public string? ProviderRefundId { get; set; }
    [MaxLength(40)] public string Status { get; set; } = "Submitting";
    public long AmountMinorUnits { get; set; }
    public bool IsExternal { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextCheckAtUtc { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
    [MaxLength(100)] public string? FailureCode { get; set; }
}
