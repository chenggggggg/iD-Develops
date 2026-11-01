using iD_Develops.Enums;
using iD_Develops.Configuration;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.Extensions.Options;

namespace iD_Develops.Services
{
    public class CatalogProductSubmissionService
    {
        private readonly IProductFormSubmissionService _productFormSubmissionService;
        private readonly IOptions<MailSettings> _mailSettings;
        private readonly EmailBrandingFactory _emailBrandingFactory;

        public CatalogProductSubmissionService(
            IProductFormSubmissionService productFormSubmissionService,
            IOptions<MailSettings> mailSettings,
            EmailBrandingFactory emailBrandingFactory)
        {
            _productFormSubmissionService = productFormSubmissionService;
            _mailSettings = mailSettings;
            _emailBrandingFactory = emailBrandingFactory;
        }

        public bool SignupNotificationsConfigured => SignupPersonalDetails.IsValidEmail(_mailSettings.Value.To?.Trim());

        public async Task SubmitAsync(
            CatalogProduct product,
            IDictionary<string, string> submittedValues,
            string? customerEmail,
            int orderQuantity,
            int participantCount,
            string? baseUrl = null,
            CancellationToken ct = default,
            string? requestKey = null)
        {
            if (product.ProductType == CatalogProductType.Signup)
            {
                if (!submittedValues.TryGetValue(SignupPersonalDetails.FullNameKey, out var name) ||
                    !SignupPersonalDetails.IsValidName(name) || !SignupPersonalDetails.IsValidEmail(customerEmail) ||
                    !submittedValues.TryGetValue(SignupPersonalDetails.EmailKey, out var email) || email != customerEmail)
                    throw new ArgumentException("Signup requires a valid full name and email address.");
                if (SignupPersonalDetails.OptionalFields.Any(item =>
                        submittedValues.TryGetValue(item.Key, out var value) && !SignupPersonalDetails.IsValidOptionalValue(value, item.MaxLength)))
                    throw new ArgumentException("Signup personal details contain an invalid value.");
                if (!SignupNotificationsConfigured)
                    throw new InvalidOperationException("Signup requires a configured company notification recipient.");
            }
            requestKey ??= Guid.NewGuid().ToString("N");
            var notifications = CreateCheckoutNotifications(product, submittedValues, customerEmail, orderQuantity,
                participantCount, baseUrl, "signup:" + requestKey).ToArray();
            await _productFormSubmissionService.SaveWithNotificationsAsync(product.Name,
                BuildSubmissionPayload(product, submittedValues, orderQuantity, participantCount),
                string.IsNullOrWhiteSpace(customerEmail) ? "unknown@local" : customerEmail, requestKey, notifications, ct);
        }

        public Task SaveSubmissionAsync(CatalogProduct product, IDictionary<string, string> submittedValues,
            string? customerEmail, int orderQuantity, int participantCount)
        {
            return _productFormSubmissionService.SaveFormSubmissionAsync(product.Name,
                BuildSubmissionPayload(product, submittedValues, orderQuantity, participantCount),
                string.IsNullOrWhiteSpace(customerEmail) ? "unknown@local" : customerEmail);
        }

        private static Dictionary<string, object> BuildSubmissionPayload(CatalogProduct product,
            IDictionary<string, string> values, int quantity, int participants)
        {
            var payload = values.ToDictionary(value => value.Key, value => (object)value.Value);
            payload["order_quantity"] = quantity;
            payload["participant_count"] = participants;
            payload["product_slug"] = product.Slug;
            payload["workflow_type"] = product.WorkflowType.ToString();
            return payload;
        }

        public IEnumerable<CatalogCheckoutMail> CreateCheckoutNotifications(CatalogProduct product,
            IDictionary<string, string> submittedValues, string? customerEmail, int orderQuantity,
            int participantCount, string? baseUrl, string sessionId)
        {
            var model = new CatalogProductSubmissionEmailModel
            {
                Product = new CatalogProduct { Name = product.Name },
                SubmittedValues = submittedValues.Where(value => product.ProductType != CatalogProductType.Signup ||
                        !SignupPersonalDetails.IsPersonalKey(value.Key))
                    .ToDictionary(value => value.Key, value => value.Value),
                CustomerEmail = customerEmail,
                PersonalDetails = product.ProductType == CatalogProductType.Signup ? SignupPersonalDetails.FromValues(submittedValues) : null,
                CustomerName = submittedValues.TryGetValue(SignupPersonalDetails.FullNameKey, out var fullName) ? fullName : null,
                OrderQuantity = orderQuantity,
                ParticipantCount = participantCount,
                IntroHtml = string.IsNullOrWhiteSpace(product.OwnerNotificationBodyHtml)
                    ? BuildDefaultOwnerIntro(product)
                    : product.OwnerNotificationBodyHtml!,
                Branding = _emailBrandingFactory.Create(baseUrl)
            };
            var emailDefaults = CatalogProductEditorOptions.GetDefaultEmailDefaults(product.ProductType);

            var ownerRecipient = _mailSettings.Value.To?.Trim();
            if (!string.IsNullOrWhiteSpace(ownerRecipient))
            {
                yield return Notification(ownerRecipient, string.IsNullOrWhiteSpace(product.OwnerNotificationSubject)
                    ? emailDefaults.OwnerSubject : product.OwnerNotificationSubject);
            }

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                model.IntroHtml = string.IsNullOrWhiteSpace(product.ConfirmationEmailBodyHtml)
                    ? BuildDefaultCustomerIntro(product) : product.ConfirmationEmailBodyHtml;
                yield return Notification(customerEmail, string.IsNullOrWhiteSpace(product.ConfirmationEmailSubject)
                    ? emailDefaults.CustomerSubject : product.ConfirmationEmailSubject);
            }

            CatalogCheckoutMail Notification(string recipient, string subject) => new()
            {
                StripeSessionId = sessionId,
                Recipient = recipient,
                Subject = subject,
                ModelJson = System.Text.Json.JsonSerializer.Serialize(model)
            };
        }

        private static string BuildDefaultCustomerIntro(CatalogProduct product)
        {
            return $"<p>Thank you for your submission for <strong>{System.Net.WebUtility.HtmlEncode(product.Name)}</strong>.</p>" +
                   "<p>We have received your details and will get back to you shortly.</p>";
        }

        private static string BuildDefaultOwnerIntro(CatalogProduct product)
        {
            return $"<p>A new submission was received for <strong>{System.Net.WebUtility.HtmlEncode(product.Name)}</strong>.</p>" +
                   "<p>The details are included below.</p>";
        }
    }
}
