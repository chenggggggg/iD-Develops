using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Schedule
{
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class AppointmentsModel : PageModel
    {
        private readonly IAppointmentService _appointmentService;
        public AppointmentsModel(IAppointmentService appointmentService) => _appointmentService = appointmentService;

        public AppointmentManagementData Data { get; private set; } = new([], [], [], []);
        public bool IsAdmin => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        [BindProperty]
        public AppointmentForm Input { get; set; } = new();

        public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

        public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
        {
            if (!Input.RequiresCredits)
            {
                Input.RequiredCreditTypeId = null;
                Input.CreditCost = 0;
                Input.CreditConsumptionPolicyId = null;
                foreach (var field in new[] { nameof(Input.RequiredCreditTypeId), nameof(Input.CreditCost), nameof(Input.CreditConsumptionPolicyId) })
                    ModelState.Remove($"{nameof(Input)}.{field}");
            }
            if (!ModelState.IsValid)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }
            var result = await _appointmentService.SaveAppointmentTypeAsync(GetActor(), Input.ToInput(), cancellationToken);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Event type could not be saved.");
                await LoadAsync(cancellationToken);
                return Page();
            }
            TempData["StatusMessage"] = Input.Id > 0 ? "Event type updated." : "Event type created.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeactivateAsync(int appointmentTypeId, CancellationToken cancellationToken)
        {
            var result = await _appointmentService.DeactivateAppointmentTypeAsync(GetActor(), appointmentTypeId, cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success
                ? "Event type deactivated."
                : result.ErrorMessage;
            return RedirectToPage();
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            Data = await _appointmentService.GetManagementDataAsync(GetActor(), cancellationToken);
            if (!IsAdmin && Input.TeacherUserIds.Count == 0)
                Input.TeacherUserIds = [User.FindFirstValue(ClaimTypes.NameIdentifier)!];
        }

        private ScheduleActor GetActor() => new(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            IsAdmin,
            User.IsInRole("Teacher"));

        public sealed class AppointmentForm : IValidatableObject
        {
            public int Id { get; set; }
            [Required, StringLength(150, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
            [Range(5, 480)] public int DurationMinutes { get; set; } = 60;
            public bool RequiresCredits { get; set; }
            public int? RequiredCreditTypeId { get; set; }
            public int CreditCost { get; set; } = 1;
            public int? CreditConsumptionPolicyId { get; set; }
            public List<string> TeacherUserIds { get; set; } = [];
            public bool IsActive { get; set; } = true;
            public Enums.ScheduleDeliveryType DeliveryType { get; set; } = Enums.ScheduleDeliveryType.Zoom;
            [StringLength(1024)] public string? MeetingUrl { get; set; }
            [StringLength(300)] public string? Location { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (!RequiresCredits)
                    yield break;
                if (RequiredCreditTypeId is null or <= 0)
                    yield return new("Select a required credit.", [nameof(RequiredCreditTypeId)]);
                if (CreditCost is < 1 or > 1000)
                    yield return new("Credit cost must be between 1 and 1,000.", [nameof(CreditCost)]);
                if (CreditConsumptionPolicyId is null or <= 0)
                    yield return new("Select a credit policy.", [nameof(CreditConsumptionPolicyId)]);
            }

            public AppointmentTypeInput ToInput() => new()
            {
                Id = Id,
                Name = Name,
                DurationMinutes = DurationMinutes,
                RequiredCreditTypeId = RequiresCredits ? RequiredCreditTypeId : null,
                CreditCost = RequiresCredits ? CreditCost : 0,
                CreditConsumptionPolicyId = RequiresCredits ? CreditConsumptionPolicyId : null,
                TeacherUserIds = TeacherUserIds,
                IsActive = IsActive,
                DeliveryType = DeliveryType,
                MeetingUrl = MeetingUrl,
                Location = Location
            };
        }
    }
}
