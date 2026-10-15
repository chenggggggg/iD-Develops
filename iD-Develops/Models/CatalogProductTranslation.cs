using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models;

[Table("CatalogProductTranslations")]
public sealed class CatalogProductTranslation
{
    public int CatalogProductId { get; set; }

    public CatalogProduct CatalogProduct { get; set; } = null!;

    [MaxLength(10)]
    public string Culture { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(4000)]
    public string? Summary { get; set; }

    public string? Description { get; set; }

    public string? FullDescriptionHtml { get; set; }

    [MaxLength(100)]
    public string? ExternalBookingButtonText { get; set; }

    [MaxLength(200)]
    public string? ConfirmationEmailSubject { get; set; }

    public string? ConfirmationEmailBodyHtml { get; set; }

    [MaxLength(200)]
    public string? OwnerNotificationSubject { get; set; }

    public string? OwnerNotificationBodyHtml { get; set; }
}
