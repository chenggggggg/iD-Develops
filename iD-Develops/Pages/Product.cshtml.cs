using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace iD_Develops.Pages
{
    [ValidateAntiForgeryToken]
    public class ProductModel : PageModel
    {
        public const string DraftProductName = "Untitled product";
        private const string PendingCheckoutSessionKey = "Catalog.PendingCheckout";

        private readonly ICatalogProductService _catalogProductService;
        private readonly ICreditConfigurationService _creditConfigurationService;
        private readonly CatalogProductSubmissionService _catalogProductSubmissionService;
        private readonly ICatalogCheckoutService _catalogCheckoutService;
        private readonly CatalogProductAccessService _catalogProductAccessService;
        private readonly CatalogProductFileStorageService _catalogProductFileStorageService;
        private readonly IWebHostEnvironment _environment;
        private readonly ITurnstileService _turnstileService;
        private readonly IApplicationUrlService _applicationUrls;
        private readonly IPortalAuthenticationHandoffService _authenticationHandoffService;

        public ProductModel(
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
        {
            _catalogProductService = catalogProductService;
            _creditConfigurationService = creditConfigurationService;
            _catalogProductSubmissionService = catalogProductSubmissionService;
            _catalogCheckoutService = catalogCheckoutService;
            _catalogProductAccessService = catalogProductAccessService;
            _catalogProductFileStorageService = catalogProductFileStorageService;
            _environment = environment;
            _turnstileService = turnstileService;
            _applicationUrls = applicationUrls;
            _authenticationHandoffService = authenticationHandoffService;
        }

        public CatalogProduct Product { get; private set; } = null!;
        public int OrderQuantity { get; private set; } = 1;
        public int ParticipantCount { get; private set; } = 1;
        public int? SelectedVariantId { get; private set; }
        public bool IsPreviewMode { get; private set; }
        public string? AccessToken { get; private set; }
        public bool CanTakeAction => IsPreviewMode || Product.IsSalesActive;
        public bool IsAdminEditMode { get; private set; }
        public List<Course> CourseOptions { get; private set; } = new();
        public IReadOnlyList<CreditType> CreditTypeOptions { get; private set; } = Array.Empty<CreditType>();
        public IReadOnlyList<CatalogProduct> CreditProductOptions { get; private set; } = Array.Empty<CatalogProduct>();
        public IReadOnlyList<CourseClassCreditOption> CourseClassOptions { get; private set; } = Array.Empty<CourseClassCreditOption>();
        public bool UploadsEnabled => _catalogProductFileStorageService.UploadsEnabled;
        public long MaxImageBytes => _catalogProductFileStorageService.MaxImageBytes;
        public long MaxDownloadBytes => _catalogProductFileStorageService.MaxDownloadBytes;
        public string ImageAccept => _catalogProductFileStorageService.ImageAccept;
        public string DownloadAccept => _catalogProductFileStorageService.DownloadAccept;
        public string? ResolvedImageUrl { get; private set; }
        public string? ResolvedAttachmentUrl { get; private set; }
        public bool RequiresAuthenticatedAccount => Product.RequiresAuthenticatedAccount;

        [BindProperty]
        public ProductEditInput EditInput { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(
            string slug,
            string? access = null,
            int? inviteUseId = null,
            bool resumeCheckout = false,
            string? portalAccess = null,
            CancellationToken ct = default)
        {
            var isEditorRequest = IsProductEditorRequest();
            if (isEditorRequest && !CanManageProducts())
            {
                return Forbid();
            }

            var product = await LoadProductAsync(slug, isEditorRequest, ct);
            if (product == null)
            {
                return NotFound();
            }

            if (inviteUseId.HasValue)
            {
                await _catalogProductAccessService.CancelInviteUseAsync(inviteUseId.Value, ct);
                TempData["StatusMessage"] = "Your secure checkout session was released. You can try again from this page.";
            }

            Product = product;
            IsPreviewMode = isEditorRequest && product.Status != CatalogProductStatus.Published;
            IsAdminEditMode = isEditorRequest;
            AccessToken = access;

            if (!IsPreviewMode && product.RequireAccessToken)
            {
                var invite = await _catalogProductAccessService.ValidateInviteAsync(product, access, null, ct);
                if (invite == null)
                {
                    return NotFound();
                }
            }

            OrderQuantity = GetOrderQuantity(product);
            ParticipantCount = GetParticipantCount(product);
            SelectedVariantId = GetSelectedVariantId();
            EditInput = CreateEditInput(product);
            if (IsAdminEditMode)
            {
                await LoadConfigurationOptionsAsync(ct);
            }
            await ResolveProductFilesAsync(product);

            if (resumeCheckout && !IsPreviewMode)
            {
                var handoff = _authenticationHandoffService.ValidateToken(portalAccess);
                var handoffPath = handoff?.ReturnPath.Split('?', 2)[0];
                if (handoff == null ||
                    !string.Equals(handoffPath, Request.Path.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToRegistration(product, access);
                }

                return await ResumePendingCheckoutAsync(product, access, handoff.UserId, ct);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string slug, string? access = null, CancellationToken ct = default)
        {
            if (IsProductEditorRequest())
            {
                return BadRequest();
            }

            var product = await LoadProductAsync(slug, isEditorRequest: false, ct);
            if (product == null)
            {
                return NotFound();
            }

            Product = product;
            IsPreviewMode = false;
            IsAdminEditMode = false;
            AccessToken = access;
            OrderQuantity = GetOrderQuantity(product);
            ParticipantCount = GetParticipantCount(product);
            SelectedVariantId = GetSelectedVariantId();
            await ResolveProductFilesAsync(product);

            var submittedValues = CollectSubmittedValues(product, ParticipantCount);
            submittedValues["order_quantity"] = OrderQuantity.ToString();
            var customerEmail = ResolveCustomerEmail(product, submittedValues);
            CatalogProductInvite? invite = null;

            if (!IsPreviewMode && product.RequireAccessToken)
            {
                invite = await _catalogProductAccessService.ValidateInviteAsync(product, access, customerEmail, ct);
                if (invite == null)
                {
                    return NotFound();
                }
            }

            if (!IsPreviewMode && !product.IsSalesActive)
            {
                ModelState.AddModelError(string.Empty, "This offer is visible, but sales are currently paused. Please contact us if you would like to continue.");
                return Page();
            }

            if (!IsPreviewMode)
            {
                await _turnstileService.ValidateAsync(HttpContext, ModelState, ct);
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            ValidateFormBuilder(EditInput.FormFields);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (!IsPreviewMode && RequiresAuthentication(product) && !(User.Identity?.IsAuthenticated ?? false))
            {
                StorePendingCheckout(product, submittedValues, customerEmail, access);
                return RedirectToRegistration(product, access);
            }

            return await ExecuteProductActionAsync(
                product,
                submittedValues,
                customerEmail,
                invite,
                access,
                GetAuthenticatedUserId(),
                ct);
        }

        private async Task<IActionResult> ExecuteProductActionAsync(
            CatalogProduct product,
            Dictionary<string, string> submittedValues,
            string? customerEmail,
            CatalogProductInvite? invite,
            string? access,
            string? authenticatedUserId,
            CancellationToken ct)
        {

            if (product.WorkflowType == CatalogWorkflowType.FormThenStripeCheckout)
            {
                var selectedVariant = ResolveSelectedVariant(product);
                if (selectedVariant == null)
                {
                    ModelState.AddModelError(string.Empty, "Please select a product option before continuing.");
                    return Page();
                }

                submittedValues["selected_variant"] = selectedVariant.Name;
                CatalogProductInviteUse? inviteUse = null;
                if (invite != null)
                {
                    try
                    {
                        inviteUse = await _catalogProductAccessService.ReserveInviteAsync(invite, customerEmail, ct);
                    }
                    catch (InvalidOperationException ex)
                    {
                        ModelState.AddModelError(string.Empty, ex.Message);
                        return Page();
                    }
                }

                if (_environment.IsEnvironment("Local"))
                {
                    var localBaseUrl = $"{Request.Scheme}://{Request.Host}";
                    if (inviteUse != null)
                    {
                        await _catalogProductAccessService.CompleteInviteUseAsync(inviteUse.Id, customerEmail, "local_checkout", ct);
                    }

                    await _catalogProductSubmissionService.SubmitAsync(
                        product,
                        submittedValues,
                        customerEmail,
                        OrderQuantity,
                        ParticipantCount,
                        localBaseUrl,
                        ct);

                    return RedirectToPage("/PaymentResult", new { local = true, product = product.Name });
                }

                try
                {
                    var checkoutUrl = await _catalogCheckoutService.CreateCheckoutSessionAsync(
                        HttpContext,
                        product,
                        selectedVariant,
                        OrderQuantity,
                        submittedValues,
                        customerEmail,
                        inviteUse?.Id,
                        access,
                        ParticipantCount,
                        authenticatedUserId,
                        ct);

                    return Redirect(checkoutUrl);
                }
                catch
                {
                    if (inviteUse != null)
                    {
                        await _catalogProductAccessService.CancelInviteUseAsync(inviteUse.Id, ct);
                    }

                    throw;
                }
            }

            if (invite != null)
            {
                try
                {
                    await _catalogProductAccessService.ConsumeInviteAsync(invite, customerEmail, ct);
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    return Page();
                }
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _catalogProductSubmissionService.SubmitAsync(product, submittedValues, customerEmail, OrderQuantity, ParticipantCount, baseUrl, ct);

            if (product.WorkflowType is CatalogWorkflowType.FormThenExternalBooking or CatalogWorkflowType.ExternalBookingOnly &&
                !string.IsNullOrWhiteSpace(product.ExternalBookingUrl))
            {
                return Redirect(product.ExternalBookingUrl);
            }

            TempData["StatusMessage"] = "Your submission has been received.";
            return RedirectToPage(new { slug = product.Slug, access });
        }

        private async Task<IActionResult> ResumePendingCheckoutAsync(
            CatalogProduct product,
            string? access,
            string authenticatedUserId,
            CancellationToken ct)
        {
            var pending = ReadPendingCheckout();
            if (pending == null || pending.ProductId != product.Id)
            {
                TempData["StatusMessage"] = "Your checkout session expired. Please review the product and continue again.";
                return RedirectToPage(new { slug = product.Slug, access });
            }

            if (!product.IsSalesActive)
            {
                HttpContext.Session.Remove(PendingCheckoutSessionKey);
                TempData["StatusMessage"] = "Sales for this product are currently paused.";
                return RedirectToPage(new { slug = product.Slug, access });
            }

            OrderQuantity = pending.OrderQuantity;
            ParticipantCount = pending.ParticipantCount;
            SelectedVariantId = pending.SelectedVariantId;
            AccessToken = pending.AccessToken;

            CatalogProductInvite? invite = null;
            if (product.RequireAccessToken)
            {
                invite = await _catalogProductAccessService.ValidateInviteAsync(
                    product,
                    pending.AccessToken,
                    pending.CustomerEmail,
                    ct);

                if (invite == null)
                {
                    HttpContext.Session.Remove(PendingCheckoutSessionKey);
                    return NotFound();
                }
            }

            HttpContext.Session.Remove(PendingCheckoutSessionKey);
            return await ExecuteProductActionAsync(
                product,
                pending.SubmittedValues,
                pending.CustomerEmail,
                invite,
                pending.AccessToken,
                authenticatedUserId,
                ct);
        }

        private void StorePendingCheckout(
            CatalogProduct product,
            Dictionary<string, string> submittedValues,
            string? customerEmail,
            string? access)
        {
            var pending = new PendingCatalogCheckout(
                product.Id,
                product.Slug,
                OrderQuantity,
                ParticipantCount,
                SelectedVariantId,
                new Dictionary<string, string>(submittedValues, StringComparer.OrdinalIgnoreCase),
                customerEmail,
                access);

            HttpContext.Session.SetString(PendingCheckoutSessionKey, JsonSerializer.Serialize(pending));
        }

        private PendingCatalogCheckout? ReadPendingCheckout()
        {
            var json = HttpContext.Session.GetString(PendingCheckoutSessionKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PendingCatalogCheckout>(json);
            }
            catch (JsonException)
            {
                HttpContext.Session.Remove(PendingCheckoutSessionKey);
                return null;
            }
        }

        private IActionResult RedirectToRegistration(CatalogProduct product, string? access)
        {
            var culture = RouteData.Values["culture"]?.ToString() ?? "en-us";
            var publicReturnPath = Url.RouteUrl(
                ApplicationHostPageRouteModelConvention.PublicProductRouteName,
                new { culture, slug = product.Slug, access, resumeCheckout = true })
                ?? $"/{culture}/products/{Uri.EscapeDataString(product.Slug)}?resumeCheckout=true";
            var continuePath = QueryHelpers.AddQueryString("/auth/continue", "returnPath", publicReturnPath);
            var registrationPath = QueryHelpers.AddQueryString("/register", "returnUrl", continuePath);

            return Redirect(_applicationUrls.PortalUrl(registrationPath));
        }

        private string? GetAuthenticatedUserId()
            => User.Identity?.IsAuthenticated == true
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

        private static bool RequiresAuthentication(CatalogProduct product)
            => product.RequiresAuthenticatedAccount;

        private sealed record PendingCatalogCheckout(
            int ProductId,
            string ProductSlug,
            int OrderQuantity,
            int ParticipantCount,
            int? SelectedVariantId,
            Dictionary<string, string> SubmittedValues,
            string? CustomerEmail,
            string? AccessToken);

        public async Task<IActionResult> OnPostPublishAsync(string slug, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            var product = await _catalogProductService.GetProductBySlugAsync(slug, ct);
            if (product == null)
            {
                return NotFound();
            }

            if (product.Status != CatalogProductStatus.Published)
            {
                ValidateExistingProductForPublish(product);
                if (!ModelState.IsValid)
                {
                    TempData["StatusMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                    return RedirectToEditor(product.Slug);
                }

                await SavePrimaryVariantSettingsAsync(product, ct);
                product = await _catalogProductService.GetProductBySlugAsync(slug, ct) ?? product;

                product.Status = CatalogProductStatus.Published;
                await _catalogProductService.UpdateProductAsync(product, ct);
            }

            TempData["StatusMessage"] = "Product published successfully.";
            return RedirectToEditor(product.Slug);
        }

        public async Task<IActionResult> OnPostSetStatusAsync(string slug, CatalogProductStatus status, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            if (!Enum.IsDefined(status))
            {
                return BadRequest();
            }

            var product = await _catalogProductService.GetProductBySlugAsync(slug, ct);
            if (product == null)
            {
                return NotFound();
            }

            if (status == CatalogProductStatus.Published &&
                product.ProductType == CatalogProductType.FreeDownload &&
                !_catalogProductFileStorageService.Exists(product.IncludedBookingBenefitUrl))
            {
                TempData["StatusMessage"] = "Attach an existing download file before publishing this free download.";
                return RedirectToEditor(product.Slug);
            }

            if (status == CatalogProductStatus.Published)
            {
                ValidateExistingProductForPublish(product);
                if (!ModelState.IsValid)
                {
                    TempData["StatusMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                    return RedirectToEditor(product.Slug);
                }

                await SavePrimaryVariantSettingsAsync(product, ct);
                product = await _catalogProductService.GetProductBySlugAsync(slug, ct) ?? product;
            }

            product.Status = status;
            await _catalogProductService.UpdateProductAsync(product, ct);

            TempData["StatusMessage"] = status switch
            {
                CatalogProductStatus.Draft => "Product moved to draft.",
                CatalogProductStatus.Published => "Product published.",
                CatalogProductStatus.Archived => "Product archived.",
                _ => "Product status updated."
            };

            return RedirectToEditor(product.Slug);
        }

        public async Task<IActionResult> OnPostSaveContentAsync(
            string slug,
            CatalogProductStatus? targetStatus = null,
            CancellationToken ct = default)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            var product = await _catalogProductService.GetProductBySlugAsync(slug, ct);
            if (product == null)
            {
                return NotFound();
            }

            Product = product;
            IsPreviewMode = product.Status != CatalogProductStatus.Published;
            IsAdminEditMode = true;
            OrderQuantity = GetOrderQuantity(product);
            ParticipantCount = GetParticipantCount(product);
            SelectedVariantId = GetSelectedVariantId();
            await LoadConfigurationOptionsAsync(ct);
            await ResolveProductFilesAsync(product);

            if (EditInput.GrantedCourseId.HasValue &&
                CourseOptions.All(course => course.Id != EditInput.GrantedCourseId.Value))
            {
                ModelState.AddModelError(nameof(EditInput.GrantedCourseId), "Select an existing course.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var requestedStatus = targetStatus ?? product.Status;
            ValidateProductSave(product, requestedStatus);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var imageUrl = string.IsNullOrWhiteSpace(EditInput.ImageObjectKey)
                ? product.ImageUrl
                : EditInput.ImageObjectKey.Trim();

            var attachmentUrl = string.IsNullOrWhiteSpace(EditInput.AttachmentObjectKey)
                ? product.IncludedBookingBenefitUrl
                : EditInput.AttachmentObjectKey.Trim();

            var requiresAttachment = product.ProductType == CatalogProductType.FreeDownload;
            if (requiresAttachment && !_catalogProductFileStorageService.Exists(attachmentUrl))
            {
                ModelState.AddModelError(string.Empty, "Choose an existing file before saving this free download.");
                return Page();
            }

            if (!string.IsNullOrWhiteSpace(attachmentUrl) && !_catalogProductFileStorageService.Exists(attachmentUrl))
            {
                ModelState.AddModelError(string.Empty, "The attached file does not exist. Choose a different file before saving.");
                return Page();
            }

            var previousImageUrl = product.ImageUrl;
            var previousAttachmentUrl = product.IncludedBookingBenefitUrl;
            try
            {
                await _catalogProductService.UpdateProductWithCreditConfigurationAsync(new CatalogProduct
                {
                    Id = product.Id,
                    Name = string.IsNullOrWhiteSpace(EditInput.Name) ? DraftProductName : EditInput.Name,
                    Slug = product.Slug,
                    Summary = EditInput.Summary,
                    Description = EditInput.Description,
                    FullDescriptionHtml = EditInput.FullDescriptionHtml,
                    ImageUrl = imageUrl,
                    ProductType = product.ProductType,
                    WorkflowType = product.WorkflowType,
                    Status = requestedStatus,
                    IsSalesActive = product.IsSalesActive,
                    RequireAccessToken = product.RequireAccessToken,
                    Currency = product.Currency,
                    BasePrice = EditInput.BasePrice,
                    VatPercentage = EditInput.VatPercentage,
                    RegistrationFee = EditInput.RegistrationFee,
                    TransactionFee = EditInput.TransactionFee,
                    ServiceFee = EditInput.ServiceFee,
                    ExternalBookingUrl = EditInput.ExternalBookingUrl,
                    ExternalBookingButtonText = EditInput.ExternalBookingButtonText,
                    ConfirmationEmailSubject = product.ConfirmationEmailSubject,
                    ConfirmationEmailBodyHtml = product.ConfirmationEmailBodyHtml,
                    OwnerNotificationSubject = product.OwnerNotificationSubject,
                    OwnerNotificationBodyHtml = product.OwnerNotificationBodyHtml,
                    AllowsMultipleParticipants = product.AllowsMultipleParticipants,
                    MaxParticipants = product.MaxParticipants,
                    HideFromProductsPage = EditInput.HideFromProductsPage,
                    EnableQuantity = product.EnableQuantity,
                    MinQuantity = product.MinQuantity,
                    MaxQuantity = product.MaxQuantity,
                    RequiresAccountCreation = product.RequiresAccountCreation,
                    GrantedCourseId = EditInput.GrantedCourseId,
                    CreditConsumptionPolicyId = product.CreditConsumptionPolicyId,
                    IncludedBookingBenefitLabel = ResolveStoredFileLabel(attachmentUrl),
                    IncludedBookingBenefitUrl = attachmentUrl,
                    IsFeatured = product.IsFeatured,
                    SortOrder = product.SortOrder
                }, product.ProductType == CatalogProductType.Credit ? EditInput.CreditConfiguration : null,
                    EditInput.IncludedCreditProductIds, ct);

                await SavePrimaryVariantSettingsAsync(product, ct);
                await SaveFormFieldsAsync(product, ct);
            }
            catch
            {
                if (!string.Equals(imageUrl, previousImageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await TryDeleteAsync(imageUrl);
                }

                if (!string.Equals(attachmentUrl, previousAttachmentUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await TryDeleteAsync(attachmentUrl);
                }

                ModelState.AddModelError(string.Empty, "Save failed. Your uploaded file was not published. Please try again.");
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

            var savedProduct = await _catalogProductService.GetProductByIdAsync(product.Id, ct) ?? product;
            TempData["StatusMessage"] = requestedStatus == CatalogProductStatus.Published
                ? "Product published."
                : "Product updated.";
            return RedirectToEditor(savedProduct.Slug);
        }

        private void ValidateProductSave(CatalogProduct product, CatalogProductStatus requestedStatus)
        {
            if (!HasAnyEditableContent(product))
            {
                ModelState.AddModelError(string.Empty, "Add at least one product detail before saving.");
            }

            if (product.ProductType == CatalogProductType.Credit)
            {
                ValidateCreditProductConfiguration();
            }

            if (requestedStatus != CatalogProductStatus.Published)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(EditInput.Name) || IsDraftPlaceholderName(EditInput.Name))
            {
                ModelState.AddModelError(nameof(EditInput.Name), "Add a product title before publishing.");
            }

            if (product.ProductType == CatalogProductType.Booking &&
                !IsValidKoalendarUrl(EditInput.ExternalBookingUrl))
            {
                ModelState.AddModelError(nameof(EditInput.ExternalBookingUrl), "Add a valid Koalendar URL before publishing.");
            }

            if (product.ProductType == CatalogProductType.Signup &&
                !EditInput.FormFields.Any(IsRealSignupInput))
            {
                ModelState.AddModelError(string.Empty, "Add at least one signup form input before publishing.");
            }

        }

        private void ValidateCreditProductConfiguration()
        {
            var input = EditInput.CreditConfiguration;
            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError("EditInput.CreditConfiguration.Name", "Enter a credit name.");
            if (string.IsNullOrWhiteSpace(input.SingularLabel) || string.IsNullOrWhiteSpace(input.PluralLabel))
                ModelState.AddModelError(string.Empty, "Enter both the singular and plural credit labels.");
            if (input.Quantity is < 1 or > 100000)
                ModelState.AddModelError("EditInput.CreditConfiguration.Quantity", "Credits included must be between 1 and 100,000.");
            if ((input.ValidityValue.HasValue) != (input.ValidityUnit.HasValue) || input.ValidityValue is <= 0)
                ModelState.AddModelError(string.Empty, "Set both a positive validity duration and unit, or leave both empty.");
            if (input.CancellationWindowHours is < 0 or > 8760)
                ModelState.AddModelError("EditInput.CreditConfiguration.CancellationWindowHours", "The cancellation window must be between 0 and 8,760 hours.");
            if (input.Scope == CreditGrantScope.Course && !input.CourseId.HasValue)
                ModelState.AddModelError("EditInput.CreditConfiguration.CourseId", "Select the course where this credit can be used.");
            if (input.Scope == CreditGrantScope.CourseClass && !input.CourseClassId.HasValue)
                ModelState.AddModelError("EditInput.CreditConfiguration.CourseClassId", "Select the class where this credit can be used.");
        }

        private bool HasAnyEditableContent(CatalogProduct product)
            => !string.IsNullOrWhiteSpace(EditInput.Name) && !IsDraftPlaceholderName(EditInput.Name) ||
               !string.IsNullOrWhiteSpace(EditInput.Summary) ||
               !string.IsNullOrWhiteSpace(EditInput.Description) ||
               !string.IsNullOrWhiteSpace(EditInput.FullDescriptionHtml) ||
               EditInput.BasePrice.HasValue && EditInput.BasePrice.Value > 0 ||
               EditInput.VatPercentage.HasValue && EditInput.VatPercentage.Value > 0 ||
               EditInput.RegistrationFee.HasValue && EditInput.RegistrationFee.Value > 0 ||
               EditInput.TransactionFee.HasValue && EditInput.TransactionFee.Value > 0 ||
               EditInput.ServiceFee.HasValue && EditInput.ServiceFee.Value > 0 ||
               EditInput.GrantedCourseId.HasValue ||
               !string.IsNullOrWhiteSpace(EditInput.CreditConfiguration.Name) ||
               !string.IsNullOrWhiteSpace(EditInput.ExternalBookingUrl) ||
               !string.IsNullOrWhiteSpace(EditInput.ImageObjectKey) ||
               !string.IsNullOrWhiteSpace(EditInput.AttachmentObjectKey) ||
               product.FormFields.Any() ||
               EditInput.FormFields.Any(f => !string.IsNullOrWhiteSpace(f.Label) ||
                                             !string.IsNullOrWhiteSpace(f.HelpText) ||
                                             !string.IsNullOrWhiteSpace(f.OptionsText));

        private static bool IsRealSignupInput(ProductFormFieldInput field)
            => field.FieldType != CatalogFormFieldType.Note &&
               !string.IsNullOrWhiteSpace(field.Label);

        private static bool IsValidKoalendarUrl(string? value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               uri.Host.Contains("koalendar.com", StringComparison.OrdinalIgnoreCase);

        private void ValidateExistingProductForPublish(CatalogProduct product)
        {
            if (string.IsNullOrWhiteSpace(product.Name) || IsDraftPlaceholderName(product.Name))
            {
                ModelState.AddModelError(string.Empty, "Add a product title before publishing.");
            }

            if (product.ProductType == CatalogProductType.Booking &&
                !IsValidKoalendarUrl(product.ExternalBookingUrl))
            {
                ModelState.AddModelError(string.Empty, "Add a valid Koalendar URL before publishing.");
            }

            if (product.ProductType == CatalogProductType.Signup &&
                !product.FormFields.Any(f => f.FieldType != CatalogFormFieldType.Note && !string.IsNullOrWhiteSpace(f.Label)))
            {
                ModelState.AddModelError(string.Empty, "Add at least one signup form input before publishing.");
            }
        }

        public async Task<IActionResult> OnPostCreateImageUploadAsync(string slug, string fileName, string? contentType, long fileSize, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return await CreateUploadResultAsync(slug, CatalogProductFileKind.Image, fileName, contentType, fileSize, ct);
        }

        public async Task<IActionResult> OnPostCreateAttachmentUploadAsync(string slug, string fileName, string? contentType, long fileSize, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return await CreateUploadResultAsync(slug, CatalogProductFileKind.Download, fileName, contentType, fileSize, ct);
        }

        public IActionResult OnPostVerifyImageUpload(string fileName, string? contentType, long fileSize)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return CreateVerifyResult(CatalogProductFileKind.Image, fileName, contentType, fileSize);
        }

        public IActionResult OnPostVerifyAttachmentUpload(string fileName, string? contentType, long fileSize)
        {
            if (!CanManageProducts())
            {
                return Forbid();
            }

            return CreateVerifyResult(CatalogProductFileKind.Download, fileName, contentType, fileSize);
        }

        private async Task SavePrimaryVariantSettingsAsync(CatalogProduct product, CancellationToken ct)
        {
            if (product.WorkflowType != CatalogWorkflowType.FormThenStripeCheckout)
            {
                return;
            }

            var variant = product.Variants
                .OrderByDescending(v => v.IsDefault)
                .ThenBy(v => v.SortOrder)
                .FirstOrDefault();

            var variantName = string.IsNullOrWhiteSpace(EditInput.PrimaryVariantName)
                ? "Checkout"
                : EditInput.PrimaryVariantName.Trim();

            var price = CalculateCheckoutAmount(
                EditInput.BasePrice ?? product.BasePrice,
                EditInput.RegistrationFee ?? product.RegistrationFee,
                EditInput.TransactionFee ?? product.TransactionFee,
                EditInput.ServiceFee ?? product.ServiceFee,
                EditInput.VatPercentage ?? product.VatPercentage);

            if (variant == null)
            {
                await _catalogProductService.AddVariantAsync(product.Id, new CatalogProductVariant
                {
                    Name = variantName,
                    Price = price,
                    Currency = product.Currency,
                    StripePriceId = null,
                    IsDefault = true,
                    IsActive = true,
                    SortOrder = 10
                }, ct);
                return;
            }

            await _catalogProductService.UpdateVariantAsync(product.Id, new CatalogProductVariant
            {
                Id = variant.Id,
                CatalogProductId = product.Id,
                Name = variantName,
                Price = price,
                Currency = product.Currency,
                StripePriceId = null,
                IsDefault = true,
                IsActive = variant.IsActive,
                SortOrder = variant.SortOrder
            }, ct);
        }

        private static decimal CalculateCheckoutAmount(
            decimal? basePrice,
            decimal? registrationFee,
            decimal? transactionFee,
            decimal? serviceFee,
            decimal? vatPercentage)
        {
            var subtotal = NormalizeMoney(basePrice) +
                           NormalizeMoney(registrationFee) +
                           NormalizeMoney(transactionFee) +
                           NormalizeMoney(serviceFee);
            var vat = Math.Clamp(NormalizeMoney(vatPercentage), 0m, 100m);
            return Math.Round(subtotal + subtotal * (vat / 100m), 2, MidpointRounding.AwayFromZero);
        }

        private static decimal NormalizeMoney(decimal? value)
            => value.HasValue ? Math.Max(0m, Math.Round(value.Value, 2, MidpointRounding.AwayFromZero)) : 0m;

        private async Task SaveFormFieldsAsync(CatalogProduct product, CancellationToken ct)
        {
            var submittedFields = EditInput.FormFields
                .Where(f => !string.IsNullOrWhiteSpace(f.Label) || f.FieldType == CatalogFormFieldType.Note)
                .Select((field, index) =>
                {
                    field.SortOrder = index * 10;
                    return field;
                })
                .ToList();

            var submittedIds = submittedFields
                .Where(f => f.Id > 0)
                .Select(f => f.Id)
                .ToHashSet();

            foreach (var existing in product.FormFields.Where(f => !submittedIds.Contains(f.Id)).ToList())
            {
                await _catalogProductService.DeleteFieldAsync(product.Id, existing.Id, ct);
            }

            foreach (var field in submittedFields)
            {
                var fieldType = NormalizeEditableFieldType(field.FieldType);
                var model = new CatalogProductFormField
                {
                    Id = field.Id,
                    CatalogProductId = product.Id,
                    Key = field.Key,
                    Label = string.IsNullOrWhiteSpace(field.Label) ? "Note" : field.Label.Trim(),
                    Placeholder = field.Placeholder,
                    HelpText = field.HelpText,
                    FieldType = fieldType,
                    IsRequired = fieldType != CatalogFormFieldType.Note && field.IsRequired,
                    IsPerParticipant = false,
                    CharacterLimit = field.CharacterLimit,
                    ListItemCount = field.ListItemCount,
                    AllowMultipleOptions = field.AllowMultipleOptions,
                    SortOrder = field.SortOrder,
                    OptionsText = field.OptionsText
                };

                if (field.Id > 0)
                {
                    await _catalogProductService.UpdateFieldAsync(product.Id, model, ct);
                }
                else
                {
                    await _catalogProductService.AddFieldAsync(product.Id, model, ct);
                }
            }
        }

        private static CatalogFormFieldType NormalizeEditableFieldType(CatalogFormFieldType fieldType)
            => fieldType is CatalogFormFieldType.Select or CatalogFormFieldType.CheckboxWithText
                ? CatalogFormFieldType.Text
                : fieldType;

        private void ValidateFormBuilder(IReadOnlyList<ProductFormFieldInput> fields)
        {
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                var fieldType = NormalizeEditableFieldType(field.FieldType);
                var label = string.IsNullOrWhiteSpace(field.Label) ? $"Form item {index + 1}" : field.Label.Trim();

                if (fieldType != CatalogFormFieldType.Note && string.IsNullOrWhiteSpace(field.Label))
                {
                    ModelState.AddModelError(string.Empty, $"Form item {index + 1}: add a question.");
                }

                if (fieldType == CatalogFormFieldType.Note && string.IsNullOrWhiteSpace(field.HelpText))
                {
                    ModelState.AddModelError(string.Empty, $"Form item {index + 1}: add note text.");
                }

                if (fieldType == CatalogFormFieldType.Checkbox &&
                    string.IsNullOrWhiteSpace(field.OptionsText))
                {
                    ModelState.AddModelError(string.Empty, $"{label}: add at least one checkbox option.");
                }

                if (fieldType == CatalogFormFieldType.NumberedList && (!field.ListItemCount.HasValue || field.ListItemCount.Value < 2))
                {
                    ModelState.AddModelError(string.Empty, $"{label}: list needs at least 2 items.");
                }

                if (field.CharacterLimit.HasValue && field.CharacterLimit.Value < 1)
                {
                    ModelState.AddModelError(string.Empty, $"{label}: character limit must be higher than 0.");
                }
            }
        }

        private async Task<IActionResult> CreateUploadResultAsync(
            string slug,
            CatalogProductFileKind kind,
            string? fileName,
            string? contentType,
            long fileSize,
            CancellationToken ct)
        {
            try
            {
                var product = await _catalogProductService.GetProductBySlugAsync(slug, ct);
                if (product == null || product.ProductType == CatalogProductType.FreeDownload)
                {
                    return NotFound();
                }

                var upload = await _catalogProductFileStorageService.CreateUploadAsync(
                    kind,
                    CatalogProductFileScope.Product,
                    product.Id,
                    fileName,
                    contentType,
                    fileSize);

                return new JsonResult(new
                {
                    success = true,
                    objectKey = upload.ObjectKey,
                    putUrl = upload.PutUrl,
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

        private async Task TryDeleteAsync(string? reference)
        {
            try
            {
                await _catalogProductFileStorageService.DeleteAsync(reference);
            }
            catch
            {
                // Storage cleanup should not hide the product save outcome.
            }
        }

        protected virtual bool IsProductEditorRequest()
            => false;

        private bool CanManageProducts()
            => IsProductEditorRequest() &&
               (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"));

        private IActionResult RedirectToEditor(string slug)
            => RedirectToRoute(
                ApplicationHostPageRouteModelConvention.PortalProductEditRouteName,
                new { slug });

        private async Task<CatalogProduct?> LoadProductAsync(string slug, bool isEditorRequest, CancellationToken ct)
        {
            if (isEditorRequest && CanManageProducts())
            {
                return await _catalogProductService.GetProductBySlugAsync(slug, ct);
            }

            return await _catalogProductService.GetPublishedProductBySlugAsync(slug, ct);
        }

        private async Task ResolveProductFilesAsync(CatalogProduct product)
        {
            ResolvedImageUrl = await _catalogProductFileStorageService.ResolvePublicAssetUrlAsync(product.ImageUrl);
            ResolvedAttachmentUrl = await _catalogProductFileStorageService.ResolveReadUrlAsync(product.IncludedBookingBenefitUrl);
        }

        private Dictionary<string, string> CollectSubmittedValues(CatalogProduct product, int participantCount)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in product.FormFields.OrderBy(f => f.SortOrder).ThenBy(f => f.Label))
            {
                var requestKey = GetFieldName(field, null);
                var value = ReadFieldValue(field, requestKey);
                ValidateField(field, value, null);
                values[requestKey] = value;
            }

            return values;
        }

        private void ValidateField(CatalogProductFormField field, string value, int? participantIndex)
        {
            if (field.FieldType == CatalogFormFieldType.Note)
            {
                return;
            }

            if (!field.IsRequired)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                var prefix = participantIndex.HasValue ? $"Participant {participantIndex.Value}: " : string.Empty;
                ModelState.AddModelError(string.Empty, $"{prefix}{field.Label} is required.");
            }
        }

        private string ReadFieldValue(CatalogProductFormField field, string requestKey)
        {
            if (field.FieldType == CatalogFormFieldType.Checkbox)
            {
                var selectedValues = new List<string>();
                for (var optionIndex = 0; optionIndex < field.Options.Count; optionIndex++)
                {
                    var parts = field.Options[optionIndex].Split('\t');
                    var optionKind = parts.Length > 0 ? parts[0] : "plain";
                    var optionLabel = parts.Length > 1 ? parts[1] : field.Options[optionIndex];
                    var optionKey = field.AllowMultipleOptions ? $"{requestKey}_{optionIndex}" : requestKey;
                    var submitted = Request.Form[optionKey].ToString();
                    if (string.IsNullOrWhiteSpace(submitted))
                    {
                        continue;
                    }

                    if (optionKind == "text")
                    {
                        var text = Request.Form[$"{requestKey}_{optionIndex}_text"].ToString().Trim();
                        selectedValues.Add(string.IsNullOrWhiteSpace(text) ? optionLabel : $"{optionLabel}: {text}");
                    }
                    else
                    {
                        selectedValues.Add(optionLabel);
                    }
                }

                return string.Join(Environment.NewLine, selectedValues);
            }

            if (field.FieldType == CatalogFormFieldType.NumberedList)
            {
                var itemCount = Math.Max(2, field.ListItemCount ?? 2);
                var values = Enumerable.Range(0, itemCount)
                    .Select(index => Request.Form[$"{requestKey}_{index}"].ToString().Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value));

                return string.Join(Environment.NewLine, values);
            }

            if (field.FieldType == CatalogFormFieldType.Note)
            {
                return string.Empty;
            }

            return Request.Form[requestKey].ToString().Trim();
        }

        private string? ResolveCustomerEmail(CatalogProduct product, IDictionary<string, string> submittedValues)
        {
            var emailField = product.FormFields
                .OrderBy(f => f.SortOrder)
                .FirstOrDefault(f => f.FieldType == CatalogFormFieldType.Email);

            if (emailField == null)
            {
                return null;
            }

            var emailKey = GetFieldName(emailField, null);

            return submittedValues.TryGetValue(emailKey, out var email) && !string.IsNullOrWhiteSpace(email)
                ? email
                : null;
        }

        private CatalogProductVariant? ResolveSelectedVariant(CatalogProduct product)
        {
            if (!SelectedVariantId.HasValue)
            {
                return product.Variants.FirstOrDefault(v => v.IsDefault && v.IsActive)
                    ?? product.Variants.FirstOrDefault(v => v.IsActive);
            }

            return product.Variants.FirstOrDefault(v => v.Id == SelectedVariantId.Value && v.IsActive);
        }

        private int GetParticipantCount(CatalogProduct product)
        {
            return 1;
        }

        private int GetOrderQuantity(CatalogProduct product)
        {
            return 1;
        }

        private int? GetSelectedVariantId()
        {
            return int.TryParse(Request.HasFormContentType ? Request.Form["selectedVariantId"] : Request.Query["selectedVariantId"], out var id)
                ? id
                : null;
        }

        public static string GetFieldName(CatalogProductFormField field, int? participantIndex)
            => participantIndex.HasValue
                ? $"participant_{participantIndex.Value}_{field.Key}"
                : field.Key;

        public static bool IsDraftPlaceholderName(string? name)
            => string.Equals(name, DraftProductName, StringComparison.Ordinal);

        private static ProductEditInput CreateEditInput(CatalogProduct product)
        {
            var primaryVariant = product.Variants
                .OrderByDescending(v => v.IsDefault)
                .ThenBy(v => v.SortOrder)
                .FirstOrDefault();

            return new()
            {
                Name = IsDraftPlaceholderName(product.Name) ? string.Empty : product.Name,
                Summary = product.Summary,
                Description = product.Description,
                FullDescriptionHtml = product.FullDescriptionHtml,
                ImageObjectKey = product.ImageUrl,
                AttachmentObjectKey = product.IncludedBookingBenefitUrl,
                BasePrice = primaryVariant?.Price ?? product.BasePrice,
                VatPercentage = product.VatPercentage,
                RegistrationFee = product.RegistrationFee,
                TransactionFee = product.TransactionFee,
                ServiceFee = product.ServiceFee,
                PrimaryVariantName = primaryVariant?.Name,
                ExternalBookingUrl = product.ExternalBookingUrl,
                ExternalBookingButtonText = product.ExternalBookingButtonText,
                HideFromProductsPage = product.HideFromProductsPage,
                GrantedCourseId = product.GrantedCourseId,
                CreditConfiguration = CreateCreditConfigurationInput(product),
                IncludedCreditProductIds = product.IncludedCreditProducts
                    .Select(inclusion => inclusion.IncludedCreditProductId)
                    .ToList(),
                FormFields = product.FormFields
                    .OrderBy(f => f.SortOrder)
                    .ThenBy(f => f.Label)
                    .Select(f => new ProductFormFieldInput
                    {
                        Id = f.Id,
                        Key = f.Key,
                        Label = f.Label,
                        Placeholder = f.Placeholder,
                        HelpText = f.HelpText,
                        FieldType = f.FieldType,
                        IsRequired = f.IsRequired,
                        IsPerParticipant = false,
                        CharacterLimit = f.CharacterLimit,
                        ListItemCount = f.ListItemCount,
                        AllowMultipleOptions = f.AllowMultipleOptions,
                        SortOrder = f.SortOrder,
                        OptionsText = f.OptionsText
                    })
                    .ToList()
            };
        }

        private static CreditProductConfigurationInput CreateCreditConfigurationInput(CatalogProduct product)
        {
            var grant = product.CreditGrants.OrderBy(item => item.Id).FirstOrDefault();
            var creditType = grant?.CreditType;
            var policy = product.CreditConsumptionPolicy;
            return new CreditProductConfigurationInput
            {
                CreditTypeId = creditType?.Id ?? 0,
                CreditConsumptionPolicyId = policy?.Id ?? 0,
                Name = creditType?.Name ?? (IsDraftPlaceholderName(product.Name) ? string.Empty : product.Name),
                Description = creditType?.Description,
                SingularLabel = creditType?.SingularLabel ?? "credit",
                PluralLabel = creditType?.PluralLabel ?? "credits",
                Quantity = grant?.Quantity ?? 1,
                ValidityValue = grant?.ValidityValue ?? creditType?.DefaultValidityValue,
                ValidityUnit = grant?.ValidityUnit ?? creditType?.DefaultValidityUnit,
                Scope = grant?.Scope ?? CreditGrantScope.Global,
                CourseId = grant?.CourseId,
                CourseClassId = grant?.CourseClassId,
                ConsumptionTiming = policy?.ConsumptionTiming ?? CreditConsumptionTiming.OnBooking,
                CancellationWindowHours = policy?.CancellationWindowHours ?? 24,
                AttendedAction = policy?.AttendedAction ?? CreditResolutionAction.Consume,
                NoShowAction = policy?.NoShowAction ?? CreditResolutionAction.Consume,
                EarlyCancellationAction = policy?.EarlyCancellationAction ?? CreditResolutionAction.Return,
                LateCancellationAction = policy?.LateCancellationAction ?? CreditResolutionAction.Consume,
                StaffCancellationAction = policy?.StaffCancellationAction ?? CreditResolutionAction.Return
            };
        }

        private async Task LoadConfigurationOptionsAsync(CancellationToken cancellationToken)
        {
            CourseOptions = await _catalogProductService.GetCourseOptionsAsync(cancellationToken);
            CreditTypeOptions = await _creditConfigurationService.GetCreditTypeOptionsAsync(true, cancellationToken);
            CreditProductOptions = await _catalogProductService.GetCreditProductOptionsAsync(Product.Id, cancellationToken);
            CourseClassOptions = await _creditConfigurationService.GetCourseClassOptionsAsync(cancellationToken);
        }

        private static string? ResolveStoredFileLabel(string? objectKey)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                return null;
            }

            var fileName = Path.GetFileName(objectKey.Replace('\\', '/'));
            var dashIndex = fileName.IndexOf('-');
            return dashIndex >= 0 && dashIndex + 1 < fileName.Length
                ? fileName[(dashIndex + 1)..]
                : fileName;
        }

        public class ProductEditInput
        {
            [MaxLength(200)]
            public string? Name { get; set; }

            [MaxLength(150)]
            public string? Summary { get; set; }

            public string? Description { get; set; }
            public string? FullDescriptionHtml { get; set; }

            [MaxLength(500)]
            public string? ImageObjectKey { get; set; }

            [MaxLength(500)]
            public string? AttachmentObjectKey { get; set; }

            public decimal? BasePrice { get; set; }

            public decimal? VatPercentage { get; set; }

            public decimal? RegistrationFee { get; set; }

            public decimal? TransactionFee { get; set; }

            public decimal? ServiceFee { get; set; }

            [MaxLength(150)]
            public string? PrimaryVariantName { get; set; }

            [MaxLength(500)]
            public string? ExternalBookingUrl { get; set; }

            [MaxLength(100)]
            public string? ExternalBookingButtonText { get; set; }

            public bool HideFromProductsPage { get; set; }

            public int? GrantedCourseId { get; set; }

            public CreditProductConfigurationInput CreditConfiguration { get; set; } = new();

            public List<int> IncludedCreditProductIds { get; set; } = new();

            public List<ProductFormFieldInput> FormFields { get; set; } = new();
        }

        public class ProductFormFieldInput
        {
            public int Id { get; set; }

            [MaxLength(100)]
            public string? Key { get; set; }

            [MaxLength(150)]
            public string? Label { get; set; }

            [MaxLength(150)]
            public string? Placeholder { get; set; }

            [MaxLength(300)]
            public string? HelpText { get; set; }

            public CatalogFormFieldType FieldType { get; set; } = CatalogFormFieldType.Text;

            public bool IsRequired { get; set; }

            public bool IsPerParticipant { get; set; }

            public int? CharacterLimit { get; set; }

            public int? ListItemCount { get; set; }

            public bool AllowMultipleOptions { get; set; } = true;

            public int SortOrder { get; set; }

            public string? OptionsText { get; set; }
        }
    }
}
