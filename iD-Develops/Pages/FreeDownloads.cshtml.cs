using System.ComponentModel.DataAnnotations;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages
{
    [ValidateAntiForgeryToken]
    public class FreeDownloadsModel : PageModel
    {
        private readonly ICatalogProductService _catalogProductService;
        private readonly ICatalogProductTemplateService _catalogProductTemplateService;
        private readonly CatalogProductFileStorageService _catalogProductFileStorageService;
        public FreeDownloadsModel(
            ICatalogProductService catalogProductService,
            ICatalogProductTemplateService catalogProductTemplateService,
            CatalogProductFileStorageService catalogProductFileStorageService)
        {
            _catalogProductService = catalogProductService;
            _catalogProductTemplateService = catalogProductTemplateService;
            _catalogProductFileStorageService = catalogProductFileStorageService;
        }

        public List<CatalogProduct> Downloads { get; private set; } = new();
        public bool IsAdminEditMode { get; private set; }
        public bool UploadsEnabled => _catalogProductFileStorageService.UploadsEnabled;
        public long MaxImageBytes => _catalogProductFileStorageService.MaxImageBytes;
        public long MaxDownloadBytes => _catalogProductFileStorageService.MaxDownloadBytes;
        public string ImageAccept => _catalogProductFileStorageService.ImageAccept;
        public string DownloadAccept => _catalogProductFileStorageService.DownloadAccept;

        [BindProperty(SupportsGet = true)]
        public string? ContentCulture { get; set; } = CatalogContentCulture.Default;

        public bool IsDefaultContentCulture => CatalogContentCulture.IsDefault(ContentCulture);

        [BindProperty]
        public FreeDownloadEditInput EditInput { get; set; } = new();

        public async Task OnGetAsync(CancellationToken ct = default)
        {
            ContentCulture = CatalogContentCulture.Normalize(ContentCulture);
            await LoadDownloadsAsync(ct);
        }

        public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            var product = _catalogProductTemplateService.CreateProduct("free-download");
            if (product == null)
            {
                TempData["StatusMessage"] = "Could not create a free download.";
                return RedirectToEditor();
            }

            await _catalogProductService.CreateProductAsync(product, ct);
            TempData["StatusMessage"] = "Untitled download created.";
            return RedirectToEditor();
        }

        public async Task<IActionResult> OnGetDownloadAsync(int id, CancellationToken ct)
        {
            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null || product.ProductType != CatalogProductType.FreeDownload)
            {
                return NotFound();
            }

            if (product.Status != CatalogProductStatus.Published && !CanManageProducts())
            {
                return NotFound();
            }

            if (!_catalogProductFileStorageService.Exists(product.IncludedBookingBenefitUrl))
            {
                return NotFound();
            }

            var downloadUrl = await _catalogProductFileStorageService.ResolveReadUrlAsync(product.IncludedBookingBenefitUrl);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return NotFound();
            }

            return Redirect(downloadUrl);
        }

        public async Task<IActionResult> OnPostSaveAsync(int id, CancellationToken ct)
        {
            ContentCulture = CatalogContentCulture.Normalize(ContentCulture);
            if (!CanManageProducts())
            {
                return Forbid();
            }

            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null || product.ProductType != CatalogProductType.FreeDownload)
            {
                return NotFound();
            }

            IsAdminEditMode = true;

            var imageUrl = string.IsNullOrWhiteSpace(EditInput.ImageObjectKey)
                ? product.ImageUrl
                : EditInput.ImageObjectKey.Trim();

            var attachmentUrl = string.IsNullOrWhiteSpace(EditInput.DownloadObjectKey)
                ? product.IncludedBookingBenefitUrl
                : EditInput.DownloadObjectKey.Trim();

            if (IsDefaultContentCulture && string.IsNullOrWhiteSpace(EditInput.Name))
            {
                ModelState.AddModelError(string.Empty, "Give this download a title before saving.");
            }

            if (!_catalogProductFileStorageService.Exists(attachmentUrl))
            {
                ModelState.AddModelError(string.Empty, "Choose a download file before saving this download.");
            }

            if (!ModelState.IsValid)
            {
                await DeleteNewUploadsAsync(product, imageUrl, attachmentUrl);
                Downloads = await LoadAdminDownloadsAsync(ct);
                return Page();
            }

            var previousImageUrl = product.ImageUrl;
            var previousAttachmentUrl = product.IncludedBookingBenefitUrl;

            try
            {
                var canonicalName = IsDefaultContentCulture
                    ? EditInput.Name!.Trim()
                    : product.Name;
                await _catalogProductService.UpdateProductAsync(new CatalogProduct
                {
                    Id = product.Id,
                    Name = canonicalName,
                    Slug = IsDefaultContentCulture ? EditInput.Name : product.Slug,
                    Summary = IsDefaultContentCulture ? EditInput.Description : product.Summary,
                    Description = product.Description,
                    FullDescriptionHtml = product.FullDescriptionHtml,
                    ImageUrl = imageUrl,
                    ProductType = product.ProductType,
                    WorkflowType = product.WorkflowType,
                    Status = CatalogProductStatus.Published,
                    IsSalesActive = product.IsSalesActive,
                    RequireAccessToken = product.RequireAccessToken,
                    Currency = product.Currency,
                    BasePrice = product.BasePrice,
                    ExternalBookingUrl = product.ExternalBookingUrl,
                    ExternalBookingButtonText = product.ExternalBookingButtonText,
                    ConfirmationEmailSubject = product.ConfirmationEmailSubject,
                    ConfirmationEmailBodyHtml = product.ConfirmationEmailBodyHtml,
                    OwnerNotificationSubject = product.OwnerNotificationSubject,
                    OwnerNotificationBodyHtml = product.OwnerNotificationBodyHtml,
                    AllowsMultipleParticipants = product.AllowsMultipleParticipants,
                    MaxParticipants = product.MaxParticipants,
                    HideFromProductsPage = true,
                    EnableQuantity = product.EnableQuantity,
                    MinQuantity = product.MinQuantity,
                    MaxQuantity = product.MaxQuantity,
                    RequiresAccountCreation = product.RequiresAccountCreation,
                    GrantedCourseId = product.GrantedCourseId,
                    IncludedBookingBenefitLabel = IsDefaultContentCulture
                        ? EditInput.Name
                        : product.IncludedBookingBenefitLabel,
                    IncludedBookingBenefitUrl = attachmentUrl,
                    IsFeatured = product.IsFeatured,
                    SortOrder = product.SortOrder
                }, ct);

                if (!IsDefaultContentCulture)
                {
                    await _catalogProductService.SaveTranslationsAsync(
                        product.Id,
                        new CatalogProductTranslationInput(
                            ContentCulture,
                            EditInput.Name,
                            EditInput.Description,
                            null,
                            null,
                            null,
                            [],
                            []),
                        ct);
                }
            }
            catch
            {
                await DeleteNewUploadsAsync(product, imageUrl, attachmentUrl);
                ModelState.AddModelError(string.Empty, "Save failed. Your uploaded file was not published. Please try again.");
                Downloads = await LoadAdminDownloadsAsync(ct);
                return Page();
            }

            if (!string.Equals(previousImageUrl, imageUrl, StringComparison.OrdinalIgnoreCase))
            {
                await TryDeleteAsync(previousImageUrl);
            }

            if (!string.Equals(previousAttachmentUrl, attachmentUrl, StringComparison.OrdinalIgnoreCase))
            {
                await TryDeleteAsync(previousAttachmentUrl);
            }

            TempData["StatusMessage"] = product.Status == CatalogProductStatus.Published
                ? "Download saved."
                : "Download published.";
            return RedirectToEditor(ContentCulture);
        }

        public async Task<IActionResult> OnPostSetStatusAsync(
            int id,
            CatalogProductStatus status,
            CancellationToken ct)
        {
            ContentCulture = CatalogContentCulture.Normalize(ContentCulture);
            if (!CanManageProducts())
            {
                return Forbid();
            }

            if (status is not (CatalogProductStatus.Draft or CatalogProductStatus.Archived))
            {
                return BadRequest();
            }

            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null || product.ProductType != CatalogProductType.FreeDownload)
            {
                return NotFound();
            }

            product.Status = status;
            await _catalogProductService.UpdateProductAsync(product, ct);

            TempData["StatusMessage"] = status == CatalogProductStatus.Draft
                ? "Download moved to draft."
                : "Download archived.";
            return RedirectToEditor(ContentCulture);
        }

        public async Task<IActionResult> OnPostCreateImageUploadAsync(int id, string fileName, string? contentType, long fileSize, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return await CreateUploadResultAsync(id, CatalogProductFileKind.Image, fileName, contentType, fileSize, ct);
        }

        public async Task<IActionResult> OnPostCreateDownloadUploadAsync(int id, string fileName, string? contentType, long fileSize, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return await CreateUploadResultAsync(id, CatalogProductFileKind.Download, fileName, contentType, fileSize, ct);
        }

        public IActionResult OnPostVerifyImageUpload(string fileName, string? contentType, long fileSize)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return CreateVerifyResult(CatalogProductFileKind.Image, fileName, contentType, fileSize);
        }

        public IActionResult OnPostVerifyDownloadUpload(string fileName, string? contentType, long fileSize)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return CreateVerifyResult(CatalogProductFileKind.Download, fileName, contentType, fileSize);
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
        {
            ContentCulture = CatalogContentCulture.Normalize(ContentCulture);
            if (!CanManageProducts())
            {
                return Forbid();
            }

            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null || product.ProductType != CatalogProductType.FreeDownload)
            {
                return NotFound();
            }

            await _catalogProductFileStorageService.DeleteAsync(product.ImageUrl);
            await _catalogProductFileStorageService.DeleteAsync(product.IncludedBookingBenefitUrl);
            await _catalogProductService.DeleteProductAsync(id, ct);
            TempData["StatusMessage"] = "Download removed.";
            return RedirectToEditor(ContentCulture);
        }

        public async Task<string?> ResolveReadUrlAsync(string? reference)
            => await _catalogProductFileStorageService.ResolveReadUrlAsync(reference);

        public async Task<string?> ResolvePublicAssetUrlAsync(string? reference)
            => await _catalogProductFileStorageService.ResolvePublicAssetUrlAsync(reference);

        private async Task<IActionResult> CreateUploadResultAsync(
            int id,
            CatalogProductFileKind kind,
            string? fileName,
            string? contentType,
            long fileSize,
            CancellationToken ct)
        {
            try
            {
                var product = await _catalogProductService.GetProductByIdAsync(id, ct);
                if (product == null || product.ProductType != CatalogProductType.FreeDownload)
                {
                    return NotFound();
                }

                var upload = await _catalogProductFileStorageService.CreateUploadAsync(
                    kind,
                    CatalogProductFileScope.FreeDownload,
                    product.Id,
                    fileName,
                    contentType,
                    fileSize);

                return new JsonResult(new
                {
                    success = true,
                    objectKey = upload.ObjectKey,
                    putUrl = upload.PutUrl,
                    readUrl = upload.ReadUrl,
                    maxBytes = upload.MaxBytes,
                    cacheControl = upload.CacheControl
                });
            }
            catch (InvalidOperationException ex)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return new JsonResult(new { success = false, errorMessage = ex.Message });
            }
        }

        private IActionResult CreateVerifyResult(
            CatalogProductFileKind kind,
            string? fileName,
            string? contentType,
            long fileSize)
        {
            try
            {
                var verifiedFile = _catalogProductFileStorageService.VerifyUpload(kind, fileName, contentType, fileSize);
                return new JsonResult(new
                {
                    success = true,
                    fileName = verifiedFile.FileName,
                    contentType = verifiedFile.ContentType,
                    maxBytes = verifiedFile.MaxBytes
                });
            }
            catch (InvalidOperationException ex)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return new JsonResult(new { success = false, errorMessage = ex.Message });
            }
        }

        private async Task DeleteNewUploadsAsync(CatalogProduct product, string? imageUrl, string? attachmentUrl)
        {
            if (IsNewUploadReference(imageUrl, product.ImageUrl, EditInput.OriginalImageObjectKey))
            {
                await TryDeleteAsync(imageUrl);
            }

            if (IsNewUploadReference(attachmentUrl, product.IncludedBookingBenefitUrl, EditInput.OriginalDownloadObjectKey))
            {
                await TryDeleteAsync(attachmentUrl);
            }
        }

        private async Task TryDeleteAsync(string? reference)
        {
            try
            {
                await _catalogProductFileStorageService.DeleteAsync(reference);
            }
            catch
            {
                // Storage cleanup is compensating work. Keep the database outcome visible to the user.
            }
        }

        private static bool IsNewUploadReference(string? submittedReference, string? databaseReference, string? originalReference)
        {
            if (string.IsNullOrWhiteSpace(submittedReference))
            {
                return false;
            }

            var submitted = submittedReference.Trim();
            return !string.Equals(submitted, databaseReference?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(submitted, originalReference?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        protected virtual bool IsEditorPage
            => false;

        private bool CanManageProducts()
            => IsEditorPage &&
               (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"));

        private IActionResult RedirectToEditor(string? contentCulture = null)
            => RedirectToRoute(
                ApplicationHostPageRouteModelConvention.PortalFreeDownloadsEditRouteName,
                new
                {
                    contentCulture = CatalogContentCulture.IsDefault(contentCulture)
                        ? null
                        : CatalogContentCulture.Normalize(contentCulture)
                });

        private async Task LoadDownloadsAsync(CancellationToken ct)
        {
            IsAdminEditMode = CanManageProducts();
            Downloads = IsAdminEditMode
                ? await LoadAdminDownloadsAsync(ct)
                : await _catalogProductService.GetPublishedFreeDownloadsForCultureAsync(
                    System.Globalization.CultureInfo.CurrentUICulture.Name,
                    ct);
        }

        private async Task<List<CatalogProduct>> LoadAdminDownloadsAsync(CancellationToken ct)
        {
            var downloads = await _catalogProductService.GetAdminFreeDownloadsAsync(ct);
            if (IsDefaultContentCulture)
            {
                return downloads;
            }

            foreach (var download in downloads)
            {
                var translation = download.Translations.FirstOrDefault(candidate =>
                    string.Equals(candidate.Culture, ContentCulture, StringComparison.OrdinalIgnoreCase));
                download.Name = translation?.Name ?? string.Empty;
                download.Summary = translation?.Summary;
            }

            return downloads;
        }

        public class FreeDownloadEditInput
        {
            [MaxLength(200)]
            public string? Name { get; set; }

            [MaxLength(4000)]
            public string? Description { get; set; }

            [MaxLength(500)]
            public string? ImageObjectKey { get; set; }

            [MaxLength(500)]
            public string? DownloadObjectKey { get; set; }

            [MaxLength(500)]
            public string? OriginalImageObjectKey { get; set; }

            [MaxLength(500)]
            public string? OriginalDownloadObjectKey { get; set; }
        }
    }
}
