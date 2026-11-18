namespace iD_Develops.Utilities;

public static class PublicLanguageNavigation
{
    public static string OtherCulture(string culture)
        => culture.Equals("nl-NL", StringComparison.OrdinalIgnoreCase) ? "en-US" : "nl-NL";

    public static string SwitchCulture(HttpRequest request, string culture)
    {
        var target = culture.ToLowerInvariant();
        if (target is not ("en-us" or "nl-nl"))
            throw new ArgumentException("Unsupported public language.", nameof(culture));

        var path = request.Path.Value ?? string.Empty;
        var end = path.Length > 0 ? path.IndexOf('/', 1) : -1;
        var segment = path.Length > 0 ? path[1..(end < 0 ? path.Length : end)] : string.Empty;
        if (segment.Equals("en-us", StringComparison.OrdinalIgnoreCase) || segment.Equals("nl-nl", StringComparison.OrdinalIgnoreCase))
            path = end < 0 ? string.Empty : path[end..];

        return request.PathBase.Add(new PathString("/" + target)).Add(new PathString(path)).ToUriComponent()
            + request.QueryString.ToUriComponent();
    }
}
