using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;

namespace iD_Develops.Pages.Portal.Admin.Products
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public sealed class FreeDownloadsModel : iD_Develops.Pages.FreeDownloadsModel
    {
        public FreeDownloadsModel(
            ICatalogProductService catalogProductService,
            ICatalogProductTemplateService catalogProductTemplateService,
            CatalogProductFileStorageService catalogProductFileStorageService)
            : base(
                catalogProductService,
                catalogProductTemplateService,
                catalogProductFileStorageService)
        {
        }

        protected override bool IsEditorPage
            => true;
    }
}
