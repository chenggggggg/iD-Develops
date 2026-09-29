namespace iD_Develops.Configuration
{
    public class MailSettings
    {
        public string? Provider { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? SecureSocketOptions { get; set; }
        public bool? UseSsl { get; set; }
        public string? WebhookApiKey { get; set; }
        public string? PickupDirectory { get; set; }
        public string? PublicBaseUrl { get; set; }
        public string? SupportEmail { get; set; }
        public string? ContactEmail { get; set; }
        public string? BillingEmail { get; set; }
        public string? NoReplyEmail { get; set; }
        public string? ComplianceEmail { get; set; }
        public string? LinkedInUrl { get; set; }
        public string? FacebookUrl { get; set; }
    }
}
