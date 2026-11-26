using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ProductFormSubmissions")]
    public class ProductFormSubmission
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(64)]
        public string? RequestKey { get; set; }

        // Historical reference, deliberately without a cascading product relationship.
        // Keep the original identity even if a product is later deleted.
        public int? CatalogProductId { get; set; }

        [MaxLength(200)]
        public string? ProductSlug { get; set; }

        [MaxLength(200)]
        public string? CustomerName { get; set; }

        [Required]
        public string FormName { get; set; } // Identifier for the form type

        [Required]
        public DateTime SubmissionDate { get; set; } = DateTime.UtcNow;

        [Required]
        public string UserEmail { get; set; } // User’s email or identifier

        [Required]
        public string SubmissionDataJson { get; set; } // JSON storing submitted form data as key-value pairs
    }
}
