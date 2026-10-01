using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using iD_Develops.Enums;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Schedule
{
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class RosterModel : PageModel
    {
        private readonly ISchedulingService _schedulingService;

        public RosterModel(ISchedulingService schedulingService)
        {
            _schedulingService = schedulingService;
        }

        public ScheduleRosterPageData Data { get; private set; } =
            new(
                Array.Empty<ScheduleRosterListItem>(),
                Array.Empty<ScheduleClassOption>(),
                Array.Empty<ScheduleTeacherOption>(),
                Array.Empty<ScheduleStudentOption>(),
                Array.Empty<TeacherAvailabilityListItem>());

        public bool IsAdmin => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        [BindProperty]
        public RosterRuleForm RuleInput { get; set; } = new();

        [BindProperty]
        public ManualEventForm ManualInput { get; set; } = new();

        [BindProperty]
        public AvailabilityForm AvailabilityInput { get; set; } = new();

        public async Task OnGetAsync(int? courseClassId, string? mode, CancellationToken cancellationToken)
        {
            await LoadAsync(cancellationToken);
            if (courseClassId.HasValue && Data.Classes.Any(courseClass => courseClass.Id == courseClassId.Value))
            {
                if (string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase))
                    ManualInput.CourseClassId = courseClassId.Value;
                else
                    RuleInput.CourseClassId = courseClassId.Value;
            }
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

        public async Task<IActionResult> OnPostSaveRuleAsync(CancellationToken cancellationToken)
        {
            ModelState.Clear();
            if (!TryValidateModel(RuleInput, nameof(RuleInput)))
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            var result = await _schedulingService.SaveRosterRuleAsync(GetActor(), RuleInput.ToInput(), cancellationToken);
            return Complete(result, RuleInput.Id > 0 ? "Recurring class updated." : "Recurring class created.");
        }

        public async Task<IActionResult> OnPostSaveAvailabilityAsync(CancellationToken cancellationToken)
        {
            ModelState.Clear();
            if (!TryValidateModel(AvailabilityInput, nameof(AvailabilityInput)))
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            var result = await _schedulingService.SaveAvailabilityAsync(
                GetActor(),
                AvailabilityInput.ToInput(),
                cancellationToken);
            return Complete(result, "Teacher availability saved.");
        }

        public async Task<IActionResult> OnPostDeleteRuleAsync(int ruleId, CancellationToken cancellationToken)
        {
            var result = await _schedulingService.DeleteRosterRuleAsync(GetActor(), ruleId, cancellationToken);
            return Complete(result, "Recurring class removed.");
        }

        public async Task<IActionResult> OnPostCreateManualAsync(CancellationToken cancellationToken)
        {
            ModelState.Clear();
            if (!TryValidateModel(ManualInput, nameof(ManualInput)))
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            var result = await _schedulingService.CreateManualEventAsync(GetActor(), ManualInput.ToInput(), cancellationToken);
            return Complete(result, "Meeting scheduled.");
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            Data = await _schedulingService.GetRosterPageAsync(GetActor(), cancellationToken);
            var selectedTeacherId = AvailabilityInput.TeacherUserId;
            if (string.IsNullOrWhiteSpace(selectedTeacherId))
                selectedTeacherId = Data.Teachers.FirstOrDefault()?.Id;
            AvailabilityInput.TeacherUserId = selectedTeacherId;

            var selectedAvailability = Data.Availability
                .Where(item => item.TeacherUserId == selectedTeacherId)
                .ToList();
            AvailabilityInput.TimeZoneId = SchedulingService.AmsterdamTimeZoneId;
            if (AvailabilityInput.Days.Count != 7)
            {
                AvailabilityInput.Days = OrderedDays()
                    .Select(day =>
                    {
                        var saved = selectedAvailability.FirstOrDefault(item => item.DayOfWeek == day);
                        return new AvailabilityDayForm
                        {
                            DayOfWeek = day,
                            IsAvailable = saved != null,
                            LocalStartTime = saved?.LocalStartTime ?? new TimeOnly(9, 0),
                            LocalEndTime = saved?.LocalEndTime ?? new TimeOnly(17, 0)
                        };
                    })
                    .ToList();
            }

            RuleInput.TimeZoneId = SchedulingService.AmsterdamTimeZoneId;
            if (!RuleInput.ActiveUntilDate.HasValue)
                RuleInput.ActiveUntilDate = RuleInput.ActiveFromDate.AddMonths(3);
            ManualInput.TimeZoneId = SchedulingService.AmsterdamTimeZoneId;
            if (ManualInput.StartLocal == default)
                ManualInput.StartLocal = DateTime.Now.Date.AddDays(1).AddHours(9);
        }

        private ScheduleActor GetActor()
            => new(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                IsAdmin,
                User.IsInRole("Teacher"));

        private static IEnumerable<DayOfWeek> OrderedDays()
            => new[]
            {
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday
            };

        private IActionResult Complete(iD_Develops.Utilities.OperationResult result, string successMessage)
        {
            TempData[result.Success ? "StatusMessage" : "ErrorMessage"] =
                result.Success ? successMessage : result.ErrorMessage;
            return RedirectToPage();
        }

        public sealed class RosterRuleForm
        {
            public int Id { get; set; }
            public string? TeacherUserId { get; set; }

            [Range(1, int.MaxValue)]
            public int CourseClassId { get; set; }

            public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
            public TimeOnly LocalStartTime { get; set; } = new(9, 0);

            [Required]
            public string TimeZoneId { get; set; } = string.Empty;

            public DateOnly ActiveFromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

            [Required]
            public DateOnly? ActiveUntilDate { get; set; }

            [Range(5, 1440)]
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

            [Range(1, int.MaxValue)]
            public int? CourseClassId { get; set; }

            public string? StudentUserId { get; set; }

            public DateTime StartLocal { get; set; }

            [Required]
            public string TimeZoneId { get; set; } = string.Empty;

            [Range(5, 1440)]
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

        public sealed class AvailabilityForm
        {
            public string? TeacherUserId { get; set; }

            [Required]
            public string TimeZoneId { get; set; } = string.Empty;

            public List<AvailabilityDayForm> Days { get; set; } = new();

            public TeacherAvailabilityInput ToInput() => new()
            {
                TeacherUserId = TeacherUserId,
                TimeZoneId = TimeZoneId,
                Days = Days.Select(day => new TeacherAvailabilityDayInput
                {
                    DayOfWeek = day.DayOfWeek,
                    IsAvailable = day.IsAvailable,
                    LocalStartTime = day.LocalStartTime,
                    LocalEndTime = day.LocalEndTime
                }).ToList()
            };
        }

        public sealed class AvailabilityDayForm
        {
            public DayOfWeek DayOfWeek { get; set; }
            public bool IsAvailable { get; set; }
            public TimeOnly LocalStartTime { get; set; } = new(9, 0);
            public TimeOnly LocalEndTime { get; set; } = new(17, 0);
        }
    }
}
