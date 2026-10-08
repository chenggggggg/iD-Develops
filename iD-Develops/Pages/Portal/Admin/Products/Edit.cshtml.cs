using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;

namespace iD_Develops.Pages.Portal.Admin.Products
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public sealed class EditModel : iD_Develops.Pages.ProductModel
    {
        public EditModel(
            ICatalogProductService catalogProductService,
            ICreditConfigurationService creditConfigurationService,
            CatalogProductSubmissionService catalogProductSubmissionService,
            ICatalogCheckoutService catalogCheckoutService,
            CatalogProductAccessService catalogProductAccessService,
            CatalogProductFileStorageService catalogProductFileStorageService,
            IWebHostEnvironment environment,
            ITurnstileService turnstileService,
            IApplicationUrlService applicationUrls,
            IPortalAuthenticationHandoffService authenticationHandoffService)
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
                authenticationHandoffService)
        {
        }

        protected override bool IsProductEditorRequest()
            => true;
    }
}
