using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("ProductFormSubmissions")]
    public class ProductFormSubmission
    {
        [Key]
        public int Id { get; set; }

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
