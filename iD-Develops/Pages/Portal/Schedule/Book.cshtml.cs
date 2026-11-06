using System.Security.Claims;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Schedule;

[Authorize(Roles = "Student,Teacher,Admin,SuperAdmin")]
public class BookModel(IAppointmentService appointmentService) : PageModel
{
    public AppointmentBookingData Data { get; private set; } = new(false, "", "", [], [], []);
    public IReadOnlyList<AppointmentTypeItem> Options => Data.Options;
    public bool IsStaff => User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
    public bool IsAdmin => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
    public string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [BindProperty(SupportsGet = true)] public string? AttendeeUserId { get; set; }
    public Guid BookingRequestId { get; private set; } = Guid.NewGuid();
    public int SelectedTypeId { get; private set; }
    public string? SelectedTeacherId { get; private set; }
    public string? StartingDate { get; private set; }

    public virtual async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (IsStaff) return RedirectToPage("/Portal/Schedule/Events", new { attendeeUserId = AttendeeUserId });
        await LoadAsync(cancellationToken);
        return Data.CanAccess ? Page() : Forbid();
    }

    public async Task<IActionResult> OnGetSlotsAsync(int appointmentTypeId, string teacherUserId, DateOnly startDate, int days = 14, CancellationToken cancellationToken = default)
    {
        var data = await appointmentService.GetBookingDataAsync(GetActor(), AttendeeUserId, cancellationToken);
        if (!data.CanAccess) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(await appointmentService.GetSlotsAsync(GetActor(), appointmentTypeId, teacherUserId, startDate, days, cancellationToken, data.AttendeeUserId));
    }

    public async Task<IActionResult> OnPostBookAsync(Guid bookingRequestId, int appointmentTypeId, string teacherUserId, DateTime startUtc, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (!Data.CanAccess) return Forbid();
        if (bookingRequestId == Guid.Empty) return BadRequest();
        var result = await appointmentService.BookAppointmentAsync(GetActor(), new(bookingRequestId, appointmentTypeId, teacherUserId, startUtc, Data.AttendeeUserId), cancellationToken);
        if (result.Success)
        {
            TempData["StatusMessage"] = Data.AttendeeUserId == CurrentUserId ? "Your session has been booked." : $"Session booked for {Data.AttendeeName}.";
            return RedirectToPage(new { attendeeUserId = Data.AttendeeUserId == CurrentUserId ? null : Data.AttendeeUserId });
        }
        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "The session could not be booked.");
        SelectedTypeId = appointmentTypeId;
        SelectedTeacherId = teacherUserId;
        var utc = startUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(startUtc, DateTimeKind.Utc) : startUtc.ToUniversalTime();
        StartingDate = TimeZoneInfo.ConvertTimeFromUtc(utc, AmsterdamZone()).ToString("yyyy-MM-dd");
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int scheduledEventId, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (!Data.CanAccess) return Forbid();
        var result = await appointmentService.CancelAppointmentAsync(GetActor(), scheduledEventId, cancellationToken);
        TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success ? "Session cancelled." : result.ErrorMessage;
        return RedirectToPage(new { attendeeUserId = Data.AttendeeUserId == CurrentUserId ? null : Data.AttendeeUserId });
    }

    public async Task<IActionResult> OnPostResolveSetupAsync(Guid requestId, bool providerCleanupConfirmed, CancellationToken cancellationToken)
    {
        if (!IsAdmin) return Forbid();
        var result = await appointmentService.ResolveSetupAsync(GetActor(), requestId, providerCleanupConfirmed, cancellationToken);
        TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success ? "Unconfirmed setup cancelled and reserved credits released." : result.ErrorMessage;
        return RedirectToPage();
    }

    protected async Task LoadAsync(CancellationToken ct) => Data = await appointmentService.GetBookingDataAsync(GetActor(), AttendeeUserId, ct);
    protected ScheduleActor GetActor() => new(CurrentUserId, User.IsInRole("Admin") || User.IsInRole("SuperAdmin"), User.IsInRole("Teacher"));
    public static TimeZoneInfo AmsterdamZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
    }
}
