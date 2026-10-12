using System.Data.Common;
using System.Text.Json;
using iD_Develops.Configuration;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Utilities;

public sealed class ApplicationErrorMiddlewareTests
{
    [Fact]
    public async Task MissingPublicPage_IsReExecutedWithoutRedirecting()
    {
        var context = CreateContext("id.localhost", "/nl-nl/missing", "?source=test");
        var calls = 0;
        var middleware = CreateMiddleware(async currentContext =>
        {
            calls++;
            if (calls == 1)
            {
                currentContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            Assert.Equal("/nl-nl/error", currentContext.Request.Path);
            Assert.Equal("not-found", currentContext.Request.Query["code"]);
            Assert.Equal("404", currentContext.Request.Query["statusCode"]);
            Assert.Equal("/nl-nl/missing?source=test", currentContext.Request.Query["returnUrl"]);
            currentContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await currentContext.Response.WriteAsync("localized error page");
        });

        await middleware.InvokeAsync(context, CreateApplicationUrls());

        Assert.Equal(2, calls);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.Equal("/nl-nl/missing", context.Request.Path);
        Assert.Equal("?source=test", context.Request.QueryString.Value);
        Assert.Equal("localized error page", await ReadBodyAsync(context));
    }

    [Fact]
    public async Task MissingPortalPage_UsesCulturelessPortalErrorPage()
    {
        var context = CreateContext("portal.id.localhost", "/missing");
        var calls = 0;
        var middleware = CreateMiddleware(currentContext =>
        {
            calls++;
            if (calls == 1)
            {
                currentContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            Assert.Equal("/error", currentContext.Request.Path);
            Assert.Equal("/missing", currentContext.Request.Query["returnUrl"]);
            currentContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, CreateApplicationUrls());

        Assert.Equal(2, calls);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task ApiStatusCode_IsNotReExecutedAsHtml()
    {
        var context = CreateContext("portal.id.localhost", "/api/missing");
        var calls = 0;
        var middleware = CreateMiddleware(currentContext =>
        {
            calls++;
            currentContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, CreateApplicationUrls());

        Assert.Equal(1, calls);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(string.Empty, await ReadBodyAsync(context));
    }

    [Fact]
    public async Task DatabaseFailureFromApi_ReturnsJsonServiceUnavailableResponse()
    {
        var context = CreateContext("portal.id.localhost", "/api/data");
        var middleware = CreateMiddleware(_ => throw new TestDbException("connection refused"));

        await middleware.InvokeAsync(context, CreateApplicationUrls());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("60", context.Response.Headers.RetryAfter);
        var payload = JsonDocument.Parse(await ReadBodyAsync(context));
        Assert.Equal("db-offline", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task BrowserException_IsReExecutedAsInternalServerError()
    {
        var context = CreateContext("id.localhost", "/en-us/failure");
        var calls = 0;
        var middleware = CreateMiddleware(currentContext =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("test failure");
            }

            Assert.Equal("/en-us/error", currentContext.Request.Path);
            Assert.Equal("server-error", currentContext.Request.Query["code"]);
            Assert.Equal("500", currentContext.Request.Query["statusCode"]);
            currentContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, CreateApplicationUrls());

        Assert.Equal(2, calls);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task FailedPost_RendersErrorPageAsGetAndRestoresOriginalMethod()
    {
        var context = CreateContext("portal.id.localhost", "/orders");
        context.Request.Method = HttpMethods.Post;
        var calls = 0;
        var middleware = CreateMiddleware(currentContext =>
        {
            calls++;
            if (calls == 1)
            {
                currentContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                return Task.CompletedTask;
            }

            Assert.Equal(HttpMethods.Get, currentContext.Request.Method);
            Assert.Equal("422", currentContext.Request.Query["statusCode"]);
            currentContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, CreateApplicationUrls());

        Assert.Equal(2, calls);
        Assert.Equal(HttpMethods.Post, context.Request.Method);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
    }

    [Fact]
    public async Task ErrorPageFailure_ReturnsPlainFallbackWithOriginalStatus()
    {
        var context = CreateContext("portal.id.localhost", "/missing");
        var calls = 0;
        var middleware = CreateMiddleware(currentContext =>
        {
            calls++;
            if (calls == 1)
            {
                currentContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            throw new InvalidOperationException("error page failed");
        });

        await middleware.InvokeAsync(context, CreateApplicationUrls());

        Assert.Equal(2, calls);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", context.Response.ContentType);
        Assert.Contains("Please try again later", await ReadBodyAsync(context), StringComparison.Ordinal);
    }

    private static ApplicationErrorMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, NullLogger<ApplicationErrorMiddleware>.Instance);

    private static IApplicationUrlService CreateApplicationUrls() =>
        new ApplicationUrlService(Options.Create(new ApplicationUrlOptions
        {
            PublicBaseUrl = "http://id.localhost:5000",
            PortalBaseUrl = "http://portal.id.localhost:5000"
        }));

    private static DefaultHttpContext CreateContext(string host, string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host, 5000);
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestDbException(string message) : DbException(message);
}
