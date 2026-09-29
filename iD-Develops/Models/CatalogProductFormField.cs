using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CatalogProductFormFields")]
    public class CatalogProductFormField
    {
        public int Id { get; set; }

        public int CatalogProductId { get; set; }

        public CatalogProduct CatalogProduct { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Label { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? Placeholder { get; set; }

        [MaxLength(300)]
        public string? HelpText { get; set; }

        public CatalogFormFieldType FieldType { get; set; }

        public bool IsRequired { get; set; }

        public bool IsPerParticipant { get; set; }

        public int? CharacterLimit { get; set; }

        public int? ListItemCount { get; set; }

        public bool AllowMultipleOptions { get; set; } = true;

        public int SortOrder { get; set; }

        public string? OptionsText { get; set; }

        [NotMapped]
        public IReadOnlyList<string> Options => (OptionsText ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
