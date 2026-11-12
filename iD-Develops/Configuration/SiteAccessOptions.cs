namespace iD_Develops.Configuration;

public enum SiteMode
{
    Marketing,
    Full
}

public sealed class SiteAccessOptions
{
    public SiteMode SiteMode { get; set; } = SiteMode.Marketing;
}
