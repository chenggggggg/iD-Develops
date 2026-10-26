using System.ComponentModel.DataAnnotations;

namespace iD_Develops.Models;

public sealed class CatalogProductSlug
{
    [Key, MaxLength(200)]
    public string Slug { get; set; } = string.Empty;
    public int CatalogProductId { get; set; }
    public CatalogProduct CatalogProduct { get; set; } = null!;
}
