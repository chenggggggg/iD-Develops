using iD_Develops.Utilities;
using Microsoft.AspNetCore.Http;

namespace iD_Develops.Tests.Utilities;

public sealed class RequestClassificationTests
{
    [Theory]
    [InlineData("cors", "empty", true)]
    [InlineData("same-origin", "empty", true)]
    [InlineData("no-cors", "empty", true)]
    [InlineData("navigate", "document", false)]
    [InlineData("navigate", "iframe", false)]
    [InlineData("no-cors", "image", false)]
    [InlineData(null, null, false)]
    public void FetchMetadataDistinguishesProgrammaticRequestsFromNavigation(string? mode, string? destination, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/profile";
        if (mode != null) context.Request.Headers["Sec-Fetch-Mode"] = mode;
        if (destination != null) context.Request.Headers["Sec-Fetch-Dest"] = destination;
        Assert.Equal(expected, DatabaseAvailability.IsAjaxOrApiRequest(context));
    }
}