namespace iD_Develops.Configuration
{
    public sealed class TurnstileSettings
    {
        public bool Enabled { get; set; }
        public bool Required { get; set; }
        public string? SiteKey { get; set; }
        public string? SecretKey { get; set; }
    }
}
