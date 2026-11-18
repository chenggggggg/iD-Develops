using iD_Develops.Utilities;
using Microsoft.AspNetCore.Http;

namespace iD_Develops.Tests.Utilities;

public sealed class PublicLanguageNavigationTests
{
    [Theory]
    [InlineData("", "", "", "nl-NL", "/nl-nl")]
    [InlineData("/", "", "", "nl-NL", "/nl-nl/")]
    [InlineData("/en-us", "", "", "nl-NL", "/nl-nl")]
    [InlineData("/NL-nl/about", "", "", "en-US", "/en-us/about")]
    [InlineData("/products/offer", "", "?token=a%2Fb&view=details", "nl-NL", "/nl-nl/products/offer?token=a%2Fb&view=details")]
    [InlineData("/en-us/results/record", "/site", "?token=abc%2Bdef", "nl-NL", "/site/nl-nl/results/record?token=abc%2Bdef")]
    public void SwitchPreservesRouteAndQuery(string path, string pathBase, string query, string culture, string expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;
        request.PathBase = pathBase;
        request.QueryString = new QueryString(query);
        Assert.Equal(expected, PublicLanguageNavigation.SwitchCulture(request, culture));
    }

    [Fact]
    public void UnsupportedLanguageIsRejected()
        => Assert.Throws<ArgumentException>(() => PublicLanguageNavigation.SwitchCulture(new DefaultHttpContext().Request, "de-DE"));
}
