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
            ICatalogCheckoutService catalogCheckoutService,
            CatalogProductAccessService catalogProductAccessService,
            CatalogProductFileStorageService catalogProductFileStorageService,
            IWebHostEnvironment environment,
            ITurnstileService turnstileService,
            IApplicationUrlService applicationUrls,
            IPortalAuthenticationHandoffService authenticationHandoffService,
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
                localizer)
        {
        }

        protected override bool IsProductEditorRequest()
            => true;
    }
}
