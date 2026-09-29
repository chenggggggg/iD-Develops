using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [NotMapped]
    public class EmailBrandingModel
    {
        public string SiteName { get; set; } = "iD! Training & Development";
        public string PublicBaseUrl { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string SupportEmail { get; set; } = string.Empty;
        public string LinkedInUrl { get; set; } = string.Empty;
        public string FacebookUrl { get; set; } = string.Empty;
    }
}
