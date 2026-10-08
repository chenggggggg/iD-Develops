using System.Data;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Utilities;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Services
{
    public sealed class SchedulingService : ISchedulingService
    {
        public const string AmsterdamTimeZoneId = "Europe/Amsterdam";
        private const int JoinWindowMinutesBeforeStart = 0;
        private const int JoinWindowMinutesAfterEnd = 60;
        private static readonly EventBookingStatus[] ActiveBookingStatuses =
            [EventBookingStatus.Confirmed, EventBookingStatus.Attended];
        private readonly ApplicationDbContext _dbContext;
        private readonly ISchedulingIntegrationService _schedulingIntegrationService;
        private readonly ILogger<SchedulingService> _logger;

        public SchedulingService(
            ApplicationDbContext dbContext,
            ISchedulingIntegrationService schedulingIntegrationService,
            ILogger<SchedulingService> logger)
        {
            _dbContext = dbContext;
            _schedulingIntegrationService = schedulingIntegrationService;
            _logger = logger;
        }

        public async Task<ScheduleRosterPageData> GetRosterPageAsync(
            ScheduleActor actor,
            CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin && !actor.IsTeacher)
            {
                return new ScheduleRosterPageData(
                    Array.Empty<ScheduleRosterListItem>(),
                    Array.Empty<ScheduleClassOption>(),
                    Array.Empty<ScheduleTeacherOption>(),
                    Array.Empty<ScheduleStudentOption>(),
                    Array.Empty<TeacherAvailabilityListItem>());
            }

            var now = DateTime.UtcNow;
            var ruleQuery = _dbContext.ScheduleRosterRules
                .AsNoTracking()
                .Where(rule => actor.IsAdmin || rule.TeacherUserId == actor.UserId);
            var ruleRows = await ruleQuery
                .OrderBy(rule => rule.TeacherUser.FirstName)
                .ThenBy(rule => rule.TeacherUser.LastName)
                .ThenBy(rule => rule.DayOfWeek)
                .ThenBy(rule => rule.LocalStartTime)
                .Select(rule => new
                {
                    rule.Id,
                    rule.TeacherUserId,
                    rule.TeacherUser.FirstName,
                    rule.TeacherUser.LastName,
                    rule.TeacherUser.UserName,
                    rule.TeacherUser.Email,
                    rule.CourseClassId,
                    CourseName = rule.CourseClass.CourseSection.Course.Name,
                    ClassTitle = rule.CourseClass.Title,
                    rule.DayOfWeek,
                    rule.LocalStartTime,
                    rule.TimeZoneId,
                    rule.ActiveFromDate,
                    rule.ActiveUntilDate,
                    rule.DurationMinutes,
                    rule.Capacity,
                    rule.DeliveryType,
                    rule.MeetingUrl,
                    rule.Location,
                    rule.BookingOpenDaysBefore,
                    rule.BookingCloseHoursBefore,
                    rule.GenerateWeeksAhead,
                    rule.IsVisibleForStudentBooking,
                    rule.IsActive,
                    UpcomingEventCount = rule.ScheduledEvents.Count(scheduleEvent =>
                        scheduleEvent.StartAtUtc >= now &&
                        scheduleEvent.Status == ScheduleEventStatus.Scheduled)
                })
                .ToListAsync(cancellationToken);

            var rules = ruleRows.Select(rule => new ScheduleRosterListItem(
                rule.Id,
                rule.TeacherUserId,
                FormatName(rule.FirstName, rule.LastName, rule.UserName, rule.Email),
                rule.CourseClassId,
                rule.CourseName,
                rule.ClassTitle,
                rule.DayOfWeek,
                rule.LocalStartTime,
                AmsterdamTimeZoneId,
                rule.ActiveFromDate,
                rule.ActiveUntilDate,
                rule.DurationMinutes,
                rule.Capacity,
                rule.DeliveryType,
                rule.MeetingUrl,
                rule.Location,
                rule.BookingOpenDaysBefore,
                rule.BookingCloseHoursBefore,
                rule.GenerateWeeksAhead,
                rule.IsVisibleForStudentBooking,
                rule.IsActive,
                rule.UpcomingEventCount)).ToList();

            var classQuery = _dbContext.CourseClasses.AsNoTracking();
            if (!actor.IsAdmin)
            {
                classQuery = classQuery.Where(courseClass =>
                    courseClass.CourseSection.Course.CreatedByUserId == actor.UserId ||
                    courseClass.CourseSection.Course.Instructors.Any(instructor => instructor.UserId == actor.UserId));
            }

            var classes = await classQuery
                .OrderBy(courseClass => courseClass.CourseSection.Course.Name)
                .ThenBy(courseClass => courseClass.CourseSection.OrderNumber)
                .ThenBy(courseClass => courseClass.OrderNumber)
                .Select(courseClass => new ScheduleClassOption(
                    courseClass.Id,
                    courseClass.CourseSection.CourseId,
                    courseClass.CourseSection.Course.Name,
                    courseClass.CourseSection.Title,
                    courseClass.Title,
                    courseClass.DurationMinutes,
                    courseClass.Capacity,
                    courseClass.Format))
                .ToListAsync(cancellationToken);

            IReadOnlyList<ScheduleTeacherOption> teachers;
            if (actor.IsAdmin)
            {
                var staffRoleIds = await _dbContext.Roles
                    .AsNoTracking()
                    .Where(role => role.Name == "Teacher" || role.Name == "Admin" || role.Name == "SuperAdmin")
                    .Select(role => role.Id)
                    .ToListAsync(cancellationToken);
                var staffUserIds = _dbContext.UserRoles
                    .AsNoTracking()
                    .Where(userRole => staffRoleIds.Contains(userRole.RoleId))
                    .Select(userRole => userRole.UserId);
                var teacherRows = await _dbContext.Users
                    .AsNoTracking()
                    .Where(user => !user.IsDeleted && staffUserIds.Contains(user.Id))
                    .OrderBy(user => user.FirstName)
                    .ThenBy(user => user.LastName)
                    .Select(user => new { user.Id, user.FirstName, user.LastName, user.UserName, user.Email })
                    .ToListAsync(cancellationToken);
                teachers = teacherRows.Select(user => new ScheduleTeacherOption(
                    user.Id,
                    FormatName(user.FirstName, user.LastName, user.UserName, user.Email),
                    user.Email ?? user.UserName ?? string.Empty)).ToList();
            }
            else
            {
                var user = await _dbContext.Users.AsNoTracking().FirstAsync(item => item.Id == actor.UserId, cancellationToken);
                teachers = [new ScheduleTeacherOption(
                    user.Id,
                    FormatName(user.FirstName, user.LastName, user.UserName, user.Email),
                    user.Email ?? user.UserName ?? string.Empty)];
            }

            var studentRoleIds = await _dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Student")
                .Select(role => role.Id)
                .ToListAsync(cancellationToken);
            var studentUserIds = _dbContext.UserRoles
                .AsNoTracking()
                .Where(userRole => studentRoleIds.Contains(userRole.RoleId))
                .Select(userRole => userRole.UserId);
            var students = await _dbContext.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted && user.Email != null && studentUserIds.Contains(user.Id))
                .OrderBy(user => user.Email)
                .Select(user => new ScheduleStudentOption(user.Id, user.Email!))
                .ToListAsync(cancellationToken);

            var teacherIds = teachers.Select(teacher => teacher.Id).ToList();
            var availability = await _dbContext.TeacherAvailabilityWindows
                .AsNoTracking()
                .Where(window => teacherIds.Contains(window.TeacherUserId))
                .OrderBy(window => window.TeacherUserId)
                .ThenBy(window => window.DayOfWeek)
                .Select(window => new TeacherAvailabilityListItem(
                    window.TeacherUserId,
                    window.DayOfWeek,
                    window.LocalStartTime,
                    window.LocalEndTime,
                    AmsterdamTimeZoneId))
                .ToListAsync(cancellationToken);

            return new ScheduleRosterPageData(rules, classes, teachers, students, availability);
        }

        public async Task<IReadOnlyList<ScheduleTeacherOption>> GetTeacherOptionsAsync(
            ScheduleActor actor,
            CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin && !actor.IsTeacher)
                return Array.Empty<ScheduleTeacherOption>();

            var staffRoleIds = await _dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Teacher" || role.Name == "Admin" || role.Name == "SuperAdmin")
                .Select(role => role.Id)
                .ToListAsync(cancellationToken);
            var staffUserIds = _dbContext.UserRoles
                .AsNoTracking()
                .Where(userRole => staffRoleIds.Contains(userRole.RoleId))
                .Select(userRole => userRole.UserId);
            var rows = await _dbContext.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted && staffUserIds.Contains(user.Id))
                .OrderBy(user => user.FirstName)
                .ThenBy(user => user.LastName)
                .Select(user => new { user.Id, user.FirstName, user.LastName, user.UserName, user.Email })
                .ToListAsync(cancellationToken);
            return rows.Select(user => new ScheduleTeacherOption(
                user.Id,
                FormatName(user.FirstName, user.LastName, user.UserName, user.Email),
                user.Email ?? user.UserName ?? string.Empty)).ToList();
        }

        public async Task<OperationResult> SaveAvailabilityAsync(
            ScheduleActor actor,
            TeacherAvailabilityInput input,
            CancellationToken cancellationToken = default)
        {
            input.TimeZoneId = AmsterdamTimeZoneId;
            if (!actor.IsAdmin && !actor.IsTeacher)
                return Failure("You do not have permission to manage teacher availability.");
            if (!actor.IsAdmin &&
                !string.IsNullOrWhiteSpace(input.TeacherUserId) &&
                !string.Equals(input.TeacherUserId.Trim(), actor.UserId, StringComparison.Ordinal))
            {
                return Failure("Teachers can only change their own availability.");
            }

            var teacherUserId = actor.IsAdmin && !string.IsNullOrWhiteSpace(input.TeacherUserId)
                ? input.TeacherUserId.Trim()
                : actor.UserId;
            if (!await IsValidTeacherAsync(actor, teacherUserId, cancellationToken))
                return Failure("The selected teacher could not be found.");

            var timeZone = FindTimeZone(input.TimeZoneId);
            if (timeZone == null)
                return Failure("The selected time zone is not available.");
            var canonicalTimeZoneId = CanonicalTimeZoneId(input.TimeZoneId);

            var selectedDays = input.Days
                .Where(day => day.IsAvailable)
                .GroupBy(day => day.DayOfWeek)
                .Select(group => group.First())
                .ToList();
            if (selectedDays.Count == 0)
                return Failure("Select at least one available day.");
            if (selectedDays.Any(day => day.LocalEndTime <= day.LocalStartTime))
                return Failure("Each available day must end after it starts.");

            var existing = await _dbContext.TeacherAvailabilityWindows
                .Where(window => window.TeacherUserId == teacherUserId)
                .ToListAsync(cancellationToken);
            _dbContext.TeacherAvailabilityWindows.RemoveRange(existing);
            var now = DateTime.UtcNow;
            foreach (var day in selectedDays)
            {
                _dbContext.TeacherAvailabilityWindows.Add(new TeacherAvailabilityWindow
                {
                    TeacherUserId = teacherUserId,
                    DayOfWeek = day.DayOfWeek,
                    LocalStartTime = day.LocalStartTime,
                    LocalEndTime = day.LocalEndTime,
                    TimeZoneId = canonicalTimeZoneId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<IReadOnlyList<ScheduleStudentOption>> GetStudentOptionsAsync(
            ScheduleActor actor,
            CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin && !actor.IsTeacher)
                return Array.Empty<ScheduleStudentOption>();

            var studentRoleIds = await _dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Student")
                .Select(role => role.Id)
                .ToListAsync(cancellationToken);
            var studentUserIds = _dbContext.UserRoles
                .AsNoTracking()
                .Where(userRole => studentRoleIds.Contains(userRole.RoleId))
                .Select(userRole => userRole.UserId);
            return await _dbContext.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted && user.Email != null && studentUserIds.Contains(user.Id))
                .OrderBy(user => user.Email)
                .Select(user => new ScheduleStudentOption(user.Id, user.Email!))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ScheduleTeacherOption>> GetAvailableTeachersAsync(
            ScheduleActor actor,
            DateTime startLocal,
            string timeZoneId,
            int durationMinutes,
            CancellationToken cancellationToken = default)
        {
            timeZoneId = AmsterdamTimeZoneId;
            if (!actor.IsAdmin || durationMinutes is < 5 or > 1440)
                return Array.Empty<ScheduleTeacherOption>();
            var sourceZone = FindTimeZone(timeZoneId);
            if (sourceZone == null)
                return Array.Empty<ScheduleTeacherOption>();

            var local = DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
            if (sourceZone.IsInvalidTime(local))
                return Array.Empty<ScheduleTeacherOption>();
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(local, sourceZone);
            var endUtc = startUtc.AddMinutes(durationMinutes);

            var windows = await _dbContext.TeacherAvailabilityWindows
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var candidateIds = windows
                .Where(window =>
                {
                    var teacherZone = FindTimeZone(AmsterdamTimeZoneId);
                    if (teacherZone == null)
                        return false;
                    var teacherLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, teacherZone);
                    return window.DayOfWeek == teacherLocal.DayOfWeek &&
                           FitsAvailability(
                               TimeOnly.FromDateTime(teacherLocal),
                               durationMinutes,
                               window.LocalStartTime,
                               window.LocalEndTime);
                })
                .Select(window => window.TeacherUserId)
                .Distinct()
                .ToList();
            if (candidateIds.Count == 0)
                return Array.Empty<ScheduleTeacherOption>();

            var conflictingIds = await _dbContext.ScheduledEvents
                .AsNoTracking()
                .Where(scheduleEvent =>
                    candidateIds.Contains(scheduleEvent.TeacherUserId) &&
                    scheduleEvent.Status == ScheduleEventStatus.Scheduled &&
                    scheduleEvent.StartAtUtc < endUtc &&
                    scheduleEvent.EndAtUtc > startUtc)
                .Select(scheduleEvent => scheduleEvent.TeacherUserId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var availableIds = new List<string>();
            foreach (var candidateId in candidateIds.Except(conflictingIds))
            {
                var externalAvailability = await _schedulingIntegrationService.CheckAvailabilityAsync(
                    candidateId,
                    startUtc,
                    endUtc,
                    cancellationToken: cancellationToken);
                if (externalAvailability.Success && externalAvailability.IsAvailable)
                    availableIds.Add(candidateId);
            }

            var rows = await _dbContext.Users
                .AsNoTracking()
                .Where(user => availableIds.Contains(user.Id) && !user.IsDeleted)
                .OrderBy(user => user.FirstName)
                .ThenBy(user => user.LastName)
                .Select(user => new { user.Id, user.FirstName, user.LastName, user.UserName, user.Email })
                .ToListAsync(cancellationToken);
            return rows.Select(user => new ScheduleTeacherOption(
                user.Id,
                FormatName(user.FirstName, user.LastName, user.UserName, user.Email),
                user.Email ?? user.UserName ?? string.Empty)).ToList();
        }

        public async Task<OperationResult> SaveRosterRuleAsync(
            ScheduleActor actor,
            ScheduleRosterRuleInput input,
            CancellationToken cancellationToken = default)
        {
            input.TimeZoneId = AmsterdamTimeZoneId;
            if (!actor.IsAdmin && !actor.IsTeacher)
                return Failure("You do not have permission to manage a roster.");
            if (!actor.IsAdmin &&
                !string.IsNullOrWhiteSpace(input.TeacherUserId) &&
                !string.Equals(input.TeacherUserId.Trim(), actor.UserId, StringComparison.Ordinal))
            {
                return Failure("Teachers can only schedule meetings for themselves.");
            }

            var validation = ValidateCommon(
                input.TimeZoneId,
                input.DurationMinutes,
                input.Capacity,
                input.DeliveryType,
                input.MeetingUrl,
                input.Location,
                input.BookingOpenDaysBefore,
                input.BookingCloseHoursBefore);
            if (!validation.Success)
                return validation;

            if (input.CourseClassId <= 0)
                return Failure("Select a course class.");
            if (input.GenerateWeeksAhead is < 1 or > 52)
                return Failure("Generate between 1 and 52 weeks of meetings.");
            if (!input.ActiveUntilDate.HasValue)
                return Failure("Choose when this recurring class ends.");
            if (input.ActiveUntilDate < input.ActiveFromDate)
                return Failure("The roster end date must be after its start date.");

            var teacherUserId = actor.IsAdmin && !string.IsNullOrWhiteSpace(input.TeacherUserId)
                ? input.TeacherUserId.Trim()
                : actor.UserId;
            if (!await IsValidTeacherAsync(actor, teacherUserId, cancellationToken))
            {
                return Failure("The selected teacher could not be found.");
            }

            if (!await CanManageClassAsync(actor, input.CourseClassId, cancellationToken))
                return Failure("You cannot schedule this course class.");

            var classDefaults = await _dbContext.CourseClasses
                .AsNoTracking()
                .Where(courseClass => courseClass.Id == input.CourseClassId)
                .Select(courseClass => new { courseClass.DurationMinutes, courseClass.Capacity })
                .FirstOrDefaultAsync(cancellationToken);
            if (classDefaults == null)
                return Failure("The selected course class could not be found.");
            input.DurationMinutes = Math.Clamp(classDefaults.DurationMinutes, 5, 1440);
            input.Capacity = Math.Clamp(classDefaults.Capacity, 1, 10000);

            var connectionValidation = await _schedulingIntegrationService.ValidateTeacherConnectionsAsync(
                teacherUserId,
                input.DeliveryType,
                input.DeliveryType == ScheduleDeliveryType.Zoom,
                cancellationToken);
            if (!connectionValidation.Success)
                return connectionValidation;

            var canonicalTimeZoneId = CanonicalTimeZoneId(input.TimeZoneId);
            ScheduleRosterRule? rule = null;
            if (input.Id > 0)
            {
                rule = await _dbContext.ScheduleRosterRules
                    .FirstOrDefaultAsync(item => item.Id == input.Id, cancellationToken);
                if (rule == null)
                    return Failure("The recurring class no longer exists.");
                if (!CanManageTeacher(actor, rule.TeacherUserId))
                    return Failure("You cannot change this recurring class.");
            }

            var overlappingRules = await _dbContext.ScheduleRosterRules
                .AsNoTracking()
                .Where(item =>
                    item.Id != input.Id &&
                    item.TeacherUserId == teacherUserId &&
                    item.DayOfWeek == input.DayOfWeek &&
                    item.IsActive)
                .ToListAsync(cancellationToken);
            if (overlappingRules.Any(item =>
                    DateRangesOverlap(input.ActiveFromDate, input.ActiveUntilDate, item.ActiveFromDate, item.ActiveUntilDate) &&
                    TimesOverlap(input.LocalStartTime, input.DurationMinutes, item.LocalStartTime, item.DurationMinutes)))
            {
                return Failure("This roster overlaps another recurring meeting for the teacher.");
            }

            var now = DateTime.UtcNow;
            if (rule == null)
            {
                rule = new ScheduleRosterRule { CreatedAtUtc = now };
                _dbContext.ScheduleRosterRules.Add(rule);
            }
            else
            {
                var generatedToRemove = await _dbContext.ScheduledEvents
                    .Where(scheduleEvent =>
                        scheduleEvent.ScheduleRosterRuleId == rule.Id &&
                        scheduleEvent.StartAtUtc > now &&
                        !scheduleEvent.IsDetachedOverride &&
                        !scheduleEvent.Bookings.Any(booking => ActiveBookingStatuses.Contains(booking.Status)))
                    .ToListAsync(cancellationToken);
                foreach (var generatedEvent in generatedToRemove)
                {
                    var externalCancellation = await _schedulingIntegrationService.CancelEventAsync(generatedEvent, cancellationToken);
                    if (!externalCancellation.Success)
                        return externalCancellation;
                }
                _dbContext.ScheduledEvents.RemoveRange(generatedToRemove);
            }

            rule.TeacherUserId = teacherUserId;
            rule.CourseClassId = input.CourseClassId;
            rule.DayOfWeek = input.DayOfWeek;
            rule.LocalStartTime = input.LocalStartTime;
            rule.TimeZoneId = canonicalTimeZoneId;
            rule.ActiveFromDate = input.ActiveFromDate;
            rule.ActiveUntilDate = input.ActiveUntilDate;
            rule.DurationMinutes = input.DurationMinutes;
            rule.Capacity = input.Capacity;
            rule.DeliveryType = input.DeliveryType;
            rule.MeetingUrl = input.DeliveryType == ScheduleDeliveryType.Zoom
                ? null
                : NormalizeUrl(input.MeetingUrl);
            rule.Location = NormalizeText(input.Location, 300);
            rule.BookingOpenDaysBefore = input.BookingOpenDaysBefore;
            rule.BookingCloseHoursBefore = input.BookingCloseHoursBefore;
            rule.GenerateWeeksAhead = input.GenerateWeeksAhead;
            rule.IsVisibleForStudentBooking = input.IsVisibleForStudentBooking;
            rule.IsActive = input.IsActive;
            rule.UpdatedAtUtc = now;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await GenerateUpcomingEventsAsync(rule.Id, cancellationToken);
            return Success();
        }

        public async Task<OperationResult> DeleteRosterRuleAsync(
            ScheduleActor actor,
            int ruleId,
            CancellationToken cancellationToken = default)
        {
            var rule = await _dbContext.ScheduleRosterRules
                .Include(item => item.ScheduledEvents)
                    .ThenInclude(scheduleEvent => scheduleEvent.Bookings)
                .FirstOrDefaultAsync(item => item.Id == ruleId, cancellationToken);
            if (rule == null)
                return Failure("The recurring class no longer exists.");
            if (!CanManageTeacher(actor, rule.TeacherUserId))
                return Failure("You cannot remove this recurring class.");

            var now = DateTime.UtcNow;
            var removable = rule.ScheduledEvents
                .Where(scheduleEvent =>
                    scheduleEvent.StartAtUtc > now &&
                    !scheduleEvent.Bookings.Any(booking => ActiveBookingStatuses.Contains(booking.Status)))
                .ToList();
            foreach (var scheduleEvent in removable)
            {
                var externalCancellation = await _schedulingIntegrationService.CancelEventAsync(scheduleEvent, cancellationToken);
                if (!externalCancellation.Success)
                    return externalCancellation;
            }
            _dbContext.ScheduledEvents.RemoveRange(removable);
            foreach (var retained in rule.ScheduledEvents.Except(removable))
                retained.ScheduleRosterRuleId = null;

            _dbContext.ScheduleRosterRules.Remove(rule);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> CreateManualEventAsync(
            ScheduleActor actor,
            ManualScheduleEventInput input,
            CancellationToken cancellationToken = default)
        {
            input.TimeZoneId = AmsterdamTimeZoneId;
            if (!actor.IsAdmin && !actor.IsTeacher)
                return Failure("You do not have permission to create meetings.");
            if (!actor.IsAdmin &&
                !string.IsNullOrWhiteSpace(input.TeacherUserId) &&
                !string.Equals(input.TeacherUserId.Trim(), actor.UserId, StringComparison.Ordinal))
            {
                return Failure("Teachers can only schedule meetings for themselves.");
            }

            var validation = ValidateCommon(
                input.TimeZoneId,
                input.DurationMinutes,
                input.Capacity,
                input.DeliveryType,
                input.MeetingUrl,
                input.Location,
                input.BookingOpenDaysBefore,
                input.BookingCloseHoursBefore);
            if (!validation.Success)
                return validation;

            var hasCourseClass = input.CourseClassId.HasValue && input.CourseClassId.Value > 0;
            var hasStudent = !string.IsNullOrWhiteSpace(input.StudentUserId);
            if (hasCourseClass == hasStudent)
                return Failure("Select either a course class or a student, but not both.");
            if (hasCourseClass && !await CanManageClassAsync(actor, input.CourseClassId!.Value, cancellationToken))
                return Failure("You cannot schedule this course class.");
            if (hasStudent && !await IsValidStudentAsync(input.StudentUserId!.Trim(), cancellationToken))
                return Failure("The selected student could not be found.");

            var teacherUserId = actor.IsAdmin && !string.IsNullOrWhiteSpace(input.TeacherUserId)
                ? input.TeacherUserId.Trim()
                : actor.UserId;
            if (!await IsValidTeacherAsync(actor, teacherUserId, cancellationToken))
                return Failure("The selected teacher could not be found.");

            var connectionValidation = await _schedulingIntegrationService.ValidateTeacherConnectionsAsync(
                teacherUserId,
                input.DeliveryType,
                input.DeliveryType == ScheduleDeliveryType.Zoom && string.IsNullOrWhiteSpace(input.MeetingUrl),
                cancellationToken);
            if (!connectionValidation.Success)
                return connectionValidation;
            var timeZone = FindTimeZone(input.TimeZoneId);
            if (timeZone == null)
                return Failure("The selected time zone is not available.");

            var local = DateTime.SpecifyKind(input.StartLocal, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(local))
                return Failure("That local time does not exist because of a daylight-saving change.");

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
            var endUtc = startUtc.AddMinutes(input.DurationMinutes);
            if (startUtc <= DateTime.UtcNow)
                return Failure("Choose a meeting time in the future.");
            if (await HasTeacherConflictAsync(teacherUserId, startUtc, endUtc, null, cancellationToken))
                return Failure("This meeting overlaps another event for the teacher.");

            var scheduleEvent = hasCourseClass
                ? await BuildEventAsync(
                    input.CourseClassId!.Value,
                    teacherUserId,
                    startUtc,
                    endUtc,
                    CanonicalTimeZoneId(input.TimeZoneId),
                    input.Capacity,
                    input.DeliveryType,
                    NormalizeUrl(input.MeetingUrl),
                    NormalizeText(input.Location, 300),
                    input.BookingOpenDaysBefore,
                    input.BookingCloseHoursBefore,
                    ScheduleEventSource.Manual,
                    null,
                    cancellationToken)
                : await BuildStudentEventAsync(
                    input.StudentUserId!.Trim(),
                    teacherUserId,
                    startUtc,
                    endUtc,
                    CanonicalTimeZoneId(input.TimeZoneId),
                    input.DeliveryType,
                    NormalizeUrl(input.MeetingUrl),
                    NormalizeText(input.Location, 300),
                    cancellationToken);
            if (scheduleEvent == null)
                return Failure("The course class, student, or teacher could not be found.");

            var provisioned = await _schedulingIntegrationService.ProvisionEventAsync(scheduleEvent, cancellationToken);
            if (!provisioned.Success)
                return provisioned;

            _dbContext.ScheduledEvents.Add(scheduleEvent);
            EventBooking? directBooking = null;
            if (hasStudent)
            {
                directBooking = new EventBooking
                {
                    ScheduledEvent = scheduleEvent,
                    UserId = input.StudentUserId!.Trim(),
                    Status = EventBookingStatus.Confirmed,
                    BookedAtUtc = DateTime.UtcNow,
                    ZoomRegistrationStatus = input.DeliveryType == ScheduleDeliveryType.Zoom
                        ? ZoomRegistrationStatus.Pending
                        : ZoomRegistrationStatus.NotRequired
                };
                _dbContext.EventBookings.Add(directBooking);
            }
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await _schedulingIntegrationService.CancelEventAsync(scheduleEvent, cancellationToken);
                throw;
            }

            if (directBooking != null)
            {
                var registration = await _schedulingIntegrationService.RegisterBookingAsync(
                    scheduleEvent.Id,
                    directBooking.UserId,
                    cancellationToken);
                if (!registration.Success)
                {
                    _logger.LogWarning(
                        "Direct student booking for event {EventId} is waiting for Zoom registration: {Error}",
                        scheduleEvent.Id,
                        registration.ErrorMessage);
                }
                var calendarSync = await _schedulingIntegrationService.SyncEventAttendeesAsync(
                    scheduleEvent.Id,
                    cancellationToken);
                if (!calendarSync.Success)
                {
                    _logger.LogWarning(
                        "Direct booking for event {EventId} was created, but its Google Calendar attendees could not be updated: {Error}",
                        scheduleEvent.Id,
                        calendarSync.ErrorMessage);
                }
            }
            return Success();
        }

        public async Task<IReadOnlyList<ScheduleCalendarItem>> GetCalendarItemsAsync(
            ScheduleActor actor,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken cancellationToken = default)
            => await GetCalendarItemsCoreAsync(
                actor,
                rangeStartUtc,
                rangeEndUtc,
                teacherUserId: null,
                includeAllTeachers: actor.IsAdmin,
                staffCalendarView: false,
                cancellationToken);

        public async Task<IReadOnlyList<ScheduleCalendarItem>> GetStaffCalendarItemsAsync(
            ScheduleActor actor,
            string? teacherUserId,
            bool includeAllTeachers,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin && !actor.IsTeacher)
                return Array.Empty<ScheduleCalendarItem>();

            var normalizedTeacherId = string.IsNullOrWhiteSpace(teacherUserId)
                ? null
                : teacherUserId.Trim();
            if (normalizedTeacherId != null &&
                !await IsValidTeacherAsync(new ScheduleActor(actor.UserId, true, actor.IsTeacher), normalizedTeacherId, cancellationToken))
            {
                return Array.Empty<ScheduleCalendarItem>();
            }

            return await GetCalendarItemsCoreAsync(
                actor,
                rangeStartUtc,
                rangeEndUtc,
                normalizedTeacherId,
                includeAllTeachers,
                staffCalendarView: true,
                cancellationToken);
        }

        private async Task<IReadOnlyList<ScheduleCalendarItem>> GetCalendarItemsCoreAsync(
            ScheduleActor actor,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            string? teacherUserId,
            bool includeAllTeachers,
            bool staffCalendarView,
            CancellationToken cancellationToken)
        {
            var startUtc = EnsureUtc(rangeStartUtc);
            var endUtc = EnsureUtc(rangeEndUtc);
            if (endUtc <= startUtc)
                endUtc = startUtc.AddMonths(1);
            if (endUtc > startUtc.AddYears(1))
                endUtc = startUtc.AddYears(1);

            var query = _dbContext.ScheduledEvents
                .AsNoTracking()
                .AsSplitQuery()
                .Include(scheduleEvent => scheduleEvent.CourseClass)
                    .ThenInclude(courseClass => courseClass!.CourseSection)
                        .ThenInclude(section => section.Course)
                .Include(scheduleEvent => scheduleEvent.Bookings)
                .Where(scheduleEvent =>
                    scheduleEvent.StartAtUtc < endUtc &&
                    scheduleEvent.EndAtUtc > startUtc);

            if (staffCalendarView)
            {
                if (!includeAllTeachers)
                    query = query.Where(scheduleEvent => scheduleEvent.TeacherUserId == (teacherUserId ?? actor.UserId));
            }
            else if (actor.IsTeacher)
                query = query.Where(scheduleEvent =>
                    scheduleEvent.TeacherUserId == actor.UserId ||
                    scheduleEvent.Bookings.Any(booking =>
                        booking.UserId == actor.UserId &&
                        ActiveBookingStatuses.Contains(booking.Status)));

            var events = await query
                .OrderBy(scheduleEvent => scheduleEvent.StartAtUtc)
                .ToListAsync(cancellationToken);

            var eventCourseIds = events
                .Where(scheduleEvent => scheduleEvent.CourseId.HasValue)
                .Select(scheduleEvent => scheduleEvent.CourseId!.Value)
                .Distinct()
                .ToList();
            var courseSections = eventCourseIds.Count == 0
                ? new List<CourseSection>()
                : await _dbContext.CourseSections
                    .AsNoTracking()
                    .Where(section => eventCourseIds.Contains(section.CourseId))
                    .OrderBy(section => section.OrderNumber)
                    .ThenBy(section => section.Id)
                    .ToListAsync(cancellationToken);
            var sectionsByCourseId = courseSections
                .GroupBy(section => section.CourseId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CourseSection>)group.ToList());

            var courseAccess = await _dbContext.UserCourses
                .AsNoTracking()
                .Where(access => access.UserId == actor.UserId)
                .ToDictionaryAsync(access => access.CourseId, cancellationToken);
            var creditLots = await _dbContext.UserCreditLots
                .AsNoTracking()
                .Where(lot =>
                    lot.UserId == actor.UserId &&
                    lot.RemainingQuantity > 0 &&
                    (!lot.ExpiresAtUtc.HasValue || lot.ExpiresAtUtc > DateTime.UtcNow))
                .ToListAsync(cancellationToken);
            var enrollmentBookingCounts = staffCalendarView
                ? new Dictionary<int, int>()
                : await _dbContext.EventBookings
                    .AsNoTracking()
                    .Where(booking =>
                        booking.UserId == actor.UserId &&
                        (booking.Status == EventBookingStatus.Confirmed ||
                         booking.Status == EventBookingStatus.Attended ||
                         ((booking.Status == EventBookingStatus.NoShow || booking.Status == EventBookingStatus.Cancelled) &&
                          booking.CreditResolution != CreditResolutionAction.Return)) &&
                        booking.ScheduledEvent.CourseClassId.HasValue)
                    .GroupBy(booking => booking.ScheduledEvent.CourseClassId!.Value)
                    .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);

            var result = new List<ScheduleCalendarItem>();
            foreach (var scheduleEvent in events)
            {
                var item = BuildCalendarItem(
                    scheduleEvent,
                    actor,
                    courseAccess,
                    creditLots,
                    enrollmentBookingCounts,
                    sectionsByCourseId,
                    staffCalendarView);
                if (item != null)
                    result.Add(item);
            }

            return result;
        }

        public async Task<ScheduleOverviewData> GetOverviewAsync(
            ScheduleActor actor,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var items = await GetCalendarItemsAsync(actor, now, now.AddDays(90), cancellationToken);
            var live = items.Where(item => item.Status == ScheduleEventStatus.Scheduled).ToList();
            var upcoming = live
                .Where(item => item.IsBooked || item.CanManage)
                .Take(6)
                .ToList();
            var available = actor.IsTeacher
                ? new List<ScheduleCalendarItem>()
                : live
                    .Where(item => !item.IsBooked && item.CanBook)
                    .ToList();
            return new ScheduleOverviewData(
                live.Count(item => item.IsBooked || item.CanManage),
                live.Count(item => item.IsBooked),
                0,
                upcoming,
                available);
        }

        public async Task<ScheduleOverviewData> GetStaffOverviewAsync(
            ScheduleActor actor,
            string? teacherUserId,
            bool includeAllTeachers,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var items = await GetStaffCalendarItemsAsync(
                actor,
                teacherUserId,
                includeAllTeachers,
                now,
                now.AddDays(90),
                cancellationToken);
            var live = items.Where(item => item.Status == ScheduleEventStatus.Scheduled).ToList();
            return new ScheduleOverviewData(
                live.Count,
                live.Count(item => item.IsBooked),
                0,
                live.Take(6).ToList(),
                Array.Empty<ScheduleCalendarItem>());
        }

        public async Task<IReadOnlyList<ScheduleCalendarItem>> GetStudentCalendarItemsAsync(
            ScheduleActor actor,
            string studentUserId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken cancellationToken = default)
        {
            if ((!actor.IsAdmin && !actor.IsTeacher) ||
                string.IsNullOrWhiteSpace(studentUserId) ||
                !await IsValidStudentAsync(studentUserId, cancellationToken))
            {
                return Array.Empty<ScheduleCalendarItem>();
            }

            var studentActor = new ScheduleActor(studentUserId, false, false);
            var items = await GetCalendarItemsAsync(studentActor, rangeStartUtc, rangeEndUtc, cancellationToken);
            return items
                .Where(item => item.IsBooked)
                .Select(item => item with
                {
                    CanBook = false,
                    CanCancelBooking = false,
                    CanJoin = false,
                    JoinUrl = null,
                    CanManage = false,
                    Category = item.Status == ScheduleEventStatus.Cancelled ? "cancelled" : "booked"
                })
                .ToList();
        }

        public async Task<ScheduleOverviewData> GetStudentOverviewAsync(
            ScheduleActor actor,
            string studentUserId,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var items = await GetStudentCalendarItemsAsync(
                actor,
                studentUserId,
                now,
                now.AddDays(90),
                cancellationToken);
            var live = items.Where(item => item.Status == ScheduleEventStatus.Scheduled).ToList();
            return new ScheduleOverviewData(
                live.Count,
                live.Count,
                0,
                live.Take(6).ToList(),
                Array.Empty<ScheduleCalendarItem>());
        }

        public async Task<IReadOnlyList<ScheduleCalendarItem>> GetCourseClassItemsAsync(
            ScheduleActor actor,
            int courseClassId,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var items = await GetCalendarItemsAsync(actor, now, now.AddMonths(6), cancellationToken);
            var classItems = items
                .Where(item => item.CourseClassId == courseClassId && item.Status == ScheduleEventStatus.Scheduled)
                .ToList();
            var bookedItems = classItems.Where(item => item.IsBooked).ToList();
            return bookedItems.Count > 0 ? bookedItems : classItems.Take(12).ToList();
        }

        public async Task<OperationResult> BookAsync(
            ScheduleActor actor,
            int scheduledEventId,
            CancellationToken cancellationToken = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            var result = await strategy.ExecuteAsync(
                () => BookWithinTransactionAsync(actor, scheduledEventId, cancellationToken));
            if (result.Success)
            {
                var registration = await _schedulingIntegrationService.RegisterBookingAsync(
                    scheduledEventId,
                    actor.UserId,
                    cancellationToken);
                if (!registration.Success)
                {
                    _logger.LogWarning(
                        "Booking for event {EventId} succeeded but Zoom registration is pending: {Error}",
                        scheduledEventId,
                        registration.ErrorMessage);
                }
                var calendarSync = await _schedulingIntegrationService.SyncEventAttendeesAsync(
                    scheduledEventId,
                    cancellationToken);
                if (!calendarSync.Success)
                {
                    _logger.LogWarning(
                        "Booking for event {EventId} succeeded but its Google Calendar attendees could not be updated: {Error}",
                        scheduledEventId,
                        calendarSync.ErrorMessage);
                }
            }
            return result;
        }

        private async Task<OperationResult> BookWithinTransactionAsync(
            ScheduleActor actor,
            int scheduledEventId,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            try
            {
                var scheduleEvent = await _dbContext.ScheduledEvents
                    .Include(item => item.CourseClass)
                        .ThenInclude(courseClass => courseClass!.CourseSection)
                            .ThenInclude(section => section.Course)
                    .Include(item => item.CreditConsumptionPolicy)
                    .Include(item => item.Bookings)
                        .ThenInclude(booking => booking.CreditAllocations)
                    .FirstOrDefaultAsync(item => item.Id == scheduledEventId, cancellationToken);
                if (scheduleEvent == null)
                    return Failure("This meeting no longer exists.");
                if (scheduleEvent.Status != ScheduleEventStatus.Scheduled)
                    return Failure("This meeting is not available for booking.");
                if (!scheduleEvent.IsVisibleForStudentBooking)
                    return Failure("This meeting is not available for student self-booking.");

                var now = DateTime.UtcNow;
                if (scheduleEvent.StartAtUtc <= now)
                    return Failure("This meeting has already started.");
                if (now < scheduleEvent.BookingOpensAtUtc)
                    return Failure($"Booking opens {scheduleEvent.BookingOpensAtUtc:dd MMM yyyy, HH:mm} UTC.");
                if (now > scheduleEvent.BookingClosesAtUtc)
                    return Failure("Booking for this meeting has closed.");

                var booking = scheduleEvent.Bookings.FirstOrDefault(item => item.UserId == actor.UserId);
                if (booking != null && ActiveBookingStatuses.Contains(booking.Status))
                    return Failure("You already booked this meeting.");

                var activeCount = scheduleEvent.Bookings.Count(item => ActiveBookingStatuses.Contains(item.Status));
                if (activeCount >= scheduleEvent.Capacity)
                    return Failure("This meeting is full.");

                var hasConflict = await _dbContext.EventBookings
                    .AsNoTracking()
                    .AnyAsync(booking =>
                        booking.UserId == actor.UserId &&
                        ActiveBookingStatuses.Contains(booking.Status) &&
                        booking.ScheduledEventId != scheduledEventId &&
                        booking.ScheduledEvent.Status == ScheduleEventStatus.Scheduled &&
                        booking.ScheduledEvent.StartAtUtc < scheduleEvent.EndAtUtc &&
                        booking.ScheduledEvent.EndAtUtc > scheduleEvent.StartAtUtc,
                        cancellationToken);
                if (hasConflict)
                    return Failure("You already have another booking at this time.");

                var requiresEnrollment = scheduleEvent.BookingAccess is
                    CourseClassBookingAccess.CourseEnrollment or
                    CourseClassBookingAccess.CourseEnrollmentAndCredit;
                var courseAccess = scheduleEvent.CourseId.HasValue
                    ? await _dbContext.UserCourses.AsNoTracking().FirstOrDefaultAsync(
                        access => access.UserId == actor.UserId && access.CourseId == scheduleEvent.CourseId,
                        cancellationToken)
                    : null;
                if (requiresEnrollment && courseAccess == null)
                {
                    return Failure("This meeting is only available to enrolled course participants.");
                }
                if (requiresEnrollment && scheduleEvent.CourseClass != null && courseAccess != null)
                {
                    var courseSections = await _dbContext.CourseSections
                        .AsNoTracking()
                        .Where(section => section.CourseId == scheduleEvent.CourseClass.CourseSection.CourseId)
                        .OrderBy(section => section.OrderNumber)
                        .ThenBy(section => section.Id)
                        .ToListAsync(cancellationToken);
                    var bookingEligibleAtUtc = CalculateBookingEligibleAtUtc(
                        courseAccess,
                        scheduleEvent.CourseClass,
                        courseSections);
                    if (bookingEligibleAtUtc > now)
                        return Failure($"Booking becomes available {bookingEligibleAtUtc:dd MMM yyyy, HH:mm} UTC.");
                }

                if (scheduleEvent.BookingAccess == CourseClassBookingAccess.CourseEnrollment &&
                    scheduleEvent.CourseClass?.EnrollmentBookingLimit is int enrollmentLimit)
                {
                    var usedIncludedMeetings = await _dbContext.EventBookings
                        .AsNoTracking()
                        .CountAsync(existing =>
                            existing.UserId == actor.UserId &&
                            existing.ScheduledEvent.CourseClassId == scheduleEvent.CourseClassId &&
                            (existing.Status == EventBookingStatus.Confirmed ||
                             existing.Status == EventBookingStatus.Attended ||
                             ((existing.Status == EventBookingStatus.NoShow || existing.Status == EventBookingStatus.Cancelled) &&
                              existing.CreditResolution != CreditResolutionAction.Return)),
                            cancellationToken);
                    if (usedIncludedMeetings >= enrollmentLimit)
                        return Failure($"You have already used the {enrollmentLimit} included meeting{(enrollmentLimit == 1 ? string.Empty : "s")} for this class.");
                }

                if (booking == null)
                {
                    booking = new EventBooking
                    {
                        UserId = actor.UserId,
                        ScheduledEvent = scheduleEvent
                    };
                    scheduleEvent.Bookings.Add(booking);
                }

                booking.Status = EventBookingStatus.Confirmed;
                booking.BookedAtUtc = now;
                booking.CancelledAtUtc = null;
                booking.AttendanceReconciledAtUtc = null;
                booking.AttendanceResolutionSource = null;
                booking.AttendanceManuallyOverridden = false;
                booking.CreditResolvedAtUtc = null;
                booking.CreditResolution = null;

                var requiresCredit = scheduleEvent.BookingAccess is
                    CourseClassBookingAccess.Credit or
                    CourseClassBookingAccess.CourseEnrollmentAndCredit;
                if (requiresCredit)
                {
                    if (!scheduleEvent.RequiredCreditTypeId.HasValue)
                        return Failure("This class is missing its required credit configuration.");

                    var lots = await EligibleCreditLotsQuery(actor.UserId, scheduleEvent, now)
                        .OrderBy(lot => lot.ExpiresAtUtc == null)
                        .ThenBy(lot => lot.ExpiresAtUtc)
                        .ThenBy(lot => lot.GrantedAtUtc)
                        .ToListAsync(cancellationToken);
                    var creditCost = Math.Max(1, scheduleEvent.CreditCost);
                    if (lots.Sum(lot => lot.RemainingQuantity) < creditCost)
                        return Failure("You do not have enough eligible credits for this meeting.");

                    var remaining = creditCost;
                    foreach (var lot in lots.Where(lot => lot.RemainingQuantity > 0))
                    {
                        var amount = Math.Min(remaining, lot.RemainingQuantity);
                        lot.RemainingQuantity -= amount;
                        booking.CreditAllocations.Add(new EventBookingCreditAllocation
                        {
                            UserCreditLot = lot,
                            Amount = amount
                        });
                        _dbContext.UserCreditTransactions.Add(new UserCreditTransaction
                        {
                            UserId = actor.UserId,
                            CreditTypeId = scheduleEvent.RequiredCreditTypeId.Value,
                            UserCreditLot = lot,
                            EventBooking = booking,
                            TransactionType = CreditTransactionType.Reserve,
                            QuantityDelta = -amount,
                            Description = $"Reserved for {scheduleEvent.Title}",
                            CreatedAtUtc = now
                        });
                        remaining -= amount;
                        if (remaining == 0)
                            break;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Success();
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogWarning(exception, "Booking event {ScheduledEventId} failed because of a concurrent update.", scheduledEventId);
                return Failure("This meeting changed while you were booking it. Refresh and try again.");
            }
        }

        public async Task<OperationResult> CancelBookingAsync(
            ScheduleActor actor,
            int scheduledEventId,
            CancellationToken cancellationToken = default)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            var result = await strategy.ExecuteAsync(
                () => CancelBookingWithinTransactionAsync(actor, scheduledEventId, cancellationToken));
            if (result.Success)
            {
                await _schedulingIntegrationService.CancelBookingRegistrationAsync(
                    scheduledEventId,
                    actor.UserId,
                    cancellationToken);
                var calendarSync = await _schedulingIntegrationService.SyncEventAttendeesAsync(
                    scheduledEventId,
                    cancellationToken);
                if (!calendarSync.Success)
                {
                    _logger.LogWarning(
                        "Cancelled booking for event {EventId}, but its Google Calendar attendees could not be updated: {Error}",
                        scheduledEventId,
                        calendarSync.ErrorMessage);
                }
            }
            return result;
        }

        private async Task<OperationResult> CancelBookingWithinTransactionAsync(
            ScheduleActor actor,
            int scheduledEventId,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var booking = await _dbContext.EventBookings
                .Include(item => item.ScheduledEvent)
                    .ThenInclude(scheduleEvent => scheduleEvent.CreditConsumptionPolicy)
                .Include(item => item.CreditAllocations)
                    .ThenInclude(allocation => allocation.UserCreditLot)
                .FirstOrDefaultAsync(item =>
                    item.ScheduledEventId == scheduledEventId &&
                    item.UserId == actor.UserId,
                    cancellationToken);
            if (booking == null || !ActiveBookingStatuses.Contains(booking.Status))
                return Failure("You do not have an active booking for this meeting.");
            if (booking.ScheduledEvent.StartAtUtc <= DateTime.UtcNow)
                return Failure("A booking cannot be cancelled after the meeting starts.");

            var now = DateTime.UtcNow;
            var policy = booking.ScheduledEvent.CreditConsumptionPolicy;
            var isEarly = policy == null || now <= booking.ScheduledEvent.StartAtUtc.AddHours(-policy.CancellationWindowHours);
            var action = policy == null
                ? CreditResolutionAction.Return
                : isEarly
                    ? policy.EarlyCancellationAction
                    : policy.LateCancellationAction;
            if (action == CreditResolutionAction.Return)
                ReturnBookingCredits(booking, now, "Returned after booking cancellation");
            else
                ConsumeBookingCredits(booking, now, "Consumed after late booking cancellation");

            booking.Status = EventBookingStatus.Cancelled;
            booking.CancelledAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Success();
        }

        public async Task<SchedulingJoinResult> GetJoinUrlAsync(
            ScheduleActor actor,
            int scheduledEventId,
            CancellationToken cancellationToken = default)
        {
            var scheduleEvent = await _dbContext.ScheduledEvents.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == scheduledEventId, cancellationToken);
            if (scheduleEvent == null || scheduleEvent.Status != ScheduleEventStatus.Scheduled)
                return new(false, null, "This meeting is no longer available.");
            var now = DateTime.UtcNow;
            if (now < scheduleEvent.StartAtUtc)
                return new(false, null, "The joining room opens when the class starts.");
            if (now > scheduleEvent.EndAtUtc.AddMinutes(JoinWindowMinutesAfterEnd))
                return new(false, null, "The joining window has closed.");

            if (IsAssignedTeacher(actor, scheduleEvent.TeacherUserId))
            {
                return string.IsNullOrWhiteSpace(scheduleEvent.MeetingUrl)
                    ? new(false, null, "The meeting joining details are unavailable.")
                    : new(true, scheduleEvent.MeetingUrl, null);
            }

            var hasActiveBooking = await _dbContext.EventBookings.AsNoTracking().AnyAsync(item =>
                item.ScheduledEventId == scheduledEventId &&
                item.UserId == actor.UserId &&
                ActiveBookingStatuses.Contains(item.Status),
                cancellationToken);
            if (!hasActiveBooking)
                return new(false, null, "You need an active reservation to join this meeting.");
            if (scheduleEvent.DeliveryType != ScheduleDeliveryType.Zoom)
            {
                return string.IsNullOrWhiteSpace(scheduleEvent.MeetingUrl)
                    ? new(false, null, "The meeting joining details are unavailable.")
                    : new(true, scheduleEvent.MeetingUrl, null);
            }
            return await _schedulingIntegrationService.GetBookingJoinUrlAsync(
                scheduledEventId,
                actor.UserId,
                cancellationToken);
        }

        public async Task<OperationResult> MoveEventAsync(
            ScheduleActor actor,
            int scheduledEventId,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            var scheduleEvent = await _dbContext.ScheduledEvents
                .Include(item => item.Bookings)
                .FirstOrDefaultAsync(item => item.Id == scheduledEventId, cancellationToken);
            if (scheduleEvent == null)
                return Failure("This meeting no longer exists.");
            if (!CanManageTeacher(actor, scheduleEvent.TeacherUserId))
                return Failure("You cannot move this meeting.");
            if (scheduleEvent.Status != ScheduleEventStatus.Scheduled)
                return Failure("Only scheduled meetings can be moved.");
            if (scheduleEvent.Bookings.Any(booking => ActiveBookingStatuses.Contains(booking.Status)))
                return Failure("Booked meetings cannot be moved. Cancel it and create a replacement instead.");

            var normalizedStart = EnsureUtc(startUtc);
            var normalizedEnd = EnsureUtc(endUtc);
            if (normalizedStart <= DateTime.UtcNow || normalizedEnd <= normalizedStart)
                return Failure("Choose a valid future meeting time.");
            if (normalizedEnd - normalizedStart > TimeSpan.FromHours(24))
                return Failure("A meeting cannot be longer than 24 hours.");
            if (await HasTeacherConflictAsync(
                    scheduleEvent.TeacherUserId,
                    normalizedStart,
                    normalizedEnd,
                    scheduledEventId,
                    cancellationToken))
            {
                return Failure("This meeting overlaps another event for the teacher.");
            }

            var externalUpdate = await _schedulingIntegrationService.UpdateEventAsync(
                scheduleEvent,
                normalizedStart,
                normalizedEnd,
                cancellationToken);
            if (!externalUpdate.Success)
                return externalUpdate;

            var shift = normalizedStart - scheduleEvent.StartAtUtc;
            scheduleEvent.StartAtUtc = normalizedStart;
            scheduleEvent.EndAtUtc = normalizedEnd;
            scheduleEvent.BookingOpensAtUtc = scheduleEvent.BookingOpensAtUtc.Add(shift);
            scheduleEvent.BookingClosesAtUtc = scheduleEvent.BookingClosesAtUtc.Add(shift);
            scheduleEvent.IsDetachedOverride = true;
            scheduleEvent.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task<OperationResult> CancelEventAsync(
            ScheduleActor actor,
            int scheduledEventId,
            string? reason,
            CancellationToken cancellationToken = default)
        {
            var scheduleEvent = await _dbContext.ScheduledEvents
                .Include(item => item.CreditConsumptionPolicy)
                .Include(item => item.Bookings)
                    .ThenInclude(booking => booking.CreditAllocations)
                        .ThenInclude(allocation => allocation.UserCreditLot)
                .FirstOrDefaultAsync(item => item.Id == scheduledEventId, cancellationToken);
            if (scheduleEvent == null)
                return Failure("This meeting no longer exists.");
            if (!CanManageTeacher(actor, scheduleEvent.TeacherUserId))
                return Failure("You cannot cancel this meeting.");
            if (scheduleEvent.Status == ScheduleEventStatus.Cancelled)
                return Success();

            var externalCancellation = await _schedulingIntegrationService.CancelEventAsync(scheduleEvent, cancellationToken);
            if (!externalCancellation.Success)
                return externalCancellation;

            var now = DateTime.UtcNow;
            var shouldReturn = scheduleEvent.CreditConsumptionPolicy?.StaffCancellationAction
                != CreditResolutionAction.Consume;
            foreach (var booking in scheduleEvent.Bookings.Where(item => ActiveBookingStatuses.Contains(item.Status)))
            {
                if (shouldReturn)
                    ReturnBookingCredits(booking, now, "Returned after staff cancelled the meeting");
                booking.Status = EventBookingStatus.Cancelled;
                booking.CancelledAtUtc = now;
            }

            scheduleEvent.Status = ScheduleEventStatus.Cancelled;
            scheduleEvent.CancellationReason = NormalizeText(reason, 1000) ?? "Cancelled by staff";
            scheduleEvent.UpdatedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Success();
        }

        public async Task GenerateUpcomingEventsAsync(
            int? ruleId = null,
            CancellationToken cancellationToken = default)
        {
            var rules = await _dbContext.ScheduleRosterRules
                .AsNoTracking()
                .AsSplitQuery()
                .Include(rule => rule.CourseClass)
                    .ThenInclude(courseClass => courseClass.CourseSection)
                        .ThenInclude(section => section.Course)
                .Include(rule => rule.TeacherUser)
                .Where(rule => rule.IsActive && (!ruleId.HasValue || rule.Id == ruleId.Value))
                .ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;

            foreach (var rule in rules)
            {
                var connectionValidation = await _schedulingIntegrationService.ValidateTeacherConnectionsAsync(
                    rule.TeacherUserId,
                    rule.DeliveryType,
                    rule.DeliveryType == ScheduleDeliveryType.Zoom,
                    cancellationToken);
                if (!connectionValidation.Success)
                {
                    _logger.LogWarning(
                        "Roster rule {RuleId} could not generate meetings: {Error}",
                        rule.Id,
                        connectionValidation.ErrorMessage);
                    continue;
                }

                var timeZone = FindTimeZone(AmsterdamTimeZoneId);
                if (timeZone == null)
                {
                    _logger.LogWarning("Roster rule {RuleId} has unknown time zone {TimeZoneId}.", rule.Id, rule.TimeZoneId);
                    continue;
                }

                var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timeZone));
                var fromDate = rule.ActiveFromDate > localToday ? rule.ActiveFromDate : localToday;
                var horizon = localToday.AddDays(rule.GenerateWeeksAhead * 7);
                var untilDate = rule.ActiveUntilDate.HasValue && rule.ActiveUntilDate.Value < horizon
                    ? rule.ActiveUntilDate.Value
                    : horizon;
                var offset = ((int)rule.DayOfWeek - (int)fromDate.DayOfWeek + 7) % 7;
                var occurrenceDate = fromDate.AddDays(offset);

                while (occurrenceDate <= untilDate)
                {
                    var localStart = DateTime.SpecifyKind(
                        occurrenceDate.ToDateTime(rule.LocalStartTime),
                        DateTimeKind.Unspecified);
                    if (!timeZone.IsInvalidTime(localStart))
                    {
                        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
                        var endUtc = startUtc.AddMinutes(rule.DurationMinutes);
                        var exists = await _dbContext.ScheduledEvents.AsNoTracking().AnyAsync(
                            scheduleEvent =>
                                scheduleEvent.ScheduleRosterRuleId == rule.Id &&
                                scheduleEvent.StartAtUtc == startUtc,
                            cancellationToken);
                        var conflict = !exists && await HasTeacherConflictAsync(
                            rule.TeacherUserId,
                            startUtc,
                            endUtc,
                            null,
                            cancellationToken);
                        if (!exists && !conflict && startUtc > now)
                        {
                            var scheduleEvent = BuildEvent(
                                rule.CourseClass,
                                rule.TeacherUser,
                                startUtc,
                                endUtc,
                                AmsterdamTimeZoneId,
                                rule.Capacity,
                                rule.DeliveryType,
                                rule.DeliveryType == ScheduleDeliveryType.Zoom ? null : rule.MeetingUrl,
                                rule.Location,
                                rule.BookingOpenDaysBefore,
                                rule.BookingCloseHoursBefore,
                                ScheduleEventSource.Roster,
                                rule.Id);
                            scheduleEvent.IsVisibleForStudentBooking = rule.IsVisibleForStudentBooking;
                            var provisioned = await _schedulingIntegrationService.ProvisionEventAsync(scheduleEvent, cancellationToken);
                            if (!provisioned.Success)
                            {
                                _logger.LogWarning(
                                    "Roster occurrence for rule {RuleId} at {StartUtc} was skipped: {Error}",
                                    rule.Id,
                                    startUtc,
                                    provisioned.ErrorMessage);
                            }
                            else
                            {
                                _dbContext.ScheduledEvents.Add(scheduleEvent);
                                try
                                {
                                    await _dbContext.SaveChangesAsync(cancellationToken);
                                }
                                catch (DbUpdateException exception)
                                {
                                    await _schedulingIntegrationService.CancelEventAsync(scheduleEvent, cancellationToken);
                                    _dbContext.Entry(scheduleEvent).State = EntityState.Detached;
                                    _logger.LogInformation(
                                        exception,
                                        "Roster occurrence for rule {RuleId} was created by another process.",
                                        rule.Id);
                                }
                            }
                        }
                    }

                    occurrenceDate = occurrenceDate.AddDays(7);
                }
            }

        }

        private async Task<ScheduledEvent?> BuildEventAsync(
            int courseClassId,
            string teacherUserId,
            DateTime startUtc,
            DateTime endUtc,
            string timeZoneId,
            int capacity,
            ScheduleDeliveryType deliveryType,
            string? meetingUrl,
            string? location,
            int bookingOpenDaysBefore,
            int bookingCloseHoursBefore,
            ScheduleEventSource source,
            int? ruleId,
            CancellationToken cancellationToken)
        {
            var courseClass = await _dbContext.CourseClasses
                .AsNoTracking()
                .Include(item => item.CourseSection)
                    .ThenInclude(section => section.Course)
                .FirstOrDefaultAsync(item => item.Id == courseClassId, cancellationToken);
            var teacher = await _dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == teacherUserId && !item.IsDeleted, cancellationToken);
            return courseClass == null || teacher == null
                ? null
                : BuildEvent(
                    courseClass,
                    teacher,
                    startUtc,
                    endUtc,
                    timeZoneId,
                    capacity,
                    deliveryType,
                    meetingUrl,
                    location,
                    bookingOpenDaysBefore,
                    bookingCloseHoursBefore,
                    source,
                    ruleId);
        }

        private async Task<ScheduledEvent?> BuildStudentEventAsync(
            string studentUserId,
            string teacherUserId,
            DateTime startUtc,
            DateTime endUtc,
            string timeZoneId,
            ScheduleDeliveryType deliveryType,
            string? meetingUrl,
            string? location,
            CancellationToken cancellationToken)
        {
            var student = await _dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == studentUserId && !item.IsDeleted, cancellationToken);
            var teacher = await _dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == teacherUserId && !item.IsDeleted, cancellationToken);
            if (student == null || teacher == null)
                return null;

            var studentEmail = student.Email ?? student.UserName ?? "Student";
            return new ScheduledEvent
            {
                TeacherUserId = teacher.Id,
                Title = $"Session with {studentEmail}",
                CourseNameSnapshot = "Private session",
                ClassNameSnapshot = studentEmail,
                TeacherNameSnapshot = FormatName(teacher.FirstName, teacher.LastName, teacher.UserName, teacher.Email),
                StartAtUtc = startUtc,
                EndAtUtc = endUtc,
                TimeZoneId = timeZoneId,
                Capacity = 1,
                DeliveryType = deliveryType,
                MeetingUrl = meetingUrl,
                Location = location,
                BookingOpensAtUtc = DateTime.UtcNow,
                BookingClosesAtUtc = startUtc,
                BookingAccess = CourseClassBookingAccess.CourseEnrollment,
                IsVisibleForStudentBooking = false,
                CreditCost = 1,
                Source = ScheduleEventSource.Manual,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private static ScheduledEvent BuildEvent(
            CourseClass courseClass,
            ApplicationUser teacher,
            DateTime startUtc,
            DateTime endUtc,
            string timeZoneId,
            int capacity,
            ScheduleDeliveryType deliveryType,
            string? meetingUrl,
            string? location,
            int bookingOpenDaysBefore,
            int bookingCloseHoursBefore,
            ScheduleEventSource source,
            int? ruleId)
        {
            var course = courseClass.CourseSection.Course;
            var normalizedCapacity = courseClass.Format == CourseClassFormat.Private
                ? 1
                : Math.Max(1, capacity);
            return new ScheduledEvent
            {
                ScheduleRosterRuleId = ruleId,
                CourseClassId = courseClass.Id,
                CourseId = course.Id,
                TeacherUserId = teacher.Id,
                Title = courseClass.Title,
                CourseNameSnapshot = course.Name,
                ClassNameSnapshot = courseClass.Title,
                TeacherNameSnapshot = FormatName(teacher.FirstName, teacher.LastName, teacher.UserName, teacher.Email),
                StartAtUtc = startUtc,
                EndAtUtc = endUtc,
                TimeZoneId = timeZoneId,
                Capacity = normalizedCapacity,
                DeliveryType = deliveryType,
                MeetingUrl = deliveryType == ScheduleDeliveryType.Zoom
                    ? meetingUrl
                    : meetingUrl ?? courseClass.MeetingLink,
                Location = location,
                BookingOpensAtUtc = startUtc.AddDays(-Math.Max(0, bookingOpenDaysBefore)),
                BookingClosesAtUtc = startUtc.AddHours(-Math.Max(0, bookingCloseHoursBefore)),
                BookingAccess = courseClass.BookingAccess,
                IsVisibleForStudentBooking = courseClass.IsVisibleForStudentBooking,
                RequiredCreditTypeId = courseClass.RequiredCreditTypeId,
                CreditCost = Math.Max(1, courseClass.CreditCost),
                CreditConsumptionPolicyId = courseClass.CreditConsumptionPolicyId,
                Source = source,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private ScheduleCalendarItem? BuildCalendarItem(
            ScheduledEvent scheduleEvent,
            ScheduleActor actor,
            IReadOnlyDictionary<int, UserCourse> courseAccess,
            IReadOnlyList<UserCreditLot> creditLots,
            IReadOnlyDictionary<int, int> enrollmentBookingCounts,
            IReadOnlyDictionary<int, IReadOnlyList<CourseSection>> sectionsByCourseId,
            bool staffCalendarView = false)
        {
            var canManage = CanManageTeacher(actor, scheduleEvent.TeacherUserId);
            var isAssignedTeacher = IsAssignedTeacher(actor, scheduleEvent.TeacherUserId);
            var canViewAsStaff = staffCalendarView && (actor.IsAdmin || actor.IsTeacher);
            var booking = scheduleEvent.Bookings.FirstOrDefault(item => item.UserId == actor.UserId);
            var hasBookingHistory = booking != null;
            var isBooked = booking != null && ActiveBookingStatuses.Contains(booking.Status);
            UserCourse? enrollment = null;
            var hasEnrollment = scheduleEvent.CourseId.HasValue &&
                                courseAccess.TryGetValue(scheduleEvent.CourseId.Value, out enrollment);
            var creditBalance = scheduleEvent.RequiredCreditTypeId.HasValue
                ? creditLots
                    .Where(lot => IsLotEligible(lot, scheduleEvent))
                    .Sum(lot => lot.RemainingQuantity)
                : 0;
            var needsEnrollment = scheduleEvent.BookingAccess is
                CourseClassBookingAccess.CourseEnrollment or
                CourseClassBookingAccess.CourseEnrollmentAndCredit;
            var needsCredit = scheduleEvent.BookingAccess is
                CourseClassBookingAccess.Credit or
                CourseClassBookingAccess.CourseEnrollmentAndCredit;
            var enrollmentBookingLimit = scheduleEvent.BookingAccess == CourseClassBookingAccess.CourseEnrollment
                ? scheduleEvent.CourseClass?.EnrollmentBookingLimit
                : null;
            var usedIncludedMeetings = scheduleEvent.CourseClassId.HasValue &&
                                       enrollmentBookingCounts.TryGetValue(scheduleEvent.CourseClassId.Value, out var usedCount)
                ? usedCount
                : 0;
            var withinEnrollmentLimit = !enrollmentBookingLimit.HasValue ||
                                        usedIncludedMeetings < enrollmentBookingLimit.Value ||
                                        isBooked;
            var bookingEligibleAtUtc = needsEnrollment && enrollment != null && scheduleEvent.CourseClass != null
                ? CalculateBookingEligibleAtUtc(
                    enrollment,
                    scheduleEvent.CourseClass,
                    scheduleEvent.CourseId.HasValue &&
                    sectionsByCourseId.TryGetValue(scheduleEvent.CourseId.Value, out var sections)
                        ? sections
                        : Array.Empty<CourseSection>())
                : (DateTime?)null;
            var withinCourseTimeline = !bookingEligibleAtUtc.HasValue || bookingEligibleAtUtc.Value <= DateTime.UtcNow;
            var eligible = (!needsEnrollment || hasEnrollment) &&
                           (!needsCredit || creditBalance >= Math.Max(1, scheduleEvent.CreditCost)) &&
                           withinEnrollmentLimit &&
                           withinCourseTimeline;

            if (!canManage && !canViewAsStaff && !scheduleEvent.IsVisibleForStudentBooking && !hasBookingHistory)
                return null;
            if (!canManage && !canViewAsStaff && !eligible && !hasBookingHistory && !hasEnrollment)
                return null;

            var activeCount = scheduleEvent.Bookings.Count(item => ActiveBookingStatuses.Contains(item.Status));
            var spotsRemaining = Math.Max(0, scheduleEvent.Capacity - activeCount);
            var now = DateTime.UtcNow;
            const bool isRecommended = false;
            var canBook = !canManage && !canViewAsStaff &&
                          eligible &&
                          !isBooked &&
                          scheduleEvent.Status == ScheduleEventStatus.Scheduled &&
                          spotsRemaining > 0 &&
                          now >= scheduleEvent.BookingOpensAtUtc &&
                          now <= scheduleEvent.BookingClosesAtUtc &&
                          scheduleEvent.StartAtUtc > now;
            var canCancel = !canManage &&
                            !canViewAsStaff &&
                            isBooked &&
                            scheduleEvent.Status == ScheduleEventStatus.Scheduled &&
                            scheduleEvent.StartAtUtc > now;
            var hasStudentJoinDetails = booking != null &&
                                        (scheduleEvent.DeliveryType != ScheduleDeliveryType.Zoom ||
                                         string.IsNullOrWhiteSpace(scheduleEvent.ZoomMeetingId) ||
                                         booking.ZoomRegistrationStatus == ZoomRegistrationStatus.Registered);
            var canJoin = !string.IsNullOrWhiteSpace(scheduleEvent.MeetingUrl) &&
                          scheduleEvent.Status == ScheduleEventStatus.Scheduled &&
                          (isAssignedTeacher || (!canViewAsStaff && isBooked && hasStudentJoinDetails)) &&
                          now >= scheduleEvent.StartAtUtc.AddMinutes(-JoinWindowMinutesBeforeStart) &&
                          now <= scheduleEvent.EndAtUtc.AddMinutes(JoinWindowMinutesAfterEnd);

            string? unavailableReason = null;
            if (!canManage && !isBooked && !canBook)
            {
                unavailableReason = !eligible
                        ? needsEnrollment && !hasEnrollment
                            ? "Course enrollment required"
                        : !withinCourseTimeline
                            ? $"Booking available {bookingEligibleAtUtc:dd MMM}"
                        : !withinEnrollmentLimit
                            ? "Included meeting limit reached"
                        : "Not enough eligible credits"
                    : scheduleEvent.Status != ScheduleEventStatus.Scheduled
                        ? "Meeting cancelled"
                        : spotsRemaining == 0
                            ? "Meeting full"
                            : now < scheduleEvent.BookingOpensAtUtc
                                ? $"Booking opens {scheduleEvent.BookingOpensAtUtc:dd MMM}"
                                : "Booking closed";
            }
            else if (!canManage && isBooked && scheduleEvent.DeliveryType == ScheduleDeliveryType.Zoom &&
                     !string.IsNullOrWhiteSpace(scheduleEvent.ZoomMeetingId) &&
                     booking?.ZoomRegistrationStatus != ZoomRegistrationStatus.Registered)
            {
                unavailableReason = booking?.ZoomRegistrationStatus == ZoomRegistrationStatus.Failed
                    ? "Your Zoom joining details are being retried"
                    : "Your Zoom joining details are being prepared";
            }

            var category = scheduleEvent.Status == ScheduleEventStatus.Cancelled
                ? "cancelled"
                : canManage || canViewAsStaff
                    ? "managed"
                    : isBooked
                        ? "booked"
                        : isRecommended
                            ? "recommended"
                            : "available";

            return new ScheduleCalendarItem(
                scheduleEvent.Id,
                scheduleEvent.CourseClassId,
                scheduleEvent.Title,
                scheduleEvent.StartAtUtc,
                scheduleEvent.EndAtUtc,
                scheduleEvent.CourseNameSnapshot,
                scheduleEvent.ClassNameSnapshot,
                scheduleEvent.TeacherNameSnapshot,
                AmsterdamTimeZoneId,
                scheduleEvent.Status,
                scheduleEvent.DeliveryType,
                scheduleEvent.Location,
                scheduleEvent.Capacity,
                activeCount,
                spotsRemaining,
                isBooked,
                isRecommended,
                canBook,
                canCancel,
                canJoin,
                canJoin ? $"/calendar?handler=Join&scheduledEventId={scheduleEvent.Id}" : null,
                canManage,
                category,
                unavailableReason);
        }

        private IQueryable<UserCreditLot> EligibleCreditLotsQuery(
            string userId,
            ScheduledEvent scheduleEvent,
            DateTime now)
        {
            var query = _dbContext.UserCreditLots.Where(lot =>
                lot.UserId == userId &&
                lot.CreditTypeId == scheduleEvent.RequiredCreditTypeId &&
                lot.RemainingQuantity > 0 &&
                (!lot.ExpiresAtUtc.HasValue || lot.ExpiresAtUtc > now));
            query = query.Where(lot =>
                lot.Scope == CreditGrantScope.Global ||
                (lot.Scope == CreditGrantScope.Course &&
                 scheduleEvent.CourseId.HasValue &&
                 lot.CourseId == scheduleEvent.CourseId) ||
                (lot.Scope == CreditGrantScope.CourseClass &&
                 scheduleEvent.CourseClassId.HasValue &&
                 lot.CourseClassId == scheduleEvent.CourseClassId));
            return query;
        }

        private static bool IsLotEligible(UserCreditLot lot, ScheduledEvent scheduleEvent)
            => lot.CreditTypeId == scheduleEvent.RequiredCreditTypeId &&
               (lot.Scope == CreditGrantScope.Global ||
                (lot.Scope == CreditGrantScope.Course && lot.CourseId == scheduleEvent.CourseId) ||
                (lot.Scope == CreditGrantScope.CourseClass && lot.CourseClassId == scheduleEvent.CourseClassId));

        private void ReturnBookingCredits(EventBooking booking, DateTime now, string description)
        {
            foreach (var allocation in booking.CreditAllocations.Where(item => !item.IsReturned && !item.IsConsumed))
            {
                allocation.UserCreditLot.RemainingQuantity += allocation.Amount;
                allocation.IsReturned = true;
                _dbContext.UserCreditTransactions.Add(new UserCreditTransaction
                {
                    UserId = booking.UserId,
                    CreditTypeId = allocation.UserCreditLot.CreditTypeId,
                    UserCreditLotId = allocation.UserCreditLotId,
                    EventBookingId = booking.Id,
                    TransactionType = CreditTransactionType.Return,
                    QuantityDelta = allocation.Amount,
                    Description = description,
                    CreatedAtUtc = now
                });
            }
            booking.CreditResolution = CreditResolutionAction.Return;
            booking.CreditResolvedAtUtc = now;
        }

        private void ConsumeBookingCredits(EventBooking booking, DateTime now, string description)
        {
            foreach (var allocation in booking.CreditAllocations.Where(item => !item.IsReturned && !item.IsConsumed))
            {
                allocation.IsConsumed = true;
                _dbContext.UserCreditTransactions.Add(new UserCreditTransaction
                {
                    UserId = booking.UserId,
                    CreditTypeId = allocation.UserCreditLot.CreditTypeId,
                    UserCreditLotId = allocation.UserCreditLotId,
                    EventBookingId = booking.Id,
                    TransactionType = CreditTransactionType.Consume,
                    QuantityDelta = 0,
                    Description = description,
                    CreatedAtUtc = now
                });
            }
            booking.CreditResolution = CreditResolutionAction.Consume;
            booking.CreditResolvedAtUtc = now;
        }

        private async Task<bool> CanManageClassAsync(
            ScheduleActor actor,
            int courseClassId,
            CancellationToken cancellationToken)
        {
            if (actor.IsAdmin)
                return await _dbContext.CourseClasses.AsNoTracking().AnyAsync(item => item.Id == courseClassId, cancellationToken);
            if (!actor.IsTeacher)
                return false;
            return await _dbContext.CourseClasses.AsNoTracking().AnyAsync(courseClass =>
                courseClass.Id == courseClassId &&
                (courseClass.CourseSection.Course.CreatedByUserId == actor.UserId ||
                 courseClass.CourseSection.Course.Instructors.Any(instructor => instructor.UserId == actor.UserId)),
                cancellationToken);
        }

        private async Task<bool> IsValidTeacherAsync(
            ScheduleActor actor,
            string userId,
            CancellationToken cancellationToken)
        {
            var userExists = await _dbContext.Users.AsNoTracking().AnyAsync(
                user => user.Id == userId && !user.IsDeleted,
                cancellationToken);
            if (!userExists)
                return false;
            if (!actor.IsAdmin)
                return actor.IsTeacher && userId == actor.UserId;

            var staffRoleIds = _dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Teacher" || role.Name == "Admin" || role.Name == "SuperAdmin")
                .Select(role => role.Id);
            return await _dbContext.UserRoles.AsNoTracking().AnyAsync(userRole =>
                userRole.UserId == userId && staffRoleIds.Contains(userRole.RoleId),
                cancellationToken);
        }

        private async Task<bool> IsValidStudentAsync(
            string userId,
            CancellationToken cancellationToken)
        {
            var studentRoleIds = _dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Student")
                .Select(role => role.Id);
            return await _dbContext.Users.AsNoTracking().AnyAsync(user =>
                user.Id == userId &&
                !user.IsDeleted &&
                _dbContext.UserRoles.Any(userRole =>
                    userRole.UserId == user.Id && studentRoleIds.Contains(userRole.RoleId)),
                cancellationToken);
        }

        private bool CanManageTeacher(ScheduleActor actor, string teacherUserId)
            => actor.IsAdmin || (actor.IsTeacher && teacherUserId == actor.UserId);

        private static bool IsAssignedTeacher(ScheduleActor actor, string teacherUserId)
            => (actor.IsTeacher || actor.IsAdmin) && teacherUserId == actor.UserId;

        private async Task<bool> HasTeacherConflictAsync(
            string teacherUserId,
            DateTime startUtc,
            DateTime endUtc,
            int? excludedEventId,
            CancellationToken cancellationToken)
            => await _dbContext.ScheduledEvents.AsNoTracking().AnyAsync(scheduleEvent =>
                scheduleEvent.TeacherUserId == teacherUserId &&
                scheduleEvent.Status == ScheduleEventStatus.Scheduled &&
                (!excludedEventId.HasValue || scheduleEvent.Id != excludedEventId.Value) &&
                scheduleEvent.StartAtUtc < endUtc &&
                scheduleEvent.EndAtUtc > startUtc,
                cancellationToken);

        private static OperationResult ValidateCommon(
            string? timeZoneId,
            int durationMinutes,
            int capacity,
            ScheduleDeliveryType deliveryType,
            string? meetingUrl,
            string? location,
            int bookingOpenDaysBefore,
            int bookingCloseHoursBefore)
        {
            if (FindTimeZone(timeZoneId) == null)
                return Failure("The selected time zone is not available.");
            if (durationMinutes is < 5 or > 1440)
                return Failure("Meeting duration must be between 5 minutes and 24 hours.");
            if (capacity is < 1 or > 10000)
                return Failure("Capacity must be between 1 and 10,000.");
            if (bookingOpenDaysBefore is < 0 or > 365)
                return Failure("Booking can open up to 365 days before a meeting.");
            if (bookingCloseHoursBefore is < 0 or > 8760)
                return Failure("Booking close time is invalid.");
            if (!string.IsNullOrWhiteSpace(meetingUrl) && NormalizeUrl(meetingUrl) == null)
                return Failure("Meeting links must use http or https.");
            if (deliveryType == ScheduleDeliveryType.InPerson && string.IsNullOrWhiteSpace(location))
                return Failure("Add the meeting location.");
            return Success();
        }

        private static bool DateRangesOverlap(
            DateOnly firstStart,
            DateOnly? firstEnd,
            DateOnly secondStart,
            DateOnly? secondEnd)
            => firstStart <= (secondEnd ?? DateOnly.MaxValue) &&
               secondStart <= (firstEnd ?? DateOnly.MaxValue);

        private static bool TimesOverlap(
            TimeOnly firstStart,
            int firstDuration,
            TimeOnly secondStart,
            int secondDuration)
        {
            var firstMinutes = firstStart.Hour * 60 + firstStart.Minute;
            var secondMinutes = secondStart.Hour * 60 + secondStart.Minute;
            return firstMinutes < secondMinutes + secondDuration &&
                   secondMinutes < firstMinutes + firstDuration;
        }

        private static bool FitsAvailability(
            TimeOnly meetingStart,
            int durationMinutes,
            TimeOnly availabilityStart,
            TimeOnly availabilityEnd)
        {
            var meetingStartMinutes = meetingStart.Hour * 60 + meetingStart.Minute;
            var meetingEndMinutes = meetingStartMinutes + durationMinutes;
            var availableStartMinutes = availabilityStart.Hour * 60 + availabilityStart.Minute;
            var availableEndMinutes = availabilityEnd.Hour * 60 + availabilityEnd.Minute;
            return meetingStartMinutes >= availableStartMinutes && meetingEndMinutes <= availableEndMinutes;
        }

        private static TimeZoneInfo? FindTimeZone(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                return null;
            }
            catch (InvalidTimeZoneException)
            {
                return null;
            }
        }

        private static string CanonicalTimeZoneId(string id)
        {
            var normalized = id.Trim();
            return TimeZoneInfo.TryConvertWindowsIdToIanaId(normalized, out var ianaId) &&
                   !string.IsNullOrWhiteSpace(ianaId)
                ? ianaId
                : normalized;
        }

        private static DateTime AddCourseDelay(
            DateTime value,
            int? amount,
            CourseUnlockUnit? unit)
        {
            if (!amount.HasValue || amount.Value <= 0 || !unit.HasValue)
                return value;
            return unit.Value switch
            {
                CourseUnlockUnit.Days => value.AddDays(amount.Value),
                CourseUnlockUnit.Weeks => value.AddDays(amount.Value * 7d),
                CourseUnlockUnit.Months => value.AddMonths(amount.Value),
                _ => value
            };
        }

        private static DateTime CalculateBookingEligibleAtUtc(
            UserCourse access,
            CourseClass courseClass,
            IReadOnlyList<CourseSection> courseSections)
        {
            var accessDate = access.PurchasedAtUtc ?? access.GrantedAtUtc;
            if (courseClass.BookingEligibility == CourseClassBookingEligibility.WhenPreviousSectionUnlocks)
            {
                var previousSection = courseSections
                    .Where(section =>
                        section.OrderNumber < courseClass.CourseSection.OrderNumber ||
                        section.OrderNumber == courseClass.CourseSection.OrderNumber && section.Id < courseClass.CourseSection.Id)
                    .OrderBy(section => section.OrderNumber)
                    .ThenBy(section => section.Id)
                    .LastOrDefault();
                return previousSection == null
                    ? accessDate
                    : AddCourseDelay(accessDate, previousSection.UnlockAfterValue, previousSection.UnlockAfterUnit);
            }

            var sectionUnlock = AddCourseDelay(
                accessDate,
                courseClass.CourseSection.UnlockAfterValue,
                courseClass.CourseSection.UnlockAfterUnit);
            var classUnlock = AddCourseDelay(
                accessDate,
                courseClass.UnlockAfterValue,
                courseClass.UnlockAfterUnit);
            return sectionUnlock >= classUnlock ? sectionUnlock : classUnlock;
        }

        private static DateTime EnsureUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

        private static string? NormalizeUrl(string? value)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 1024)
                return null;
            return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri.ToString()
                : null;
        }

        private static string? NormalizeText(string? value, int maxLength)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return null;
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }

        private static string FormatName(
            string? firstName,
            string? lastName,
            string? userName,
            string? email)
        {
            var fullName = string.Join(" ", new[] { firstName?.Trim(), lastName?.Trim() }
                .Where(item => !string.IsNullOrWhiteSpace(item)));
            return !string.IsNullOrWhiteSpace(fullName)
                ? fullName
                : userName ?? email ?? "Teacher";
        }

        private static OperationResult Success()
            => new() { Success = true, ErrorMessage = string.Empty };

        private static OperationResult Failure(string message)
            => new() { Success = false, ErrorMessage = message };
    }
}
