using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services
{
    public class CatalogProductSubmissionService
    {
        private readonly IProductFormSubmissionService _productFormSubmissionService;
        private readonly IMailService _mailService;
        private readonly IOptions<MailSettings> _mailSettings;
        private readonly EmailBrandingFactory _emailBrandingFactory;

        public CatalogProductSubmissionService(
            IProductFormSubmissionService productFormSubmissionService,
            IMailService mailService,
            IOptions<MailSettings> mailSettings,
            EmailBrandingFactory emailBrandingFactory)
        {
            _productFormSubmissionService = productFormSubmissionService;
            _mailService = mailService;
            _mailSettings = mailSettings;
            _emailBrandingFactory = emailBrandingFactory;
        }

        public async Task SubmitAsync(
            CatalogProduct product,
            IDictionary<string, string> submittedValues,
            string? customerEmail,
            int orderQuantity,
            int participantCount,
            string? baseUrl = null,
            CancellationToken ct = default)
        {
            var savePayload = submittedValues.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
            savePayload["order_quantity"] = orderQuantity;
            savePayload["participant_count"] = participantCount;
            savePayload["product_slug"] = product.Slug;
            savePayload["workflow_type"] = product.WorkflowType.ToString();

            await _productFormSubmissionService.SaveFormSubmissionAsync(
                product.Name,
                savePayload,
                string.IsNullOrWhiteSpace(customerEmail) ? "unknown@local" : customerEmail);

            var model = new CatalogProductSubmissionEmailModel
            {
                Product = product,
                SubmittedValues = new Dictionary<string, string>(submittedValues),
                OrderQuantity = orderQuantity,
                ParticipantCount = participantCount,
                IntroHtml = string.IsNullOrWhiteSpace(product.OwnerNotificationBodyHtml)
                    ? BuildDefaultOwnerIntro(product)
                    : product.OwnerNotificationBodyHtml!,
                Branding = _emailBrandingFactory.Create(baseUrl)
            };
            var emailDefaults = CatalogProductEditorOptions.GetDefaultEmailDefaults(product.ProductType);

            var ownerRecipient = _mailSettings.Value.To;
            if (!string.IsNullOrWhiteSpace(ownerRecipient))
            {
                await _mailService.SendAsync(
                    "/wwwroot/templates/ProductEmailTemplate.cshtml",
                    model,
                    ownerRecipient,
                    string.IsNullOrWhiteSpace(product.OwnerNotificationSubject)
                        ? emailDefaults.OwnerSubject
                        : product.OwnerNotificationSubject,
                    _mailSettings.Value.BillingEmail,
                    ct: ct);
            }

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                model.IntroHtml = product.ConfirmationEmailBodyHtml ?? BuildDefaultCustomerIntro(product);
                await _mailService.SendAsync(
                    "/wwwroot/templates/ProductEmailTemplate.cshtml",
                    model,
                    customerEmail,
                    string.IsNullOrWhiteSpace(product.ConfirmationEmailSubject)
                        ? emailDefaults.CustomerSubject
                        : product.ConfirmationEmailSubject,
                    _mailSettings.Value.BillingEmail,
                    ct: ct);
            }
        }

        private static string BuildDefaultCustomerIntro(CatalogProduct product)
        {
            return $"<p>Thank you for your submission for <strong>{product.Name}</strong>.</p>" +
                   "<p>We have received your details and will get back to you shortly.</p>";
        }

        private static string BuildDefaultOwnerIntro(CatalogProduct product)
        {
            return $"<p>A new submission was received for <strong>{product.Name}</strong>.</p>" +
                   "<p>The details are included below.</p>";
        }
    }
}
