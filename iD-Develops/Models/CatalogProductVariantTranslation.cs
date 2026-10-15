using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models;

[Table("CatalogProductVariantTranslations")]
public sealed class CatalogProductVariantTranslation
{
    public int CatalogProductVariantId { get; set; }

    public CatalogProductVariant CatalogProductVariant { get; set; } = null!;

    [MaxLength(10)]
    public string Culture { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Name { get; set; }
}
