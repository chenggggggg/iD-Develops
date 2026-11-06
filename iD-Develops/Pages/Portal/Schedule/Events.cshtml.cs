using System.ComponentModel.DataAnnotations;
using iD_Develops.Enums;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iD_Develops.Pages.Portal.Schedule;

[Authorize(Roles = "Teacher,Admin,SuperAdmin")]
public class EventsModel(IAppointmentService appointmentService, ISchedulingService schedulingService, ISchedulingIntegrationService integrationService) : BookModel(appointmentService)
{
    private readonly ISchedulingService _schedulingService = schedulingService;
    private readonly ISchedulingIntegrationService _integrationService = integrationService;
    public ScheduleRosterPageData ScheduleData { get; private set; } = new([], [], [], [], []);
    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }
    [BindProperty(SupportsGet = true)] public int? CourseClassId { get; set; }
    public string SelectedMode => Mode is "single" or "recurring" ? Mode : "session";
    // Bind only the submitted form in its handler, preserving conversion errors without validating unused forms.
    public RosterRuleForm RuleInput { get; set; } = new();
    public ManualEventForm ManualInput { get; set; } = new();

    public override async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (SelectedMode == "session")
        {
            await LoadAsync(cancellationToken);
            return Data.CanAccess ? Page() : Forbid();
        }
        await LoadScheduleAsync(cancellationToken);
        if (CourseClassId.HasValue && ScheduleData.Classes.Any(item => item.Id == CourseClassId.Value))
        {
            if (SelectedMode == "single") ManualInput.CourseClassId = CourseClassId.Value;
            else RuleInput.CourseClassId = CourseClassId.Value;
        }
        return Page();
    }

    private async Task LoadScheduleAsync(CancellationToken ct)
    {
        ScheduleData = await _schedulingService.GetRosterPageAsync(GetActor(), ct);
        RuleInput.TimeZoneId = SchedulingService.AmsterdamTimeZoneId;
        RuleInput.ActiveUntilDate ??= RuleInput.ActiveFromDate.AddMonths(3);
        ManualInput.TimeZoneId = SchedulingService.AmsterdamTimeZoneId;
        if (ManualInput.StartLocal == default) ManualInput.StartLocal = DateTime.Now.Date.AddDays(1).AddHours(9);
    }

    public async Task<IActionResult> OnGetAvailableTeachersAsync(
        DateTime startLocal,
        string timeZoneId,
        int durationMinutes,
        CancellationToken cancellationToken)
    {
        var teachers = await _schedulingService.GetAvailableTeachersAsync(
            GetActor(),
            startLocal,
            timeZoneId,
            durationMinutes,
            cancellationToken);
        return new JsonResult(teachers);
    }

    public async Task<IActionResult> OnGetZoomReadinessAsync(string teacherUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(teacherUserId)) return BadRequest();
        var actor = GetActor();
        if (!IsAdmin && teacherUserId != actor.UserId) return Forbid();
        var teachers = await _schedulingService.GetTeacherOptionsAsync(actor, cancellationToken);
        if (!teachers.Any(teacher => teacher.Id == teacherUserId)) return NotFound();
        var readiness = await _integrationService.GetZoomReadinessAsync(teacherUserId, cancellationToken);
        return new JsonResult(new { state = readiness.State.ToString(), message = readiness.Message, canProvision = readiness.CanAttemptProvisioning });
    }

    public async Task<IActionResult> OnPostRetryRuleAsync(int ruleId, CancellationToken cancellationToken)
        => Complete(await _schedulingService.RetryRosterRuleAsync(GetActor(), ruleId, cancellationToken), "Meeting setup completed.");

    public async Task<IActionResult> OnPostSaveRuleAsync([Bind(Prefix = nameof(RuleInput))] RosterRuleForm input, CancellationToken cancellationToken)
    {
        RuleInput = input;
        Mode = "recurring";
        if (!ModelState.IsValid || !TryValidateModel(RuleInput, nameof(RuleInput)))
        {
            await LoadScheduleAsync(cancellationToken);
            return Page();
        }

        var result = await _schedulingService.SaveRosterRuleAsync(GetActor(), RuleInput.ToInput(), cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "The recurring class could not be saved.");
            await LoadScheduleAsync(cancellationToken);
            return Page();
        }
        return Complete(result, RuleInput.Id > 0 ? "Recurring class updated." : "Recurring class created.");
    }

    public async Task<IActionResult> OnPostDeleteRuleAsync(int ruleId, CancellationToken cancellationToken)
    {
        var result = await _schedulingService.DeleteRosterRuleAsync(GetActor(), ruleId, cancellationToken);
        return Complete(result, "Recurring class removed.");
    }

    public async Task<IActionResult> OnPostCreateManualAsync([Bind(Prefix = nameof(ManualInput))] ManualEventForm input, CancellationToken cancellationToken)
    {
        ManualInput = input;
        Mode = "single";
        if (!ModelState.IsValid || !TryValidateModel(ManualInput, nameof(ManualInput)))
        {
            await LoadScheduleAsync(cancellationToken);
            return Page();
        }

        var result = await _schedulingService.CreateManualEventAsync(GetActor(), ManualInput.ToInput(), cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "The event could not be saved.");
            await LoadScheduleAsync(cancellationToken);
            return Page();
        }
        return Complete(result, "Meeting scheduled.");
    }


    private IActionResult Complete(iD_Develops.Utilities.OperationResult result, string successMessage)
    {
        if (result.Success && result is ScheduleRosterSaveResult { PendingSetupMessage: { Length: > 0 } warning })
            TempData["WarningMessage"] = warning;
        else TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success ? successMessage : result.ErrorMessage;
        return RedirectToPage(new { mode = SelectedMode == "single" ? "single" : "recurring" });
    }

    public sealed class RosterRuleForm
    {
        public int Id { get; set; }
        public string? TeacherUserId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Select a course class.")]
        public int CourseClassId { get; set; }

        public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
        public TimeOnly LocalStartTime { get; set; } = new(9, 0);

        [Required]
        public string TimeZoneId { get; set; } = string.Empty;

        public DateOnly ActiveFromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required]
        public DateOnly? ActiveUntilDate { get; set; }

        [Range(5, 1440, ErrorMessage = "Enter a duration between 5 and 1440 minutes.")]
        public int DurationMinutes { get; set; } = 60;

        [Range(1, 10000)]
        public int Capacity { get; set; } = 1;

        public ScheduleDeliveryType DeliveryType { get; set; } = ScheduleDeliveryType.Zoom;
        public string? MeetingUrl { get; set; }
        public string? Location { get; set; }

        [Range(0, 365)]
        public int BookingOpenDaysBefore { get; set; } = 30;

        [Range(0, 8760)]
        public int BookingCloseHoursBefore { get; set; } = 1;

        [Range(1, 52)]
        public int GenerateWeeksAhead { get; set; } = 12;

        public bool IsVisibleForStudentBooking { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public ScheduleRosterRuleInput ToInput() => new()
        {
            Id = Id,
            TeacherUserId = TeacherUserId,
            CourseClassId = CourseClassId,
            DayOfWeek = DayOfWeek,
            LocalStartTime = LocalStartTime,
            TimeZoneId = TimeZoneId,
            ActiveFromDate = ActiveFromDate,
            ActiveUntilDate = ActiveUntilDate,
            DurationMinutes = DurationMinutes,
            Capacity = Capacity,
            DeliveryType = DeliveryType,
            MeetingUrl = MeetingUrl,
            Location = Location,
            BookingOpenDaysBefore = BookingOpenDaysBefore,
            BookingCloseHoursBefore = BookingCloseHoursBefore,
            GenerateWeeksAhead = GenerateWeeksAhead,
            IsVisibleForStudentBooking = IsVisibleForStudentBooking,
            IsActive = IsActive
        };
    }

    public sealed class ManualEventForm
    {
        public string? TeacherUserId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Select a course class.")]
        public int? CourseClassId { get; set; }

        public string? StudentUserId { get; set; }

        public DateTime StartLocal { get; set; }

        [Required]
        public string TimeZoneId { get; set; } = string.Empty;

        [Range(5, 1440, ErrorMessage = "Enter a duration between 5 and 1440 minutes.")]
        public int DurationMinutes { get; set; } = 60;

        [Range(1, 10000)]
        public int Capacity { get; set; } = 1;

        public ScheduleDeliveryType DeliveryType { get; set; } = ScheduleDeliveryType.Zoom;
        public string? MeetingUrl { get; set; }
        public string? Location { get; set; }

        [Range(0, 365)]
        public int BookingOpenDaysBefore { get; set; } = 30;

        [Range(0, 8760)]
        public int BookingCloseHoursBefore { get; set; } = 1;

        public ManualScheduleEventInput ToInput() => new()
        {
            TeacherUserId = TeacherUserId,
            CourseClassId = CourseClassId,
            StudentUserId = StudentUserId,
            StartLocal = StartLocal,
            TimeZoneId = TimeZoneId,
            DurationMinutes = DurationMinutes,
            Capacity = Capacity,
            DeliveryType = DeliveryType,
            MeetingUrl = MeetingUrl,
            Location = Location,
            BookingOpenDaysBefore = BookingOpenDaysBefore,
            BookingCloseHoursBefore = BookingCloseHoursBefore
        };
    }

}
