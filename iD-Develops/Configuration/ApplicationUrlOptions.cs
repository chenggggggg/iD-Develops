namespace iD_Develops.Configuration
{
    public sealed class ApplicationUrlOptions
    {
        public const string SectionName = "ApplicationUrls";

        public string PublicBaseUrl { get; set; } = "http://id.localhost:5000";
        public string PortalBaseUrl { get; set; } = "http://portal.id.localhost:5000";
    }
}
