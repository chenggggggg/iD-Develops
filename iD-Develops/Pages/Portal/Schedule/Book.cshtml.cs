using System.Security.Claims;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Schedule
{
    [Authorize(Roles = "Student")]
    public class BookModel : PageModel
    {
        private readonly IAppointmentService _appointmentService;
        public BookModel(IAppointmentService appointmentService) => _appointmentService = appointmentService;
        public IReadOnlyList<AppointmentTypeItem> Options { get; private set; } = [];

        public async Task OnGetAsync(CancellationToken cancellationToken) => Options = await _appointmentService.GetStudentOptionsAsync(GetActor(), cancellationToken);

        public async Task<IActionResult> OnGetSlotsAsync(int appointmentTypeId, string teacherUserId, DateOnly startDate, int days = 14, CancellationToken cancellationToken = default)
            => new JsonResult(await _appointmentService.GetSlotsAsync(GetActor(), appointmentTypeId, teacherUserId, startDate, days, cancellationToken));

        public async Task<IActionResult> OnPostBookAsync(int appointmentTypeId, string teacherUserId, DateTime startUtc, CancellationToken cancellationToken)
        {
            var result = await _appointmentService.BookAppointmentAsync(GetActor(), appointmentTypeId, teacherUserId, startUtc.ToUniversalTime(), cancellationToken);
            if (result.Success)
            {
                TempData["StatusMessage"] = "Your private session has been booked.";
                return RedirectToPage("/Portal/Schedule/Index");
            }
            TempData["ErrorMessage"] = result.ErrorMessage;
            return RedirectToPage();
        }

        private ScheduleActor GetActor() => new(User.FindFirstValue(ClaimTypes.NameIdentifier)!, false, User.IsInRole("Teacher"));
    }
}
