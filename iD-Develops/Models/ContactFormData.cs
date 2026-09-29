using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [NotMapped]
    public class ContactFormData
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }
        public EmailBrandingModel Branding { get; set; } = new();
    }
}
