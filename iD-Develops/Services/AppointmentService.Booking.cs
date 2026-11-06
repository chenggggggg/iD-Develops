using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services;

public sealed partial class AppointmentService
{
    private async Task<string?> ResolveAttendeeAsync(ScheduleActor actor, string? requestedId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor.UserId) || !await _dbContext.Users.AnyAsync(item => item.Id == actor.UserId && !item.IsDeleted, ct)) return null;
        var id = string.IsNullOrWhiteSpace(requestedId) ? actor.UserId : requestedId.Trim();
        if (id == actor.UserId) return id;
        if (!actor.IsAdmin && !actor.IsTeacher) return null;
        var roles = _dbContext.Roles.Where(item => item.Name == "Student").Select(item => item.Id);
        return await _dbContext.Users.AnyAsync(item => item.Id == id && !item.IsDeleted &&
            _dbContext.UserRoles.Any(role => role.UserId == item.Id && roles.Contains(role.RoleId)), ct) ? id : null;
    }

    public async Task<AppointmentBookingData> GetBookingDataAsync(ScheduleActor actor, string? attendeeUserId = null, CancellationToken cancellationToken = default)
    {
        var attendeeId = await ResolveAttendeeAsync(actor, attendeeUserId, cancellationToken);
        if (attendeeId == null) return new(false, string.Empty, string.Empty, [], [], []);
        var attendee = await _dbContext.Users.AsNoTracking().SingleAsync(item => item.Id == attendeeId, cancellationToken);
        var validHosts = await ValidTeacherIdsQuery().ToListAsync(cancellationToken);
        var options = (await GetStudentOptionsAsync(new(attendeeId, false, false), cancellationToken))
            .Select(item => item with
            {
                Teachers = item.Teachers.Where(host => host.Id != attendeeId && validHosts.Contains(host.Id) &&
                (actor.IsAdmin || !actor.IsTeacher || attendeeId == actor.UserId || host.Id == actor.UserId)).ToList()
            })
            .Where(item => item.Teachers.Count > 0).ToList();
        List<AppointmentAttendee> attendees = [];
        if (actor.IsAdmin || actor.IsTeacher)
        {
            var roles = _dbContext.Roles.Where(item => item.Name == "Student").Select(item => item.Id);
            var users = await _dbContext.Users.AsNoTracking().Where(item => !item.IsDeleted && item.Id != actor.UserId &&
                _dbContext.UserRoles.Any(role => role.UserId == item.Id && roles.Contains(role.RoleId)))
                .OrderBy(item => item.FirstName).ThenBy(item => item.LastName).ToListAsync(cancellationToken);
            attendees = users.Select(item => new AppointmentAttendee(item.Id, DisplayName(item), item.Email ?? string.Empty)).ToList();
        }
        var bookings = await _dbContext.EventBookings.AsNoTracking().Include(item => item.ScheduledEvent).ThenInclude(item => item.TeacherUser)
            .Include(item => item.ScheduledEvent).ThenInclude(item => item.CreditConsumptionPolicy)
            .Where(item => item.UserId == attendeeId && item.ScheduledEvent.AppointmentTypeId != null &&
                (actor.IsAdmin || !actor.IsTeacher || attendeeId == actor.UserId || item.ScheduledEvent.TeacherUserId == actor.UserId))
            .OrderByDescending(item => item.ScheduledEvent.StartAtUtc).Take(40).ToListAsync(cancellationToken);
        var ids = bookings.Select(item => item.ScheduledEventId).ToList();
        var requests = await _dbContext.AppointmentBookingRequests.AsNoTracking().Where(item => item.ScheduledEventId != null && ids.Contains(item.ScheduledEventId.Value))
            .ToDictionaryAsync(item => item.ScheduledEventId!.Value, cancellationToken);
        var list = bookings.Select(item =>
        {
            requests.TryGetValue(item.ScheduledEventId, out var request);
            var policy = item.ScheduledEvent.CreditConsumptionPolicy;
            var action = actor.UserId != item.UserId ? policy?.StaffCancellationAction
                : DateTime.UtcNow <= item.ScheduledEvent.StartAtUtc.AddHours(-(policy?.CancellationWindowHours ?? 0)) ? policy?.EarlyCancellationAction : policy?.LateCancellationAction;
            var cancellationMessage = item.ScheduledEvent.RequiredCreditTypeId == null ? "No credits were required for this session."
                : action == null || action == CreditResolutionAction.Return ? "Reserved credits will be returned."
                : "Reserved credits will be consumed under this session's cancellation policy.";
            return new AppointmentBookingItem(item.ScheduledEventId, item.ScheduledEvent.Title, DisplayName(item.ScheduledEvent.TeacherUser),
                item.ScheduledEvent.StartAtUtc, item.ScheduledEvent.EndAtUtc, item.Status.ToString(), request?.Status, request?.Message,
                item.Status == EventBookingStatus.Confirmed && item.ScheduledEvent.Status == ScheduleEventStatus.Scheduled &&
                item.ScheduledEvent.StartAtUtc > DateTime.UtcNow && (request == null || request.Status == "Confirmed"), cancellationMessage, request?.Id);
        }).ToList();
        List<AppointmentSetupIssue> issues = [];
        if (actor.IsAdmin || actor.IsTeacher)
        {
            var pending = await _dbContext.AppointmentBookingRequests.AsNoTracking().Include(item => item.ScheduledEvent)
                .Where(item => (item.Status == "Preparing" || item.Status == "ReviewRequired") && (actor.IsAdmin || item.TeacherUserId == actor.UserId))
                .OrderBy(item => item.CreatedAtUtc).Take(20).ToListAsync(cancellationToken);
            var userIds = pending.Select(item => item.AttendeeUserId).ToList();
            var users = await _dbContext.Users.AsNoTracking().Where(item => userIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
            issues = pending.Select(item => new AppointmentSetupIssue(item.Id, item.ScheduledEventId ?? 0, item.ScheduledEvent?.Title ?? "Private session",
                users.TryGetValue(item.AttendeeUserId, out var user) ? DisplayName(user) : "Attendee", item.StartAtUtc, item.Status, item.Message, item.ScheduledEvent?.TeacherNameSnapshot ?? "Teacher")).ToList();
        }
        return new(true, attendeeId, DisplayName(attendee), options, attendees, list, issues);
    }

    public Task<OperationResult> BookAppointmentAsync(ScheduleActor actor, int appointmentTypeId, string teacherUserId, DateTime startUtc, CancellationToken cancellationToken = default)
        => BookAppointmentAsync(actor, new(Guid.NewGuid(), appointmentTypeId, teacherUserId, startUtc), cancellationToken);

    public async Task<OperationResult> BookAppointmentAsync(ScheduleActor actor, AppointmentBookingInput input, CancellationToken cancellationToken = default)
    {
        if (input.RequestId == Guid.Empty || input.AppointmentTypeId <= 0 || string.IsNullOrWhiteSpace(input.TeacherUserId)) return Failure("Select an event type, teacher, and time.");
        var attendeeId = await ResolveAttendeeAsync(actor, input.AttendeeUserId, cancellationToken);
        if (attendeeId == null) return Failure("You cannot book for this attendee.");
        if (attendeeId == input.TeacherUserId) return Failure("Choose another teacher for your own session.");
        if (!actor.IsAdmin && actor.IsTeacher && attendeeId != actor.UserId && input.TeacherUserId != actor.UserId)
            return Failure("Teachers can book for students only with themselves as the teacher.");
        var startUtc = input.StartUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(input.StartUtc, DateTimeKind.Utc) : input.StartUtc.ToUniversalTime();
        await using var requestLock = new CheckoutDatabaseLock(_dbContext, "appointment-request:" + input.RequestId);
        if (!await requestLock.TryAcquireAsync(cancellationToken)) return Failure("This booking is already being processed. Please check your sessions shortly.");
        await using var people = new ScheduleDatabaseLock(_dbContext);
        await people.AcquireAsync([attendeeId, input.TeacherUserId], cancellationToken);
        var existing = await _dbContext.AppointmentBookingRequests.AsNoTracking().Include(item => item.ScheduledEvent)
            .FirstOrDefaultAsync(item => item.Id == input.RequestId, cancellationToken);
        if (existing != null)
        {
            if (existing.RequestedByUserId != actor.UserId || existing.AttendeeUserId != attendeeId || existing.TeacherUserId != input.TeacherUserId ||
                existing.AppointmentTypeId != input.AppointmentTypeId || existing.StartAtUtc != startUtc)
                return Failure("This booking request does not match. Reload the booking page.");
            return existing.Status == "Confirmed" && existing.ScheduledEvent?.Status == ScheduleEventStatus.Scheduled ? Success()
                : Failure(existing.Status == "Failed" ? existing.Message ?? "This booking was not completed. Choose a time again."
                    : existing.ScheduledEvent?.Status == ScheduleEventStatus.Cancelled ? "This booking has been cancelled."
                    : "Meeting setup is awaiting confirmation. Please contact the company before trying this booking again.");
        }
        var type = await AppointmentTypesQuery().FirstOrDefaultAsync(item => item.Id == input.AppointmentTypeId && item.IsActive, cancellationToken);
        if (type == null || !type.Teachers.Any(item => item.TeacherUserId == input.TeacherUserId) ||
            !await ValidTeacherIdsQuery().AnyAsync(id => id == input.TeacherUserId, cancellationToken)) return Failure("This event type is no longer available with that teacher.");
        if (type.RequiredCreditTypeId.HasValue && (type.RequiredCreditType?.IsActive != true || type.CreditConsumptionPolicy?.IsActive != true))
            return Failure("This event type's credit settings are unavailable. Please contact the company.");
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, AmsterdamZone()));
        var slots = await GetSlotsAsync(actor, type.Id, input.TeacherUserId, date, 1, cancellationToken, attendeeId);
        if (!slots.Any(item => item.IsAvailable && item.StartUtc == startUtc)) return Failure("That time is no longer available. Choose another slot.");
        var host = type.Teachers.Single(item => item.TeacherUserId == input.TeacherUserId).TeacherUser;
        var meeting = new ScheduledEvent
        {
            AppointmentTypeId = type.Id,
            TeacherUserId = host.Id,
            Title = type.Name,
            CourseNameSnapshot = "Private appointment",
            ClassNameSnapshot = type.Name,
            TeacherNameSnapshot = host.FullName,
            StartAtUtc = startUtc,
            EndAtUtc = startUtc.AddMinutes(type.DurationMinutes),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
            Capacity = 1,
            Source = ScheduleEventSource.StudentAppointment,
            DeliveryType = type.DeliveryType,
            MeetingUrl = type.MeetingUrl,
            Location = type.Location,
            BookingOpensAtUtc = DateTime.UtcNow,
            BookingClosesAtUtc = startUtc,
            BookingAccess = type.RequiredCreditTypeId.HasValue ? CourseClassBookingAccess.Credit : CourseClassBookingAccess.Free,
            RequiredCreditTypeId = type.RequiredCreditTypeId,
            CreditCost = type.CreditCost,
            CreditConsumptionPolicyId = type.CreditConsumptionPolicyId,
            IsVisibleForStudentBooking = false
        };
        var request = new AppointmentBookingRequest
        {
            Id = input.RequestId,
            RequestedByUserId = actor.UserId,
            AttendeeUserId = attendeeId,
            TeacherUserId = host.Id,
            AppointmentTypeId = type.Id,
            StartAtUtc = meeting.StartAtUtc,
            EndAtUtc = meeting.EndAtUtc
        };
        var reserved = await _schedulingService.ReserveAppointmentAsync(meeting, request, cancellationToken);
        if (!reserved.Success) return reserved;
        try
        {
            var provisioned = await _integrationService.ProvisionEventAsync(meeting, cancellationToken);
            if (!provisioned.Success)
            {
                if (provisioned.OutcomeUncertain) return await MarkForReviewAsync(request.Id, meeting);
                var cleaned = await _integrationService.CancelEventAsync(meeting, cancellationToken);
                if (!cleaned.Success) return await MarkForReviewAsync(request.Id, meeting);
                var message = provisioned.ErrorMessage ?? "Meeting setup failed. No credits were charged. Choose a time again.";
                await _schedulingService.FailAppointmentAsync(request.Id, message, cancellationToken);
                return Failure(message);
            }
            if (type.DeliveryType == ScheduleDeliveryType.Zoom && string.IsNullOrWhiteSpace(type.MeetingUrl) &&
                (string.IsNullOrWhiteSpace(meeting.ZoomMeetingId) || string.IsNullOrWhiteSpace(meeting.MeetingUrl)))
                return await MarkForReviewAsync(request.Id, meeting);
            await SaveSetupOutcomeAsync(request.Id, meeting, "Confirmed", null, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Appointment request {RequestId} could not confirm its provider outcome.", request.Id);
            return await MarkForReviewAsync(request.Id, meeting);
        }
        try
        {
            await _integrationService.RegisterBookingAsync(meeting.Id, attendeeId, cancellationToken);
            await _integrationService.SyncEventAttendeesAsync(meeting.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Appointment {RequestId} is confirmed; attendee synchronization will retry.", request.Id);
        }
        return Success();
    }

    private async Task<OperationResult> MarkForReviewAsync(Guid requestId, ScheduledEvent meeting)
    {
        const string message = "Meeting setup needs confirmation. Your time and credits are held to prevent duplicate bookings. Please contact the company.";
        try { await SaveSetupOutcomeAsync(requestId, meeting, "ReviewRequired", message, CancellationToken.None); }
        catch (Exception exception) { _logger.LogError(exception, "Could not record provider review for appointment {RequestId}. The durable preparing request remains held.", requestId); }
        return Failure(message);
    }

    private async Task SaveSetupOutcomeAsync(Guid requestId, ScheduledEvent details, string status, string? message, CancellationToken ct)
    {
        await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            var request = await _dbContext.AppointmentBookingRequests.Include(item => item.ScheduledEvent).SingleAsync(item => item.Id == requestId, ct);
            if (request.Status == "Confirmed") return;
            var target = request.ScheduledEvent!;
            target.MeetingUrl = details.MeetingUrl;
            target.ZoomMeetingId = details.ZoomMeetingId;
            target.ZoomMeetingUuid = details.ZoomMeetingUuid;
            target.GoogleCalendarEventId = details.GoogleCalendarEventId;
            target.GoogleCalendarId = details.GoogleCalendarId;
            target.ExternalSyncedAtUtc = details.ExternalSyncedAtUtc;
            target.ExternalSyncError = message;
            request.Status = status;
            request.Message = message;
            request.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        });
    }

    public async Task<OperationResult> CancelAppointmentAsync(ScheduleActor actor, int scheduledEventId, CancellationToken cancellationToken = default)
    {
        var meeting = await _dbContext.ScheduledEvents.AsNoTracking().Include(item => item.Bookings)
            .FirstOrDefaultAsync(item => item.Id == scheduledEventId && item.AppointmentTypeId != null, cancellationToken);
        if (meeting == null) return Failure("This session could not be found.");
        if (meeting.StartAtUtc <= DateTime.UtcNow) return Failure("A session cannot be cancelled after it starts.");
        var booking = meeting.Bookings.SingleOrDefault();
        if (booking == null || (booking.UserId != actor.UserId && !actor.IsAdmin && !(actor.IsTeacher && meeting.TeacherUserId == actor.UserId)))
            return Failure("You cannot cancel this session.");
        return booking.UserId == actor.UserId
            ? await _schedulingService.CancelBookingAsync(new(booking.UserId, false, false), meeting.Id, cancellationToken)
            : await _schedulingService.CancelEventAsync(actor, meeting.Id, "Cancelled by staff", cancellationToken);
    }

    public async Task<OperationResult> ResolveSetupAsync(ScheduleActor actor, Guid requestId, bool providerCleanupConfirmed, CancellationToken cancellationToken = default)
    {
        if (!actor.IsAdmin || !providerCleanupConfirmed) return Failure("An administrator must verify that no external meeting remains before releasing this booking.");
        await using var requestLock = new CheckoutDatabaseLock(_dbContext, "appointment-request:" + requestId);
        if (!await requestLock.TryAcquireAsync(cancellationToken)) return Failure("Meeting setup is still being processed. Wait for its outcome before reviewing it.");
        var request = await _dbContext.AppointmentBookingRequests.AsNoTracking().Include(item => item.ScheduledEvent).FirstOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request == null) return Failure("This booking request could not be found.");
        await using var people = new ScheduleDatabaseLock(_dbContext);
        await people.AcquireAsync([request.TeacherUserId, request.AttendeeUserId], cancellationToken);
        if (request.Status == "Failed") return Success();
        if (request.Status == "Confirmed") return Failure("This session is confirmed. Use its normal cancellation action.");
        if (request.ScheduledEvent != null)
        {
            var cleanup = await _integrationService.CancelEventAsync(request.ScheduledEvent, cancellationToken);
            if (!cleanup.Success) return Failure("Provider cleanup could not be confirmed. The time and credits remain held.");
        }
        return await _schedulingService.FailAppointmentAsync(requestId, "Setup cancelled after company review. Any reserved credits were returned.", cancellationToken, actor.UserId);
    }

    private static DateTime WindowBoundaryUtc(DateTime local, TimeZoneInfo zone, bool earliest)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (!zone.IsAmbiguousTime(local)) return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        var alternatives = zone.GetAmbiguousTimeOffsets(local).Select(offset => new DateTimeOffset(local, offset).UtcDateTime);
        return earliest ? alternatives.Min() : alternatives.Max();
    }

    private static string DisplayName(ApplicationUser user) => string.IsNullOrWhiteSpace(user.FullName) ? user.Email ?? "Account" : user.FullName;
}
