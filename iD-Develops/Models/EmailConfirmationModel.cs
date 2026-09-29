using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [NotMapped]
    public class EmailConfirmationModel
    {
        public string DisplayName { get; set; }
        public string ConfirmationLink { get; set; }
        public EmailBrandingModel Branding { get; set; } = new();
    }
}
