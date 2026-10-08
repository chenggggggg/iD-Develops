using iD_Develops.Configuration;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class ApplicationRoutingTests
{
    [Fact]
    public void ApplicationUrlService_BuildsUrlsAndRecognizesPortalHost()
    {
        var service = new ApplicationUrlService(Options.Create(new ApplicationUrlOptions
        {
            PublicBaseUrl = "https://example.test",
            PortalBaseUrl = "https://portal.example.test"
        }));
        var portalRequest = new DefaultHttpContext().Request;
        portalRequest.Host = new HostString("portal.example.test");

        Assert.Equal("https://example.test/en-us/products", service.PublicUrl("/en-us/products"));
        Assert.Equal("https://portal.example.test/login", service.PortalUrl("login"));
        Assert.True(service.IsPortalRequest(portalRequest));
    }

    [Fact]
    public void PortalSessionIndicator_SharesOnlyProtectedDisplayStateAcrossConfiguredHosts()
    {
        var service = new PortalSessionIndicatorService(
            new EphemeralDataProtectionProvider(),
            Options.Create(new ApplicationUrlOptions
            {
                PublicBaseUrl = "https://id.localhost:5000",
                PortalBaseUrl = "https://portal.id.localhost:5000"
            }));
        var portalContext = new DefaultHttpContext();
        portalContext.Request.Scheme = "https";

        service.MarkSignedIn(portalContext, isPersistent: true);

        var setCookie = portalContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("domain=.id.localhost", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        var publicContext = new DefaultHttpContext();
        publicContext.Request.Headers.Cookie = setCookie.Split(';', 2)[0];
        Assert.True(service.HasActiveSession(publicContext.Request));

        publicContext.Request.Headers.Cookie = $"{PortalSessionIndicatorService.CookieName}=tampered";
        Assert.False(service.HasActiveSession(publicContext.Request));
    }

    [Fact]
    public void RouteConvention_AssignsPublicProductToPublicModuleOnly()
    {
        var model = CreatePageRouteModel("/Product", "{slug}");
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal("{culture=en-us}/products/{slug}", selector.AttributeRouteModel!.Template);
        Assert.Equal(ApplicationHostPageRouteModelConvention.PublicProductRouteName, selector.AttributeRouteModel.Name);
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains("example.test"));
    }

    [Fact]
    public void RouteConvention_AssignsProductEditorToPortalModuleOnly()
    {
        var model = CreatePageRouteModel("/Portal/Admin/Products/Edit", "Portal/Admin/Products/Edit");
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal("products/{slug}/edit", selector.AttributeRouteModel!.Template);
        Assert.Equal(ApplicationHostPageRouteModelConvention.PortalProductEditRouteName, selector.AttributeRouteModel.Name);
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains("portal.example.test"));
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is AuthorizeAttribute authorization && authorization.Roles == "Admin,SuperAdmin");
    }

    [Theory]
    [InlineData("/Index", "", "{culture=en-us}")]
    [InlineData("/Products", "Products", "{culture=en-us}/Products")]
    [InlineData("/Error", "Error", "{culture=en-us}/error")]
    public void RouteConvention_DefaultsCultureForPublicLinkGeneration(
        string viewEnginePath,
        string template,
        string expectedTemplate)
    {
        var model = CreatePageRouteModel(viewEnginePath, template);
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal(expectedTemplate, selector.AttributeRouteModel!.Template);
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains("example.test"));
    }

    [Fact]
    public void RouteConvention_AssignsPortalErrorToCulturelessPortalRoute()
    {
        var model = CreatePageRouteModel("/Portal/Error", "Portal/Error");
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal("error", selector.AttributeRouteModel!.Template);
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains("portal.example.test"));
    }

    [Fact]
    public void RouteConvention_KeepsIdentityErrorOffThePublicErrorRoute()
    {
        var model = CreatePageRouteModel("/Error", "Error", "Identity");
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal("identity/Error", selector.AttributeRouteModel!.Template);
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains("portal.example.test"));
        Assert.DoesNotContain(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains("example.test"));
    }

    [Fact]
    public void RazorPages_RegistersOneEndpointForEachErrorSurface()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        builder.Services
            .AddRazorPages()
            .AddApplicationPart(typeof(iD_Develops.Pages.ErrorModel).Assembly)
            .AddRazorPagesOptions(options =>
                options.Conventions.Add(new ApplicationHostPageRouteModelConvention(
                    "example.test",
                    "portal.example.test")))
            .AddRazorRuntimeCompilation();

        using var app = builder.Build();
        app.MapRazorPages();
        var endpoints = ((IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath
                .EndsWith("/Error", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var publicError = Assert.Single(endpoints, endpoint =>
            endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath == "/Error");
        Assert.Equal("{culture=en-us}/error", publicError.RoutePattern.RawText);

        var portalError = Assert.Single(endpoints, endpoint =>
            endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath == "/Portal/Error");
        Assert.Equal("error", portalError.RoutePattern.RawText);
    }

    [Theory]
    [InlineData("/FreeDownloads", "FreeDownloads", "{culture=en-us}/free-downloads", ApplicationHostPageRouteModelConvention.PublicFreeDownloadsRouteName, "example.test")]
    [InlineData("/Portal/Admin/Products/FreeDownloads", "Portal/Admin/Products/FreeDownloads", "products/free-downloads/edit", ApplicationHostPageRouteModelConvention.PortalFreeDownloadsEditRouteName, "portal.example.test")]
    [InlineData("/Examination/Completed", "Examination/Completed", "{culture=en-us}/examination/completed/{recordId:guid}", ApplicationHostPageRouteModelConvention.PublicExamCompletedRouteName, "example.test")]
    [InlineData("/Portal/Examination/Completed", "Portal/Examination/Completed", "examination/completed/{recordId:guid}", ApplicationHostPageRouteModelConvention.PortalExamCompletedRouteName, "portal.example.test")]
    public void RouteConvention_AssignsSharedWorkflowsToOneModule(
        string viewEnginePath,
        string originalTemplate,
        string expectedTemplate,
        string expectedRouteName,
        string expectedHost)
    {
        var model = CreatePageRouteModel(viewEnginePath, originalTemplate);
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal(expectedTemplate, selector.AttributeRouteModel!.Template);
        Assert.Equal(expectedRouteName, selector.AttributeRouteModel.Name);
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains(expectedHost));
    }

    [Fact]
    public void RouteConvention_RemovesPortalPrefixAndCultureFromPortalPages()
    {
        var model = CreatePageRouteModel("/Portal/Courses/Course", "/courses/{courseId:int}");
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal("courses/{courseId:int}", selector.AttributeRouteModel!.Template);
        Assert.DoesNotContain("culture", selector.AttributeRouteModel.Template, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(selector.EndpointMetadata, metadata =>
            metadata is HostAttribute host && host.Hosts.Contains("portal.example.test"));
    }

    [Fact]
    public void RouteConvention_RemovesConventionalIndexSegmentFromPortalPages()
    {
        var model = CreatePageRouteModel("/Portal/Settings/Index", "Portal/Settings/Index");
        var convention = new ApplicationHostPageRouteModelConvention("example.test", "portal.example.test");

        convention.Apply(model);

        var selector = Assert.Single(model.Selectors);
        Assert.Equal("Settings", selector.AttributeRouteModel!.Template);
    }

    [Fact]
    public void AuthenticationHandoff_OnlyAcceptsProtectedPublicProductReturns()
    {
        var service = new PortalAuthenticationHandoffService(new EphemeralDataProtectionProvider());
        const string returnPath = "/en-us/products/dutch-course?resumeCheckout=true";

        var token = service.CreateToken("user-123", returnPath);
        var handoff = service.ValidateToken(token);

        Assert.NotNull(handoff);
        Assert.Equal("user-123", handoff!.UserId);
        Assert.Equal(returnPath, handoff.ReturnPath);
        Assert.Null(service.ValidateToken(token + "tampered"));
        Assert.False(PortalAuthenticationHandoffService.IsSafePublicReturnPath("//attacker.example/path"));
        Assert.False(PortalAuthenticationHandoffService.IsSafePublicReturnPath("/en-us/about"));
    }

    private static PageRouteModel CreatePageRouteModel(
        string viewEnginePath,
        string template,
        string? areaName = null)
    {
        var model = new PageRouteModel(viewEnginePath + ".cshtml", viewEnginePath, areaName);
        model.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel { Template = template }
        });
        return model;
    }
}
