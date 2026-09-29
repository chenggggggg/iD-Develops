using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [NotMapped]
    public class TestResultData
    {
        public string DisplayName { get; set; }
        // Sender
        public string From { get; set; }
        // Content
        public string Subject { get; set; }
        public string Body { get; set; }
        public string ResultURL { get; set; }
        public string ProductsURL { get; set; }
        public string ContactURL { get; set; }
        public string ActionIntro { get; set; } = string.Empty;
        public string ActionUrl { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public double Score { get; set; }
        public double MaxScore { get; set; }
        public string ResultLevel { get; set; } = string.Empty;
        public string ResultAdvice { get; set; } = string.Empty;
        public EmailBrandingModel Branding { get; set; } = new();
    }
}
