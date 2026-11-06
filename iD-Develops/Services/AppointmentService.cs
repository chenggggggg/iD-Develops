using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed partial class AppointmentService : IAppointmentService
    {
        private const int SlotIntervalMinutes = 30;
        private readonly ApplicationDbContext _dbContext;
        private readonly ISchedulingService _schedulingService;
        private readonly ISchedulingIntegrationService _integrationService;
        private readonly ILogger<AppointmentService> _logger;

        public AppointmentService(
            ApplicationDbContext dbContext,
            ISchedulingService schedulingService,
            ISchedulingIntegrationService integrationService,
            ILogger<AppointmentService> logger)
        {
            _dbContext = dbContext;
            _schedulingService = schedulingService;
            _integrationService = integrationService;
            _logger = logger;
        }

        public async Task<AppointmentManagementData> GetManagementDataAsync(
            ScheduleActor actor,
            CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin && !actor.IsTeacher)
                return new([], [], [], []);

            var types = await AppointmentTypesQuery()
                .Where(item => actor.IsAdmin || item.CreatedByUserId == actor.UserId)
                .OrderBy(item => item.Name)
                .ToListAsync(cancellationToken);
            var teacherOptions = await GetTeacherOptionsAsync(actor, cancellationToken);
            var credits = await _dbContext.CreditTypes.AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => item.Name)
                .Select(item => new CreditOption(item.Id, item.Name, item.SingularLabel, item.PluralLabel))
                .ToListAsync(cancellationToken);
            var policies = await _dbContext.CreditConsumptionPolicies.AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => item.Name)
                .Select(item => new CreditPolicyOption(item.Id, item.Name))
                .ToListAsync(cancellationToken);
            return new(
                types.Select(item => Map(item, actor.IsAdmin || item.CreatedByUserId == actor.UserId)).ToList(),
                teacherOptions,
                credits,
                policies);
        }

        public async Task<OperationResult> SaveAppointmentTypeAsync(
            ScheduleActor actor,
            AppointmentTypeInput input,
            CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin && !actor.IsTeacher)
                return Failure("You do not have permission to manage event types.");
            if (input.Id < 0)
                return Failure("This event type no longer exists.");
            var name = input.Name?.Trim() ?? string.Empty;
            if (name.Length is < 2 or > 150)
                return Failure("Enter an event type name between 2 and 150 characters.");
            if (input.DurationMinutes is < 5 or > 480)
                return Failure("Duration must be between 5 minutes and 8 hours.");
            if (!Enum.IsDefined(input.DeliveryType)) return Failure("Select a valid delivery type.");
            var location = input.Location?.Trim();
            var meetingUrl = input.MeetingUrl?.Trim();
            if (location?.Length > 300) return Failure("Location must be 300 characters or fewer.");
            if (!string.IsNullOrEmpty(meetingUrl) && (meetingUrl.Length > 1024 || !Uri.TryCreate(meetingUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")))
                return Failure("Enter a valid HTTP or HTTPS meeting link.");
            if (input.DeliveryType == ScheduleDeliveryType.InPerson && string.IsNullOrWhiteSpace(location)) return Failure("Enter a location for an in-person session.");
            if (input.DeliveryType == ScheduleDeliveryType.OtherOnline && string.IsNullOrWhiteSpace(meetingUrl)) return Failure("Enter a meeting link for an online session.");
            var requiresCredits = input.RequiredCreditTypeId.HasValue;
            if (requiresCredits)
            {
                if (input.CreditCost is < 1 or > 1000)
                    return Failure("Credit cost must be between 1 and 1,000.");
                if (!await _dbContext.CreditTypes.AnyAsync(item => item.Id == input.RequiredCreditTypeId && item.IsActive, cancellationToken))
                    return Failure("Select an active required credit.");
                if (!await _dbContext.CreditConsumptionPolicies.AnyAsync(item => item.Id == input.CreditConsumptionPolicyId && item.IsActive, cancellationToken))
                    return Failure("Select an active credit policy.");
            }

            var teacherIds = actor.IsAdmin
                ? input.TeacherUserIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().ToList()
                : [actor.UserId];
            if (teacherIds.Count == 0)
                return Failure("Assign at least one teacher.");
            var validTeacherIds = await ValidTeacherIdsQuery().Where(id => teacherIds.Contains(id)).ToListAsync(cancellationToken);
            if (validTeacherIds.Count != teacherIds.Count)
                return Failure("One or more assigned teachers are invalid.");

            AppointmentType entity;
            if (input.Id > 0)
            {
                entity = await _dbContext.AppointmentTypes
                    .Include(item => item.Teachers)
                    .FirstOrDefaultAsync(item => item.Id == input.Id, cancellationToken)!;
                if (entity == null)
                    return Failure("This event type no longer exists.");
                if (!actor.IsAdmin && entity.CreatedByUserId != actor.UserId)
                    return Failure("You cannot edit this event type.");
            }
            else
            {
                entity = new AppointmentType { CreatedByUserId = actor.UserId, CreatedAtUtc = DateTime.UtcNow };
                _dbContext.AppointmentTypes.Add(entity);
            }

            entity.Name = name;
            entity.DurationMinutes = input.DurationMinutes;
            entity.DeliveryType = input.DeliveryType;
            entity.Location = string.IsNullOrEmpty(location) ? null : location;
            entity.MeetingUrl = input.DeliveryType == ScheduleDeliveryType.InPerson || string.IsNullOrEmpty(meetingUrl) ? null : meetingUrl;
            entity.RequiredCreditTypeId = input.RequiredCreditTypeId;
            entity.CreditCost = requiresCredits ? input.CreditCost : 0;
            entity.CreditConsumptionPolicyId = requiresCredits ? input.CreditConsumptionPolicyId : null;
            entity.IsActive = input.IsActive;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            if (actor.IsAdmin || input.Id == 0)
            {
                _dbContext.AppointmentTypeTeachers.RemoveRange(entity.Teachers.Where(item => !teacherIds.Contains(item.TeacherUserId)));
                foreach (var id in teacherIds.Where(id => entity.Teachers.All(item => item.TeacherUserId != id)))
                    entity.Teachers.Add(new AppointmentTypeTeacher { TeacherUserId = id });
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> DeactivateAppointmentTypeAsync(
            ScheduleActor actor,
            int appointmentTypeId,
            CancellationToken cancellationToken = default)
        {
            var entity = await _dbContext.AppointmentTypes.FirstOrDefaultAsync(item => item.Id == appointmentTypeId, cancellationToken);
            if (entity == null)
                return Failure("Event type no longer exists.");
            if (!actor.IsAdmin && (!actor.IsTeacher || entity.CreatedByUserId != actor.UserId))
                return Failure("You cannot deactivate this event type.");
            entity.IsActive = false;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<IReadOnlyList<AppointmentTypeItem>> GetStudentOptionsAsync(
            ScheduleActor actor,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var types = await AppointmentTypesQuery().Where(item => item.IsActive).OrderBy(item => item.Name).ToListAsync(cancellationToken);
            var balances = await _dbContext.UserCreditLots.AsNoTracking()
                .Where(lot => lot.UserId == actor.UserId && lot.Scope == CreditGrantScope.Global && !lot.RefundBlocked && lot.RemainingQuantity > 0 &&
                              (!lot.ExpiresAtUtc.HasValue || lot.ExpiresAtUtc > now))
                .GroupBy(lot => lot.CreditTypeId)
                .Select(group => new { CreditTypeId = group.Key, Quantity = group.Sum(lot => lot.RemainingQuantity) })
                .ToDictionaryAsync(item => item.CreditTypeId, item => item.Quantity, cancellationToken);
            return types.Select(item => Map(item, false) with
            {
                MeetingUrl = null,
                AvailableCreditQuantity = item.RequiredCreditTypeId.HasValue ? balances.GetValueOrDefault(item.RequiredCreditTypeId.Value) : 0
            }).ToList();
        }

        public async Task<IReadOnlyList<AppointmentSlot>> GetSlotsAsync(
            ScheduleActor actor,
            int appointmentTypeId,
            string teacherUserId,
            DateOnly startDate,
            int days,
            CancellationToken cancellationToken = default,
            string? attendeeUserId = null)
        {
            var attendeeId = await ResolveAttendeeAsync(actor, attendeeUserId, cancellationToken);
            if (attendeeId == null || attendeeId == teacherUserId || (!actor.IsAdmin && actor.IsTeacher && attendeeId != actor.UserId && teacherUserId != actor.UserId)) return [];
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AmsterdamZone()));
            if (startDate < today || startDate.DayNumber > DateOnly.MaxValue.DayNumber - 31) return [];
            days = Math.Clamp(days, 1, 31);
            var type = await _dbContext.AppointmentTypes.AsNoTracking()
                .Include(item => item.Teachers)
                .FirstOrDefaultAsync(item => item.Id == appointmentTypeId && item.IsActive, cancellationToken);
            if (type == null || !type.Teachers.Any(item => item.TeacherUserId == teacherUserId))
                return [];

            var zone = AmsterdamZone();
            var rangeStartLocal = startDate.ToDateTime(TimeOnly.MinValue);
            var rangeEndLocal = startDate.AddDays(days).ToDateTime(TimeOnly.MinValue);
            var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, zone);
            var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(rangeEndLocal, zone);
            var windows = await _dbContext.TeacherAvailabilityWindows.AsNoTracking()
                .Where(item => item.TeacherUserId == teacherUserId)
                .ToListAsync(cancellationToken);
            var conflicts = await _dbContext.ScheduledEvents.AsNoTracking()
                .Where(item => item.TeacherUserId == teacherUserId && item.Status == ScheduleEventStatus.Scheduled &&
                               item.StartAtUtc < rangeEndUtc && item.EndAtUtc > rangeStartUtc)
                .Select(item => new { item.StartAtUtc, item.EndAtUtc })
                .ToListAsync(cancellationToken);
            var attendeeConflicts = await _dbContext.EventBookings.AsNoTracking().Where(item => item.UserId == attendeeId &&
                (item.Status == EventBookingStatus.Confirmed || item.Status == EventBookingStatus.Attended) && item.ScheduledEvent.Status == ScheduleEventStatus.Scheduled &&
                item.ScheduledEvent.StartAtUtc < rangeEndUtc && item.ScheduledEvent.EndAtUtc > rangeStartUtc)
                .Select(item => new { item.ScheduledEvent.StartAtUtc, item.ScheduledEvent.EndAtUtc }).ToListAsync(cancellationToken);
            var hosting = await _dbContext.ScheduledEvents.AsNoTracking().Where(item => item.TeacherUserId == attendeeId && item.Status == ScheduleEventStatus.Scheduled &&
                item.StartAtUtc < rangeEndUtc && item.EndAtUtc > rangeStartUtc).Select(item => new { item.StartAtUtc, item.EndAtUtc }).ToListAsync(cancellationToken);
            var readiness = await _integrationService.ValidateTeacherConnectionsAsync(teacherUserId, type.DeliveryType, type.DeliveryType == ScheduleDeliveryType.Zoom && string.IsNullOrWhiteSpace(type.MeetingUrl), cancellationToken);
            var external = await _integrationService.GetBusyPeriodsAsync(
                teacherUserId,
                rangeStartUtc,
                rangeEndUtc,
                cancellationToken);

            var slots = new List<AppointmentSlot>();
            for (var offset = 0; offset < days; offset++)
            {
                var date = startDate.AddDays(offset);
                foreach (var window in windows.Where(item => item.DayOfWeek == date.DayOfWeek))
                {
                    var windowStart = WindowBoundaryUtc(date.ToDateTime(window.LocalStartTime), zone, earliest: true);
                    var windowEnd = WindowBoundaryUtc(date.ToDateTime(window.LocalEndTime), zone, earliest: false);
                    for (var startUtc = windowStart; startUtc.AddMinutes(type.DurationMinutes) <= windowEnd; startUtc = startUtc.AddMinutes(SlotIntervalMinutes))
                    {
                        var endUtc = startUtc.AddMinutes(type.DurationMinutes);
                        var reason = startUtc <= DateTime.UtcNow.AddHours(1) ? "Too close to the start time"
                            : conflicts.Any(item => item.StartAtUtc < endUtc && item.EndAtUtc > startUtc) ? "Already scheduled"
                            : attendeeConflicts.Concat(hosting).Any(item => item.StartAtUtc < endUtc && item.EndAtUtc > startUtc) ? "The attendee already has a meeting at this time"
                            : !readiness.Success ? "The teacher is not ready for this booking"
                            : !external.Success ? "Calendar availability unavailable"
                            : external.Periods.Any(item => item.StartUtc < endUtc && item.EndUtc > startUtc) ? "Busy in Google Calendar"
                            : null;
                        slots.Add(new(startUtc, endUtc, reason == null, reason));
                    }
                }
            }
            return slots.DistinctBy(item => item.StartUtc).OrderBy(item => item.StartUtc).ToList();
        }

        private IQueryable<AppointmentType> AppointmentTypesQuery()
            => _dbContext.AppointmentTypes.AsNoTracking().AsSplitQuery()
                .Include(item => item.RequiredCreditType)
                .Include(item => item.CreditConsumptionPolicy)
                .Include(item => item.Teachers).ThenInclude(item => item.TeacherUser);

        private async Task<IReadOnlyList<AppointmentTeacherItem>> GetTeacherOptionsAsync(ScheduleActor actor, CancellationToken cancellationToken)
        {
            var ids = actor.IsAdmin ? await ValidTeacherIdsQuery().ToListAsync(cancellationToken) : [actor.UserId];
            return await _dbContext.Users.AsNoTracking().Where(user => ids.Contains(user.Id) && !user.IsDeleted)
                .OrderBy(user => user.FirstName).ThenBy(user => user.LastName)
                .Select(user => new AppointmentTeacherItem(user.Id, user.FirstName + " " + user.LastName, user.Email ?? user.UserName ?? string.Empty))
                .ToListAsync(cancellationToken);
        }

        private IQueryable<string> ValidTeacherIdsQuery()
        {
            var roleIds = _dbContext.Roles.Where(role => role.Name == "Teacher" || role.Name == "Admin" || role.Name == "SuperAdmin").Select(role => role.Id);
            var users = _dbContext.Users.Where(user => !user.IsDeleted).Select(user => user.Id);
            return _dbContext.UserRoles.Where(item => roleIds.Contains(item.RoleId) && users.Contains(item.UserId)).Select(item => item.UserId).Distinct();
        }

        private static AppointmentTypeItem Map(AppointmentType item, bool canEdit)
            => new(
                item.Id,
                item.Name,
                item.DurationMinutes,
                item.RequiredCreditTypeId,
                item.RequiredCreditType?.Name ?? string.Empty,
                (item.CreditCost == 1 ? item.RequiredCreditType?.SingularLabel : item.RequiredCreditType?.PluralLabel) ?? string.Empty,
                item.CreditCost,
                item.CreditConsumptionPolicyId,
                item.CreditConsumptionPolicy?.Name ?? string.Empty,
                item.IsActive,
                canEdit,
                item.Teachers.Select(teacher => new AppointmentTeacherItem(
                    teacher.TeacherUserId,
                    teacher.TeacherUser.FullName,
                    teacher.TeacherUser.Email ?? teacher.TeacherUser.UserName ?? string.Empty)).ToList(),
                DeliveryType: item.DeliveryType, MeetingUrl: item.MeetingUrl, Location: item.Location);

        private static TimeZoneInfo AmsterdamZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
        }

        private static OperationResult Success() => new() { Success = true };
        private static OperationResult Failure(string error) => new() { Success = false, ErrorMessage = error };
    }
}
