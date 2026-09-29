using System.ComponentModel.DataAnnotations;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Admin
{
    [Authorize(Roles = "Admin, SuperAdmin")]
    public class CreditsModel : PageModel
    {
        private readonly ICreditConfigurationService _creditConfigurationService;

        public CreditsModel(ICreditConfigurationService creditConfigurationService)
        {
            _creditConfigurationService = creditConfigurationService;
        }

        public IReadOnlyList<CreditTypeListItem> CreditTypes { get; private set; } = Array.Empty<CreditTypeListItem>();
        public IReadOnlyList<CreditPolicyListItem> Policies { get; private set; } = Array.Empty<CreditPolicyListItem>();

        public bool OpenCreditTypeCreateForm { get; private set; }
        public bool OpenPolicyCreateForm { get; private set; }

        public CreditTypeFormInput CreditTypeInput { get; set; } = new();

        public PolicyFormInput PolicyInput { get; set; } = new();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadAsync(cancellationToken);
        }

        public async Task<IActionResult> OnPostCreateCreditTypeAsync(
            [Bind(Prefix = nameof(CreditTypeInput))] CreditTypeFormInput input,
            CancellationToken cancellationToken)
        {
            CreditTypeInput = input;
            if (!ModelState.IsValid)
            {
                OpenCreditTypeCreateForm = true;
                return await InvalidPageAsync(cancellationToken);
            }

            var result = await _creditConfigurationService.CreateCreditTypeAsync(CreditTypeInput.ToEntity(), cancellationToken);
            if (!result.Success)
            {
                ModelState.AddModelError(nameof(CreditTypeInput), result.ErrorMessage ?? "Credit type could not be created.");
                OpenCreditTypeCreateForm = true;
                return await InvalidPageAsync(cancellationToken);
            }
            return Complete(result.Success, result.ErrorMessage, "Credit type created.");
        }

        public async Task<IActionResult> OnPostUpdateCreditTypeAsync(
            [Bind(Prefix = nameof(CreditTypeInput))] CreditTypeFormInput input,
            CancellationToken cancellationToken)
        {
            CreditTypeInput = input;
            if (!ModelState.IsValid)
                return await InvalidPageAsync(cancellationToken);

            var result = await _creditConfigurationService.UpdateCreditTypeAsync(CreditTypeInput.ToEntity(), cancellationToken);
            return Complete(result.Success, result.ErrorMessage, "Credit type updated.");
        }

        public async Task<IActionResult> OnPostCreatePolicyAsync(
            [Bind(Prefix = nameof(PolicyInput))] PolicyFormInput input,
            CancellationToken cancellationToken)
        {
            PolicyInput = input;
            if (!ModelState.IsValid)
            {
                OpenPolicyCreateForm = true;
                return await InvalidPageAsync(cancellationToken);
            }

            var result = await _creditConfigurationService.CreatePolicyAsync(PolicyInput.ToEntity(), cancellationToken);
            if (!result.Success)
            {
                ModelState.AddModelError(nameof(PolicyInput), result.ErrorMessage ?? "Consumption policy could not be created.");
                OpenPolicyCreateForm = true;
                return await InvalidPageAsync(cancellationToken);
            }
            return Complete(result.Success, result.ErrorMessage, "Consumption policy created.");
        }

        public async Task<IActionResult> OnPostUpdatePolicyAsync(
            [Bind(Prefix = nameof(PolicyInput))] PolicyFormInput input,
            CancellationToken cancellationToken)
        {
            PolicyInput = input;
            if (!ModelState.IsValid)
                return await InvalidPageAsync(cancellationToken);

            var result = await _creditConfigurationService.UpdatePolicyAsync(PolicyInput.ToEntity(), cancellationToken);
            return Complete(result.Success, result.ErrorMessage, "Consumption policy updated.");
        }

        private async Task<IActionResult> InvalidPageAsync(CancellationToken cancellationToken)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        private IActionResult Complete(bool succeeded, string? error, string successMessage)
        {
            TempData[succeeded ? "StatusMessage" : "ErrorMessage"] = succeeded ? successMessage : error;
            return RedirectToPage();
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            CreditTypes = await _creditConfigurationService.GetCreditTypesAsync(cancellationToken);
            Policies = await _creditConfigurationService.GetPoliciesAsync(cancellationToken);
        }

        public sealed class CreditTypeFormInput
        {
            public int Id { get; set; }

            [Required, MaxLength(150)]
            public string Name { get; set; } = string.Empty;

            [MaxLength(1000)]
            public string? Description { get; set; }

            [Required, MaxLength(80)]
            public string SingularLabel { get; set; } = "credit";

            [Required, MaxLength(80)]
            public string PluralLabel { get; set; } = "credits";

            [Range(1, 3650)]
            public int? DefaultValidityValue { get; set; }

            public CreditValidityUnit? DefaultValidityUnit { get; set; }
            public bool IsActive { get; set; } = true;

            public CreditType ToEntity() => new()
            {
                Id = Id,
                Name = Name,
                Description = Description,
                SingularLabel = SingularLabel,
                PluralLabel = PluralLabel,
                DefaultValidityValue = DefaultValidityValue,
                DefaultValidityUnit = DefaultValidityUnit,
                IsActive = IsActive
            };
        }

        public sealed class PolicyFormInput
        {
            public int Id { get; set; }

            [Required, MaxLength(150)]
            public string Name { get; set; } = string.Empty;

            [MaxLength(1000)]
            public string? Description { get; set; }

            public CreditConsumptionTiming ConsumptionTiming { get; set; } = CreditConsumptionTiming.OnBooking;

            [Range(0, 8760)]
            public int CancellationWindowHours { get; set; } = 24;

            public CreditResolutionAction AttendedAction { get; set; } = CreditResolutionAction.Consume;
            public CreditResolutionAction NoShowAction { get; set; } = CreditResolutionAction.Consume;
            public CreditResolutionAction EarlyCancellationAction { get; set; } = CreditResolutionAction.Return;
            public CreditResolutionAction LateCancellationAction { get; set; } = CreditResolutionAction.Consume;
            public CreditResolutionAction StaffCancellationAction { get; set; } = CreditResolutionAction.Return;
            public bool IsActive { get; set; } = true;

            public CreditConsumptionPolicy ToEntity() => new()
            {
                Id = Id,
                Name = Name,
                Description = Description,
                ConsumptionTiming = ConsumptionTiming,
                CancellationWindowHours = CancellationWindowHours,
                AttendedAction = AttendedAction,
                NoShowAction = NoShowAction,
                EarlyCancellationAction = EarlyCancellationAction,
                LateCancellationAction = LateCancellationAction,
                StaffCancellationAction = StaffCancellationAction,
                IsActive = IsActive
            };
        }
    }
}
