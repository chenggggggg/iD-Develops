using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models;

[Table("CatalogProductFormFieldTranslations")]
public sealed class CatalogProductFormFieldTranslation
{
    public int CatalogProductFormFieldId { get; set; }

    public CatalogProductFormField CatalogProductFormField { get; set; } = null!;

    [MaxLength(10)]
    public string Culture { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Label { get; set; }

    [MaxLength(150)]
    public string? Placeholder { get; set; }

    [MaxLength(300)]
    public string? HelpText { get; set; }

    public string? OptionsText { get; set; }
}
