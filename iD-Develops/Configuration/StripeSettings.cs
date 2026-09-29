namespace iD_Develops.Configuration
{
    public class StripeSettings
    {
        public string? SecretKey { get; set; }

        public string? WebhookSecret { get; set; }

        public bool Enabled { get; set; }

        public bool Required { get; set; }
    }
}
