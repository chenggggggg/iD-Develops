using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;

namespace iD_Develops.Pages.Portal.Admin.Products
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public sealed class EditModel : iD_Develops.Pages.ProductModel
    {
        public EditModel(
            ICatalogProductService catalogProductService,
            ICreditConfigurationService creditConfigurationService,
            CatalogProductSubmissionService catalogProductSubmissionService,
            CatalogCheckoutCoordinator catalogCheckoutService,
            CatalogProductAccessService catalogProductAccessService,
            CatalogProductFileStorageService catalogProductFileStorageService,
            IWebHostEnvironment environment,
            ITurnstileService turnstileService,
            IApplicationUrlService applicationUrls,
            IPortalAuthenticationHandoffService authenticationHandoffService,
            IApplicationAuditService auditService,
            IStringLocalizer<iD_Develops.Pages.ProductModel> localizer)
            : base(
                catalogProductService,
                creditConfigurationService,
                catalogProductSubmissionService,
                catalogCheckoutService,
                catalogProductAccessService,
                catalogProductFileStorageService,
                environment,
                turnstileService,
                applicationUrls,
                authenticationHandoffService,
                auditService,
                localizer)
        {
        }

        protected override bool IsProductEditorRequest()
            => true;
    }
}
