using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Schedule;

[Authorize(Roles = "Teacher,Admin,SuperAdmin")]
public class RosterModel(ISchedulingService schedulingService) : PageModel
{
    public ScheduleRosterPageData Data { get; private set; } = new([], [], [], [], []);
    public bool IsAdmin => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
    [BindProperty] public AvailabilityForm AvailabilityInput { get; set; } = new();

    public async Task OnGetAsync(string? teacherUserId, CancellationToken cancellationToken)
    {
        AvailabilityInput.TeacherUserId = teacherUserId;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAvailabilityAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (!TryValidateModel(AvailabilityInput, nameof(AvailabilityInput)))
        {
            await LoadAsync(cancellationToken);
            return Page();
        }
        var result = await schedulingService.SaveAvailabilityAsync(GetActor(), AvailabilityInput.ToInput(), cancellationToken);
        TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success ? "Teacher availability saved." : result.ErrorMessage;
        return RedirectToPage(new { teacherUserId = AvailabilityInput.TeacherUserId });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Data = await schedulingService.GetRosterPageAsync(GetActor(), ct);
        var selected = AvailabilityInput.TeacherUserId;
        if (!Data.Teachers.Any(item => item.Id == selected))
            selected = Data.Teachers.Any(item => item.Id == GetActor().UserId) ? GetActor().UserId : Data.Teachers.FirstOrDefault()?.Id;
        AvailabilityInput.TeacherUserId = selected;
        AvailabilityInput.TimeZoneId = SchedulingService.AmsterdamTimeZoneId;
        if (AvailabilityInput.Days.Count != 7)
            AvailabilityInput.Days = Enumerable.Range(0, 7).Select(index => (DayOfWeek)((index + 1) % 7)).Select(day =>
            {
                var saved = Data.Availability.FirstOrDefault(item => item.TeacherUserId == selected && item.DayOfWeek == day);
                return new AvailabilityDayForm { DayOfWeek = day, IsAvailable = saved != null, LocalStartTime = saved?.LocalStartTime ?? new(9, 0), LocalEndTime = saved?.LocalEndTime ?? new(17, 0) };
            }).ToList();
    }

    private ScheduleActor GetActor() => new(User.FindFirstValue(ClaimTypes.NameIdentifier)!, IsAdmin, User.IsInRole("Teacher"));
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
