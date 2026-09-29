using System.Globalization;
using System.Security.Claims;
using iD_Develops.Enums;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Schedule
{
    [Authorize(Policy = "PortalUser")]
    public class IndexModel : PageModel
    {
        private readonly ISchedulingService _schedulingService;
        private readonly IZoomAttendanceService _zoomAttendanceService;

        public IndexModel(
            ISchedulingService schedulingService,
            IZoomAttendanceService zoomAttendanceService)
        {
            _schedulingService = schedulingService;
            _zoomAttendanceService = zoomAttendanceService;
        }

        public ScheduleOverviewData Overview { get; private set; } =
            new(0, 0, 0, Array.Empty<ScheduleCalendarItem>(), Array.Empty<ScheduleCalendarItem>());

        public bool CanManageSchedule => User.IsInRole("Teacher") || IsAdmin;

        public bool ViewingStudentCalendar => CalendarView.StartsWith("student:", StringComparison.Ordinal);

        public bool ViewingAllCalendars => string.Equals(CalendarView, "all", StringComparison.Ordinal);

        public string? SelectedTeacherUserId => CalendarView.StartsWith("teacher:", StringComparison.Ordinal)
            ? CalendarView["teacher:".Length..]
            : null;

        public string? StudentUserId => ViewingStudentCalendar
            ? CalendarView["student:".Length..]
            : null;

        public bool CanEditDisplayedCalendar =>
            CanManageSchedule &&
            !ViewingStudentCalendar &&
            (IsAdmin || (!ViewingAllCalendars &&
                         (SelectedTeacherUserId == null || SelectedTeacherUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))));

        public IReadOnlyList<ScheduleStudentOption> Students { get; private set; } = Array.Empty<ScheduleStudentOption>();

        public IReadOnlyList<ScheduleTeacherOption> Teachers { get; private set; } = Array.Empty<ScheduleTeacherOption>();

        public string? SelectedStudentEmail => Students.FirstOrDefault(student => student.Id == StudentUserId)?.Email;

        public string DisplayedCalendarName => ViewingStudentCalendar
            ? SelectedStudentEmail ?? "Student calendar"
            : ViewingAllCalendars
                ? "All calendars"
                : SelectedTeacherUserId == null
                    ? "My calendar"
                    : Teachers.FirstOrDefault(teacher => teacher.Id == SelectedTeacherUserId)?.Name ?? "Teacher calendar";

        [BindProperty(SupportsGet = true)]
        public string CalendarView { get; set; } = "mine";

        private bool IsAdmin => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var actor = GetActor();
            if (actor == null)
                return Challenge();

            await LoadCalendarOptionsAsync(actor, cancellationToken);
            Overview = await GetSelectedOverviewAsync(actor, cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnGetEventsAsync(
            string? start,
            string? end,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store, no-cache";
            var actor = GetActor();
            if (actor == null)
                return Unauthorized();

            await LoadCalendarOptionsAsync(actor, cancellationToken);

            var rangeStart = ParseDate(start) ?? DateTime.UtcNow.AddMonths(-1);
            var rangeEnd = ParseDate(end) ?? DateTime.UtcNow.AddMonths(3);
            var items = ViewingStudentCalendar && CanManageSchedule
                ? await _schedulingService.GetStudentCalendarItemsAsync(
                    actor,
                    StudentUserId!,
                    rangeStart,
                    rangeEnd,
                    cancellationToken)
                : CanManageSchedule && CalendarView != "mine"
                    ? await _schedulingService.GetStaffCalendarItemsAsync(
                        actor,
                        SelectedTeacherUserId,
                        ViewingAllCalendars,
                        rangeStart,
                        rangeEnd,
                        cancellationToken)
                : await _schedulingService.GetCalendarItemsAsync(
                    actor,
                    rangeStart,
                    rangeEnd,
                    cancellationToken);
            return new JsonResult(items);
        }

        public async Task<IActionResult> OnGetOverviewAsync(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store, no-cache";
            var actor = GetActor();
            if (actor == null)
                return Unauthorized();
            await LoadCalendarOptionsAsync(actor, cancellationToken);
            return new JsonResult(await GetSelectedOverviewAsync(actor, cancellationToken));
        }

        public async Task<IActionResult> OnGetJoinAsync(
            int scheduledEventId,
            CancellationToken cancellationToken)
        {
            var actor = GetActor();
            if (actor == null)
                return Challenge();
            var result = await _schedulingService.GetJoinUrlAsync(actor, scheduledEventId, cancellationToken);
            if (!result.Success || string.IsNullOrWhiteSpace(result.JoinUrl))
            {
                TempData["ErrorMessage"] = result.ErrorMessage;
                return RedirectToPage();
            }
            return Redirect(result.JoinUrl);
        }

        public Task<IActionResult> OnPostBookAsync(int scheduledEventId, CancellationToken cancellationToken)
            => CompleteAsync(
                actor => _schedulingService.BookAsync(actor, scheduledEventId, cancellationToken),
                "Meeting booked.");

        public Task<IActionResult> OnPostCancelBookingAsync(int scheduledEventId, CancellationToken cancellationToken)
            => CompleteAsync(
                actor => _schedulingService.CancelBookingAsync(actor, scheduledEventId, cancellationToken),
                "Booking cancelled.");

        public Task<IActionResult> OnPostAttendanceAsync(
            int eventBookingId,
            EventBookingStatus status,
            CancellationToken cancellationToken)
            => CompleteAsync(
                actor => _zoomAttendanceService.OverrideAttendanceAsync(
                    actor,
                    eventBookingId,
                    status,
                    cancellationToken),
                "Attendance updated.");

        public Task<IActionResult> OnPostMoveEventAsync(
            int scheduledEventId,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken)
            => CompleteAsync(
                actor => _schedulingService.MoveEventAsync(
                    actor,
                    scheduledEventId,
                    startUtc,
                    endUtc,
                    cancellationToken),
                "Meeting moved.");

        public Task<IActionResult> OnPostCancelEventAsync(
            int scheduledEventId,
            string? reason,
            CancellationToken cancellationToken)
            => CompleteAsync(
                actor => _schedulingService.CancelEventAsync(
                    actor,
                    scheduledEventId,
                    reason,
                    cancellationToken),
                "Meeting cancelled.");

        private async Task<IActionResult> CompleteAsync(
            Func<ScheduleActor, Task<iD_Develops.Utilities.OperationResult>> action,
            string successMessage)
        {
            var actor = GetActor();
            if (actor == null)
                return Unauthorized();

            var result = await action(actor);
            var message = result.Success ? successMessage : result.ErrorMessage;
            if (IsAjaxRequest())
            {
                if (!result.Success)
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                return new JsonResult(new { success = result.Success, message });
            }

            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = message;
            return RedirectToPage();
        }

        private ScheduleActor? GetActor()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrWhiteSpace(userId)
                ? null
                : new ScheduleActor(userId, IsAdmin, User.IsInRole("Teacher"));
        }

        private bool IsAjaxRequest()
            => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        private static DateTime? ParseDate(string? value)
            => DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed.UtcDateTime
                : null;

        private async Task LoadCalendarOptionsAsync(ScheduleActor actor, CancellationToken cancellationToken)
        {
            CalendarView = string.IsNullOrWhiteSpace(CalendarView) ? "mine" : CalendarView.Trim();
            if (!CanManageSchedule)
            {
                CalendarView = "mine";
                return;
            }

            Teachers = await _schedulingService.GetTeacherOptionsAsync(actor, cancellationToken);
            Students = await _schedulingService.GetStudentOptionsAsync(actor, cancellationToken);
            var valid = CalendarView == "mine" ||
                        CalendarView == "all" ||
                        (SelectedTeacherUserId != null && Teachers.Any(teacher => teacher.Id == SelectedTeacherUserId)) ||
                        (StudentUserId != null && Students.Any(student => student.Id == StudentUserId));
            if (!valid)
                CalendarView = "mine";
        }

        private async Task<ScheduleOverviewData> GetSelectedOverviewAsync(
            ScheduleActor actor,
            CancellationToken cancellationToken)
        {
            ScheduleOverviewData selectedOverview;
            if (ViewingStudentCalendar && CanManageSchedule)
            {
                selectedOverview = await _schedulingService.GetStudentOverviewAsync(actor, StudentUserId!, cancellationToken);
            }
            else if (CanManageSchedule && CalendarView != "mine")
            {
                selectedOverview = await _schedulingService.GetStaffOverviewAsync(
                    actor,
                    SelectedTeacherUserId,
                    ViewingAllCalendars,
                    cancellationToken);
            }
            else
            {
                selectedOverview = await _schedulingService.GetOverviewAsync(actor, cancellationToken);
            }

            return selectedOverview;
        }
    }
}
