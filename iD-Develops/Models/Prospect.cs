using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("Prospects")]
    public class Prospect
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        [Required]
        [MaxLength(256)]
        [EmailAddress]
        public string Email { get; set; }
        public string UnsubscribeToken { get; set; }
        public bool IsUnsubscribed { get; set; }
        public bool IsBounced { get; set; }
        public bool IsSuppressed { get; set; }
    }
}
