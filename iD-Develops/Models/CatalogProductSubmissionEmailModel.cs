namespace iD_Develops.Models
{
    public class CatalogProductSubmissionEmailModel
    {
        public CatalogProduct Product { get; set; } = null!;
        public IDictionary<string, string> SubmittedValues { get; set; } = new Dictionary<string, string>();
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public SignupPersonalDetails? PersonalDetails { get; set; }
        public int OrderQuantity { get; set; } = 1;
        public int ParticipantCount { get; set; }
        public string IntroHtml { get; set; } = string.Empty;
        public EmailBrandingModel Branding { get; set; } = new();
    }
}
