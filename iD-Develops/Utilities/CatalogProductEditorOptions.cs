using iD_Develops.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace iD_Develops.Utilities
{
    public static class CatalogProductEditorOptions
    {
        public sealed record EditorOption(int Value, string Label, string Description);
        public sealed record JourneyState(bool CollectDetails, bool SendToBooking, bool TakePayment);
        public sealed record EmailDefaults(string CustomerSubject, string OwnerSubject);

        private static readonly IReadOnlyDictionary<CatalogProductType, CatalogWorkflowType[]> AllowedWorkflowMap =
            new Dictionary<CatalogProductType, CatalogWorkflowType[]>
            {
                [CatalogProductType.Enrollment] = new[] { CatalogWorkflowType.FormSubmission },
                [CatalogProductType.ExternalBooking] = new[] { CatalogWorkflowType.FormThenExternalBooking, CatalogWorkflowType.ExternalBookingOnly },
                [CatalogProductType.DigitalCourse] = new[] { CatalogWorkflowType.FormThenStripeCheckout },
                [CatalogProductType.BookingAddOn] = new[] { CatalogWorkflowType.FormThenStripeCheckout, CatalogWorkflowType.FormThenExternalBooking, CatalogWorkflowType.ExternalBookingOnly },
                [CatalogProductType.Standard] = new[] { CatalogWorkflowType.FormThenStripeCheckout },
                [CatalogProductType.Booking] = new[] { CatalogWorkflowType.ExternalBookingOnly, CatalogWorkflowType.FormThenExternalBooking },
                [CatalogProductType.Signup] = new[] { CatalogWorkflowType.FormSubmission },
                [CatalogProductType.Credit] = new[] { CatalogWorkflowType.FormThenStripeCheckout },
                [CatalogProductType.FreeDownload] = new[] { CatalogWorkflowType.ExternalBookingOnly }
            };

        public static IReadOnlyList<EditorOption> OfferTypeOptions { get; } = new[]
        {
            new EditorOption((int)CatalogProductType.Enrollment, "Enrollment request", "Collect an interest or signup form that you review manually."),
            new EditorOption((int)CatalogProductType.ExternalBooking, "Booking service", "Send people into a booking flow such as Koalendar."),
            new EditorOption((int)CatalogProductType.DigitalCourse, "Digital course", "Take payment and then unlock course access or digital delivery."),
            new EditorOption((int)CatalogProductType.BookingAddOn, "Credits or tokens", "Sell or grant reusable session credits, tokens, or flexible add-on access."),
            new EditorOption((int)CatalogProductType.Standard, "Standard checkout", "Show product details and send the customer to Stripe checkout."),
            new EditorOption((int)CatalogProductType.Booking, "Koalendar booking", "Show product details and send the customer to Koalendar."),
            new EditorOption((int)CatalogProductType.Signup, "Signup form", "Collect a custom signup form without payment."),
            new EditorOption((int)CatalogProductType.Credit, "Credits", "Sell credits through Stripe checkout."),
            new EditorOption((int)CatalogProductType.FreeDownload, "Free download", "Publish a downloadable file in the free downloads section.")
        };

        public static IEnumerable<SelectListItem> GetOfferTypeSelectList(CatalogProductType? selected = null)
            => OfferTypeOptions.Select(option => new SelectListItem(option.Label, option.Value.ToString())
            {
                Selected = selected.HasValue && option.Value == (int)selected.Value
            });

        public static IReadOnlyList<CatalogWorkflowType> GetAllowedWorkflows(CatalogProductType productType)
            => AllowedWorkflowMap.TryGetValue(productType, out var flows)
                ? flows
                : Array.Empty<CatalogWorkflowType>();

        public static bool IsWorkflowAllowed(CatalogProductType productType, CatalogWorkflowType workflowType)
            => GetAllowedWorkflows(productType).Contains(workflowType);

        public static JourneyState GetJourneyState(CatalogWorkflowType workflowType)
            => workflowType switch
            {
                CatalogWorkflowType.FormSubmission => new JourneyState(true, false, false),
                CatalogWorkflowType.FormThenExternalBooking => new JourneyState(true, true, false),
                CatalogWorkflowType.FormThenStripeCheckout => new JourneyState(true, false, true),
                CatalogWorkflowType.ExternalBookingOnly => new JourneyState(false, true, false),
                _ => new JourneyState(true, false, false)
            };

        public static CatalogWorkflowType? ResolveWorkflowType(bool collectDetails, bool sendToBooking, bool takePayment)
        {
            if (collectDetails && !sendToBooking && !takePayment)
            {
                return CatalogWorkflowType.FormSubmission;
            }

            if (collectDetails && sendToBooking && !takePayment)
            {
                return CatalogWorkflowType.FormThenExternalBooking;
            }

            if (collectDetails && !sendToBooking && takePayment)
            {
                return CatalogWorkflowType.FormThenStripeCheckout;
            }

            if (!collectDetails && sendToBooking && !takePayment)
            {
                return CatalogWorkflowType.ExternalBookingOnly;
            }

            return null;
        }

        public static JourneyState GetDefaultJourneyState(CatalogProductType productType)
            => productType switch
            {
                CatalogProductType.Enrollment => new JourneyState(true, false, false),
                CatalogProductType.ExternalBooking => new JourneyState(true, true, false),
                CatalogProductType.DigitalCourse => new JourneyState(true, false, true),
                CatalogProductType.BookingAddOn => new JourneyState(true, false, true),
                CatalogProductType.Standard => new JourneyState(true, false, true),
                CatalogProductType.Booking => new JourneyState(false, true, false),
                CatalogProductType.Signup => new JourneyState(true, false, false),
                CatalogProductType.Credit => new JourneyState(true, false, true),
                CatalogProductType.FreeDownload => new JourneyState(false, false, false),
                _ => new JourneyState(true, false, false)
            };

        public static JourneyState NormalizeJourneyState(CatalogProductType productType, bool collectDetails, bool sendToBooking, bool takePayment)
            => productType switch
            {
                CatalogProductType.Enrollment => new JourneyState(true, false, false),
                CatalogProductType.ExternalBooking => new JourneyState(collectDetails, true, false),
                CatalogProductType.DigitalCourse => new JourneyState(true, false, true),
                CatalogProductType.BookingAddOn => new JourneyState(collectDetails, sendToBooking, takePayment),
                CatalogProductType.Standard => new JourneyState(true, false, true),
                CatalogProductType.Booking => new JourneyState(false, true, false),
                CatalogProductType.Signup => new JourneyState(true, false, false),
                CatalogProductType.Credit => new JourneyState(true, false, true),
                CatalogProductType.FreeDownload => new JourneyState(false, false, false),
                _ => new JourneyState(collectDetails, sendToBooking, takePayment)
            };

        public static EmailDefaults GetDefaultEmailDefaults(CatalogProductType productType)
            => productType switch
            {
                CatalogProductType.Enrollment => new EmailDefaults("Successfully signed up!", "New enrollment request"),
                CatalogProductType.ExternalBooking => new EmailDefaults("Booking request received!", "New booking request"),
                CatalogProductType.DigitalCourse => new EmailDefaults("Thanks for your order!", "New digital order"),
                CatalogProductType.BookingAddOn => new EmailDefaults("Thanks for your order!", "New credits or tokens order"),
                CatalogProductType.Standard => new EmailDefaults("Thanks for your order!", "New product order"),
                CatalogProductType.Booking => new EmailDefaults("Booking received!", "New Koalendar booking"),
                CatalogProductType.Signup => new EmailDefaults("Successfully signed up!", "New signup request"),
                CatalogProductType.Credit => new EmailDefaults("Thanks for your order!", "New credit order"),
                CatalogProductType.FreeDownload => new EmailDefaults("Your download is ready", "New free download activity"),
                _ => new EmailDefaults("Thanks for your submission!", "New product submission")
            };
    }
}
