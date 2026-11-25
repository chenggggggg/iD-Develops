namespace iD_Develops.Models
{
    public class CatalogProductSubmissionEmailModel
    {
        public CatalogProduct Product { get; set; } = null!;
        public IDictionary<string, string> SubmittedValues { get; set; } = new Dictionary<string, string>();
        public List<ProductEmailAnswer>? Answers { get; set; }
        public string Culture { get; set; } = "en-us";
        public bool IsCompanyNotification { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public SignupPersonalDetails? PersonalDetails { get; set; }
        public int OrderQuantity { get; set; } = 1;
        public int ParticipantCount { get; set; }
        public string IntroHtml { get; set; } = string.Empty;
        public EmailBrandingModel Branding { get; set; } = new();

        // Older queued notifications only contain key/value pairs. Keep them deliverable
        // without exposing system metadata or requiring the current product definition.
        public IEnumerable<ProductEmailAnswer> ReadableAnswers() => Answers ?? SubmittedValues
            .Where(item => !IsMetadata(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => new ProductEmailAnswer { Label = ReadableLabel(item.Key), Value = item.Value }).ToList();

        public static bool IsMetadata(string key) => SignupPersonalDetails.IsPersonalKey(key) ||
            key is "order_quantity" or "participant_count" or "product_slug" or "workflow_type";

        public static string ReadableLabel(string key) => string.Join(" ", key.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries));
    }

    public sealed class ProductEmailAnswer
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
