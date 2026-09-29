using System.ComponentModel.DataAnnotations;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
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

        [BindProperty]
        public FreeDownloadEditInput EditInput { get; set; } = new();

        public async Task OnGetAsync(bool preview = false, CancellationToken ct = default)
        {
            await LoadDownloadsAsync(preview, ct);
        }

        public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
        {
            if (!(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
            {
                return Forbid();
            }

            var product = _catalogProductTemplateService.CreateProduct("free-download");
            if (product == null)
            {
                TempData["StatusMessage"] = "Could not create a free download.";
                return RedirectToPage(new { preview = true });
            }

            await _catalogProductService.CreateProductAsync(product, ct);
            TempData["StatusMessage"] = "Untitled download created.";
            return RedirectToPage(new { preview = true });
        }

        public async Task<IActionResult> OnGetDownloadAsync(int id, CancellationToken ct)
        {
            var product = await _catalogProductService.GetProductByIdAsync(id, ct);
            if (product == null || product.ProductType != CatalogProductType.FreeDownload)
            {
                return NotFound();
            }

            if (product.Status != CatalogProductStatus.Published && !(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
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
            if (!(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
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

            if (string.IsNullOrWhiteSpace(EditInput.Name))
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
                Downloads = await _catalogProductService.GetAdminFreeDownloadsAsync(ct);
                return Page();
            }

            var previousImageUrl = product.ImageUrl;
            var previousAttachmentUrl = product.IncludedBookingBenefitUrl;

            try
            {
                await _catalogProductService.UpdateProductAsync(new CatalogProduct
                {
                    Id = product.Id,
                    Name = EditInput.Name!.Trim(),
                    Slug = EditInput.Name,
                    Summary = EditInput.Description,
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
                    IncludedBookingBenefitLabel = EditInput.Name,
                    IncludedBookingBenefitUrl = attachmentUrl,
                    IsFeatured = product.IsFeatured,
                    SortOrder = product.SortOrder
                }, ct);
            }
            catch
            {
                await DeleteNewUploadsAsync(product, imageUrl, attachmentUrl);
                ModelState.AddModelError(string.Empty, "Save failed. Your uploaded file was not published. Please try again.");
                Downloads = await _catalogProductService.GetAdminFreeDownloadsAsync(ct);
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
            return RedirectToPage(new { preview = true });
        }

        public async Task<IActionResult> OnPostCreateImageUploadAsync(int id, string fileName, string? contentType, long fileSize, CancellationToken ct)
        {
            if (!(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
            {
                return Forbid();
            }

            return await CreateUploadResultAsync(id, CatalogProductFileKind.Image, fileName, contentType, fileSize, ct);
        }

        public async Task<IActionResult> OnPostCreateDownloadUploadAsync(int id, string fileName, string? contentType, long fileSize, CancellationToken ct)
        {
            if (!(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
            {
                return Forbid();
            }

            return await CreateUploadResultAsync(id, CatalogProductFileKind.Download, fileName, contentType, fileSize, ct);
        }

        public IActionResult OnPostVerifyImageUpload(string fileName, string? contentType, long fileSize)
        {
            if (!(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
            {
                return Forbid();
            }

            return CreateVerifyResult(CatalogProductFileKind.Image, fileName, contentType, fileSize);
        }

        public IActionResult OnPostVerifyDownloadUpload(string fileName, string? contentType, long fileSize)
        {
            if (!(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
            {
                return Forbid();
            }

            return CreateVerifyResult(CatalogProductFileKind.Download, fileName, contentType, fileSize);
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
        {
            if (!(User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
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
            return RedirectToPage(new { preview = true });
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

        private async Task LoadDownloadsAsync(bool preview, CancellationToken ct)
        {
            IsAdminEditMode = preview && (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"));
            Downloads = IsAdminEditMode
                ? await _catalogProductService.GetAdminFreeDownloadsAsync(ct)
                : await _catalogProductService.GetPublishedFreeDownloadsAsync(ct);
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
