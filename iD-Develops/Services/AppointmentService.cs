using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class AppointmentService : IAppointmentService
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
                return Failure("You do not have permission to manage appointment types.");
            var name = input.Name?.Trim() ?? string.Empty;
            if (name.Length is < 2 or > 150)
                return Failure("Enter an appointment name between 2 and 150 characters.");
            if (input.DurationMinutes is < 5 or > 480)
                return Failure("Duration must be between 5 minutes and 8 hours.");
            if (input.CreditCost is < 1 or > 1000)
                return Failure("Credit cost must be between 1 and 1,000.");
            if (!await _dbContext.CreditTypes.AnyAsync(item => item.Id == input.RequiredCreditTypeId && item.IsActive, cancellationToken))
                return Failure("Select an active required credit.");
            if (!await _dbContext.CreditConsumptionPolicies.AnyAsync(item => item.Id == input.CreditConsumptionPolicyId && item.IsActive, cancellationToken))
                return Failure("Select an active credit policy.");

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
                    .FirstOrDefaultAsync(item => item.Id == input.Id, cancellationToken)
                    ?? throw new InvalidOperationException("Appointment type no longer exists.");
                if (!actor.IsAdmin && entity.CreatedByUserId != actor.UserId)
                    return Failure("You cannot edit this appointment type.");
                _dbContext.AppointmentTypeTeachers.RemoveRange(entity.Teachers);
            }
            else
            {
                entity = new AppointmentType { CreatedByUserId = actor.UserId, CreatedAtUtc = DateTime.UtcNow };
                _dbContext.AppointmentTypes.Add(entity);
            }

            entity.Name = name;
            entity.DurationMinutes = input.DurationMinutes;
            entity.RequiredCreditTypeId = input.RequiredCreditTypeId;
            entity.CreditCost = input.CreditCost;
            entity.CreditConsumptionPolicyId = input.CreditConsumptionPolicyId;
            entity.IsActive = input.IsActive;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.Teachers = teacherIds.Select(id => new AppointmentTypeTeacher { TeacherUserId = id }).ToList();
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
                return Failure("Appointment type no longer exists.");
            if (!actor.IsAdmin && (!actor.IsTeacher || entity.CreatedByUserId != actor.UserId))
                return Failure("You cannot deactivate this appointment type.");
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
                .Where(lot => lot.UserId == actor.UserId && lot.Scope == CreditGrantScope.Global && lot.RemainingQuantity > 0 &&
                              (!lot.ExpiresAtUtc.HasValue || lot.ExpiresAtUtc > now))
                .GroupBy(lot => lot.CreditTypeId)
                .Select(group => new { CreditTypeId = group.Key, Quantity = group.Sum(lot => lot.RemainingQuantity) })
                .ToDictionaryAsync(item => item.CreditTypeId, item => item.Quantity, cancellationToken);
            return types.Select(item => Map(item, false) with
            {
                AvailableCreditQuantity = balances.GetValueOrDefault(item.RequiredCreditTypeId)
            }).ToList();
        }

        public async Task<IReadOnlyList<AppointmentSlot>> GetSlotsAsync(
            ScheduleActor actor,
            int appointmentTypeId,
            string teacherUserId,
            DateOnly startDate,
            int days,
            CancellationToken cancellationToken = default)
        {
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
                    var cursor = date.ToDateTime(window.LocalStartTime);
                    var windowEnd = date.ToDateTime(window.LocalEndTime);
                    while (cursor.AddMinutes(type.DurationMinutes) <= windowEnd)
                    {
                        if (!zone.IsInvalidTime(cursor))
                        {
                            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cursor, DateTimeKind.Unspecified), zone);
                            var endUtc = startUtc.AddMinutes(type.DurationMinutes);
                            var reason = startUtc <= DateTime.UtcNow.AddHours(1)
                                ? "Too close to the start time"
                                : conflicts.Any(item => item.StartAtUtc < endUtc && item.EndAtUtc > startUtc)
                                    ? "Already scheduled"
                                    : !external.Success
                                        ? "Calendar availability unavailable"
                                        : external.Periods.Any(item => item.StartUtc < endUtc && item.EndUtc > startUtc)
                                            ? "Busy in Google Calendar"
                                            : null;
                            slots.Add(new AppointmentSlot(startUtc, endUtc, reason == null, reason));
                        }
                        cursor = cursor.AddMinutes(SlotIntervalMinutes);
                    }
                }
            }
            return slots.OrderBy(item => item.StartUtc).ToList();
        }

        public async Task<OperationResult> BookAppointmentAsync(
            ScheduleActor actor,
            int appointmentTypeId,
            string teacherUserId,
            DateTime startUtc,
            CancellationToken cancellationToken = default)
        {
            startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
            var type = await AppointmentTypesQuery()
                .FirstOrDefaultAsync(item => item.Id == appointmentTypeId && item.IsActive, cancellationToken);
            if (type == null || !type.Teachers.Any(item => item.TeacherUserId == teacherUserId))
                return Failure("This appointment is no longer available with that teacher.");
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, AmsterdamZone()));
            var slots = await GetSlotsAsync(actor, appointmentTypeId, teacherUserId, date, 1, cancellationToken);
            if (!slots.Any(item => item.IsAvailable && item.StartUtc == startUtc))
                return Failure("That time is no longer available. Choose another slot.");
            var balance = await _dbContext.UserCreditLots.AsNoTracking()
                .Where(lot => lot.UserId == actor.UserId && lot.CreditTypeId == type.RequiredCreditTypeId &&
                              lot.Scope == CreditGrantScope.Global && lot.RemainingQuantity > 0 &&
                              (!lot.ExpiresAtUtc.HasValue || lot.ExpiresAtUtc > DateTime.UtcNow))
                .SumAsync(lot => lot.RemainingQuantity, cancellationToken);
            if (balance < type.CreditCost)
                return Failure($"You need {type.CreditCost} {type.RequiredCreditType.PluralLabel} to book this appointment.");
            var connections = await _integrationService.ValidateTeacherConnectionsAsync(
                teacherUserId,
                ScheduleDeliveryType.Zoom,
                requiresGeneratedZoom: true,
                cancellationToken);
            if (!connections.Success)
                return connections;

            var teacher = type.Teachers.First(item => item.TeacherUserId == teacherUserId).TeacherUser;
            var scheduleEvent = new ScheduledEvent
            {
                AppointmentTypeId = type.Id,
                TeacherUserId = teacherUserId,
                Title = type.Name,
                CourseNameSnapshot = "Private appointment",
                ClassNameSnapshot = type.Name,
                TeacherNameSnapshot = teacher.FullName,
                StartAtUtc = startUtc,
                EndAtUtc = startUtc.AddMinutes(type.DurationMinutes),
                TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
                Capacity = 1,
                Source = ScheduleEventSource.StudentAppointment,
                DeliveryType = ScheduleDeliveryType.Zoom,
                BookingOpensAtUtc = DateTime.UtcNow,
                BookingClosesAtUtc = startUtc,
                BookingAccess = CourseClassBookingAccess.Credit,
                RequiredCreditTypeId = type.RequiredCreditTypeId,
                CreditCost = type.CreditCost,
                CreditConsumptionPolicyId = type.CreditConsumptionPolicyId
            };
            var provisioned = await _integrationService.ProvisionEventAsync(scheduleEvent, cancellationToken);
            if (!provisioned.Success)
                return provisioned;
            _dbContext.ScheduledEvents.Add(scheduleEvent);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                var booked = await _schedulingService.BookAsync(actor, scheduleEvent.Id, cancellationToken);
                if (booked.Success)
                    return booked;
                await CleanupExternalEventAsync(scheduleEvent, cancellationToken);
                return booked;
            }
            catch (DbUpdateException)
            {
                _logger.LogDebug("Appointment slot was taken before it could be saved for teacher {TeacherUserId}.", teacherUserId);
                await CleanupExternalEventAsync(scheduleEvent, cancellationToken);
                return Failure("That time was just booked by someone else. Choose another slot.");
            }
            catch
            {
                await CleanupExternalEventAsync(scheduleEvent, cancellationToken);
                throw;
            }
        }

        private async Task CleanupExternalEventAsync(ScheduledEvent scheduleEvent, CancellationToken cancellationToken)
        {
            try { await _integrationService.CancelEventAsync(scheduleEvent, cancellationToken); }
            catch (Exception exception) { _logger.LogWarning(exception, "External appointment cleanup failed for {EventId}.", scheduleEvent.Id); }
            if (scheduleEvent.Id > 0)
            {
                _dbContext.ScheduledEvents.Remove(scheduleEvent);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                _dbContext.Entry(scheduleEvent).State = EntityState.Detached;
            }
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
            return _dbContext.UserRoles.Where(item => roleIds.Contains(item.RoleId)).Select(item => item.UserId).Distinct();
        }

        private static AppointmentTypeItem Map(AppointmentType item, bool canEdit)
            => new(
                item.Id,
                item.Name,
                item.DurationMinutes,
                item.RequiredCreditTypeId,
                item.RequiredCreditType.Name,
                item.CreditCost == 1 ? item.RequiredCreditType.SingularLabel : item.RequiredCreditType.PluralLabel,
                item.CreditCost,
                item.CreditConsumptionPolicyId,
                item.CreditConsumptionPolicy.Name,
                item.IsActive,
                canEdit,
                item.Teachers.Select(teacher => new AppointmentTeacherItem(
                    teacher.TeacherUserId,
                    teacher.TeacherUser.FullName,
                    teacher.TeacherUser.Email ?? teacher.TeacherUser.UserName ?? string.Empty)).ToList());

        private static TimeZoneInfo AmsterdamZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
        }

        private static OperationResult Success() => new() { Success = true };
        private static OperationResult Failure(string error) => new() { Success = false, ErrorMessage = error };
    }
}
