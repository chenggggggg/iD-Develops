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
            ModelState.Clear();
            if (!TryValidateModel(Input, nameof(Input)))
            {
                await LoadAsync(cancellationToken);
                return Page();
            }
            var result = await _appointmentService.SaveAppointmentTypeAsync(GetActor(), Input.ToInput(), cancellationToken);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Appointment type could not be saved.");
                await LoadAsync(cancellationToken);
                return Page();
            }
            TempData["StatusMessage"] = Input.Id > 0 ? "Appointment type updated." : "Appointment type created.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeactivateAsync(int appointmentTypeId, CancellationToken cancellationToken)
        {
            var result = await _appointmentService.DeactivateAppointmentTypeAsync(GetActor(), appointmentTypeId, cancellationToken);
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success
                ? "Appointment type deactivated."
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

        public sealed class AppointmentForm
        {
            public int Id { get; set; }
            [Required, StringLength(150, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
            [Range(5, 480)] public int DurationMinutes { get; set; } = 60;
            [Range(1, int.MaxValue)] public int RequiredCreditTypeId { get; set; }
            [Range(1, 1000)] public int CreditCost { get; set; } = 1;
            [Range(1, int.MaxValue)] public int CreditConsumptionPolicyId { get; set; }
            public List<string> TeacherUserIds { get; set; } = [];
            public bool IsActive { get; set; } = true;

            public AppointmentTypeInput ToInput() => new()
            {
                Id = Id,
                Name = Name,
                DurationMinutes = DurationMinutes,
                RequiredCreditTypeId = RequiredCreditTypeId,
                CreditCost = CreditCost,
                CreditConsumptionPolicyId = CreditConsumptionPolicyId,
                TeacherUserIds = TeacherUserIds,
                IsActive = IsActive
            };
        }
    }
}
