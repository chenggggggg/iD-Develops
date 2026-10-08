using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace iD_Develops.Utilities
{
    /// <summary>
    /// Publishes the public website and English-only portal as separate route
    /// surfaces while both are served by the same ASP.NET Core application.
    /// </summary>
    public sealed class ApplicationHostPageRouteModelConvention : IPageRouteModelConvention
    {
        public const string PublicProductRouteName = "PublicProduct";
        public const string PortalProductEditRouteName = "PortalProductEdit";
        public const string PublicFreeDownloadsRouteName = "PublicFreeDownloads";
        public const string PortalFreeDownloadsEditRouteName = "PortalFreeDownloadsEdit";
        public const string PublicExamCompletedRouteName = "PublicExamCompleted";
        public const string PortalExamCompletedRouteName = "PortalExamCompleted";

        private static readonly IReadOnlyDictionary<string, string> PortalRoutes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/Portal/Home/Index"] = string.Empty,
                ["/Portal/Exams/List"] = "exams",
                ["/Portal/Exams/Record"] = "exams/results/{recordId:guid}",
                ["/Portal/Examination/Start"] = "exams/{examId:int}/start",
                ["/Portal/Examination/Index"] = "examination/{recordId:guid}",
                ["/Portal/Examination/Edit"] = "exams/{examId:int}/edit",
                ["/Portal/Admin/Products/Index"] = "products",
                ["/Portal/Examination/Completed"] = "examination/completed/{recordId:guid}"
            };

        private static readonly IReadOnlyDictionary<string, string> IdentityRoutes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/Account/Login"] = "login",
                ["/Account/Logout"] = "logout",
                ["/Account/Register"] = "register",
                ["/Account/AccessDenied"] = "access-denied",
                ["/Account/ForgotPassword"] = "account/forgot-password",
                ["/Account/ForgotPasswordConfirmation"] = "account/forgot-password-confirmation",
                ["/Account/ResetPassword"] = "account/reset-password",
                ["/Account/ResetPasswordConfirmation"] = "account/reset-password-confirmation",
                ["/Account/ConfirmEmail"] = "account/confirm-email",
                ["/Account/ConfirmEmailChange"] = "account/confirm-email-change",
                ["/Account/RegisterConfirmation"] = "account/register-confirmation",
                ["/Account/Manage/Index"] = "account/manage"
            };

        private readonly string[] _publicHosts;
        private readonly string[] _portalHosts;

        public ApplicationHostPageRouteModelConvention(string publicHost, string portalHost)
        {
            _publicHosts = [publicHost];
            _portalHosts = [portalHost];
        }

        public void Apply(PageRouteModel model)
        {
            if (model.Selectors.Count == 0)
            {
                return;
            }

            // Area ownership must be resolved before comparing ViewEnginePath.
            // Identity's Razor Class Library contains pages (including /Error)
            // whose view paths can match application pages outside the area.
            if (string.Equals(model.AreaName, "Identity", StringComparison.OrdinalIgnoreCase))
            {
                var identityRoute = IdentityRoutes.TryGetValue(model.ViewEnginePath, out var configuredRoute)
                    ? configuredRoute
                    : "identity" + NormalizePagePath(model.ViewEnginePath);
                ReplaceSelectors(model, Route(identityRoute, _portalHosts));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Product", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route("{culture=en-us}/products/{slug}", _publicHosts, PublicProductRouteName));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/FreeDownloads", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route("{culture=en-us}/free-downloads", _publicHosts, PublicFreeDownloadsRouteName));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Portal/Admin/Products/Edit", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route("products/{slug}/edit", _portalHosts, PortalProductEditRouteName, authorizeProductManagement: true));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Portal/Admin/Products/FreeDownloads", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route("products/free-downloads/edit", _portalHosts, PortalFreeDownloadsEditRouteName, authorizeProductManagement: true));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Portal/Examination/Level-Test-Introduction", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(model, Route("{culture=en-us}/level-test", _publicHosts));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Portal/Examination/Level-Test", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(model, Route("{culture=en-us}/examination/level-test/{recordId:guid?}", _publicHosts));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Examination/Completed", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route("{culture=en-us}/examination/completed/{recordId:guid}", _publicHosts, PublicExamCompletedRouteName));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Portal/Examination/Completed", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route(PortalRoutes[model.ViewEnginePath], _portalHosts, PortalExamCompletedRouteName));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Examination/Results", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(model, Route("{culture=en-us}/examination/results/{recordId:guid}", _publicHosts));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Error", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route("{culture=en-us}/error", _publicHosts));
                return;
            }

            if (string.Equals(model.ViewEnginePath, "/Portal/Error", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceSelectors(
                    model,
                    Route("error", _portalHosts));
                return;
            }

            if (model.ViewEnginePath.StartsWith("/Portal/", StringComparison.OrdinalIgnoreCase))
            {
                var portalRoute = ResolvePortalRoute(model);
                ReplaceSelectors(model, Route(portalRoute, _portalHosts));
                return;
            }

            ApplyPublicRoutes(model);
        }

        private void ApplyPublicRoutes(PageRouteModel model)
        {
            foreach (var selector in model.Selectors)
            {
                if (selector.AttributeRouteModel == null)
                {
                    continue;
                }

                var originalTemplate = selector.AttributeRouteModel.Template ?? string.Empty;
                if (!originalTemplate.Contains("{culture", StringComparison.OrdinalIgnoreCase))
                {
                    selector.AttributeRouteModel.Template =
                        AttributeRouteModel.CombineTemplates("{culture=en-us}", originalTemplate);
                }

                selector.EndpointMetadata.Add(new HostAttribute(_publicHosts));
            }
        }

        private static string ResolvePortalRoute(PageRouteModel model)
        {
            if (PortalRoutes.TryGetValue(model.ViewEnginePath, out var configuredRoute))
            {
                return configuredRoute;
            }

            var originalTemplate = model.Selectors[0].AttributeRouteModel?.Template ?? string.Empty;
            var normalizedTemplate = originalTemplate.TrimStart('/');
            if (string.Equals(
                    normalizedTemplate,
                    model.ViewEnginePath.TrimStart('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                var conventionalPagePath = model.ViewEnginePath["/Portal".Length..];
                return NormalizePagePath(conventionalPagePath).TrimStart('/');
            }

            if (normalizedTemplate.StartsWith("portal/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTemplate["portal/".Length..];
            }

            if (!string.IsNullOrWhiteSpace(normalizedTemplate))
            {
                return normalizedTemplate;
            }

            var pagePath = model.ViewEnginePath["/Portal".Length..];
            return NormalizePagePath(pagePath).TrimStart('/');
        }

        private static string NormalizePagePath(string pagePath)
        {
            var normalized = pagePath.Replace("/Index", string.Empty, StringComparison.OrdinalIgnoreCase);
            return normalized == "/" ? string.Empty : normalized;
        }

        private static RouteDefinition Route(
            string template,
            string[] hosts,
            string? routeName = null,
            bool authorizeProductManagement = false)
            => new(template, hosts, routeName, authorizeProductManagement);

        private static void ReplaceSelectors(PageRouteModel model, params RouteDefinition[] routes)
        {
            var source = model.Selectors[0];
            model.Selectors.Clear();

            foreach (var route in routes)
            {
                var selector = new SelectorModel(source)
                {
                    AttributeRouteModel = new AttributeRouteModel
                    {
                        Template = route.Template,
                        Name = route.RouteName
                    }
                };
                selector.EndpointMetadata.Add(new HostAttribute(route.Hosts));
                if (route.AuthorizeProductManagement)
                {
                    selector.EndpointMetadata.Add(new AuthorizeAttribute { Roles = "Admin,SuperAdmin" });
                }

                model.Selectors.Add(selector);
            }
        }

        private sealed record RouteDefinition(
            string Template,
            string[] Hosts,
            string? RouteName,
            bool AuthorizeProductManagement);
    }
}
