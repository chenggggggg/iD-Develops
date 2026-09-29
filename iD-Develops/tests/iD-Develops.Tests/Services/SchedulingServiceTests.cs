using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace iD_Develops.Tests.Services;

public sealed class SchedulingServiceTests
{
    [Fact]
    public async Task BookAsync_EnforcesCapacityAndDuplicateBookings()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, capacity: 1);
        var secondStudent = CreateUser("student-2", "second@example.com", "Second", "Student");
        db.Users.Add(secondStudent);
        db.UserCourses.Add(new UserCourse
        {
            UserId = secondStudent.Id,
            CourseId = seed.Course.Id,
            GrantedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var firstActor = new ScheduleActor(seed.Student.Id, false, false);
        var secondActor = new ScheduleActor(secondStudent.Id, false, false);

        var first = await service.BookAsync(firstActor, seed.Event.Id);
        var duplicate = await service.BookAsync(firstActor, seed.Event.Id);
        var full = await service.BookAsync(secondActor, seed.Event.Id);

        Assert.True(first.Success);
        Assert.False(duplicate.Success);
        Assert.Contains("already booked", duplicate.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(full.Success);
        Assert.Contains("full", full.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.EventBookings.CountAsync(item => item.Status == EventBookingStatus.Confirmed));
    }

    [Fact]
    public async Task CourseEnrollmentLimit_AllowsIncludedMeetingWithoutCreditsAndReleasesItAfterCancellation()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, bookingAccess: CourseClassBookingAccess.CourseEnrollment);
        seed.CourseClass.EnrollmentBookingLimit = 1;
        var secondStart = DateTime.UtcNow.AddDays(14);
        var secondEvent = new ScheduledEvent
        {
            CourseClassId = seed.CourseClass.Id,
            CourseId = seed.Course.Id,
            TeacherUserId = seed.Teacher.Id,
            Title = seed.CourseClass.Title,
            CourseNameSnapshot = seed.Course.Name,
            ClassNameSnapshot = seed.CourseClass.Title,
            TeacherNameSnapshot = seed.Teacher.FullName,
            StartAtUtc = secondStart,
            EndAtUtc = secondStart.AddHours(1),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
            Capacity = 3,
            DeliveryType = ScheduleDeliveryType.Zoom,
            MeetingUrl = "https://zoom.example.test/second",
            BookingOpensAtUtc = DateTime.UtcNow.AddDays(-1),
            BookingClosesAtUtc = secondStart.AddHours(-1),
            BookingAccess = CourseClassBookingAccess.CourseEnrollment
        };
        db.ScheduledEvents.Add(secondEvent);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var actor = new ScheduleActor(seed.Student.Id, false, false);

        var first = await service.BookAsync(actor, seed.Event.Id);
        var limited = await service.BookAsync(actor, secondEvent.Id);

        Assert.True(first.Success, first.ErrorMessage);
        Assert.False(limited.Success);
        Assert.Contains("included meeting", limited.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.UserCreditTransactions);

        var cancelled = await service.CancelBookingAsync(actor, seed.Event.Id);
        var replacement = await service.BookAsync(actor, secondEvent.Id);

        Assert.True(cancelled.Success, cancelled.ErrorMessage);
        Assert.True(replacement.Success, replacement.ErrorMessage);
        Assert.Empty(db.UserCreditTransactions);
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsReservedCreditsAccordingToPolicy()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(
            db,
            bookingAccess: CourseClassBookingAccess.CourseEnrollmentAndCredit,
            creditCost: 2);
        var creditType = new CreditType
        {
            Name = "Private meeting",
            NormalizedName = "PRIVATE MEETING",
            SingularLabel = "meeting",
            PluralLabel = "meetings"
        };
        var policy = new CreditConsumptionPolicy
        {
            Name = "Return before 24 hours",
            NormalizedName = "RETURN BEFORE 24 HOURS",
            CancellationWindowHours = 24,
            EarlyCancellationAction = CreditResolutionAction.Return,
            LateCancellationAction = CreditResolutionAction.Consume
        };
        db.CreditTypes.Add(creditType);
        db.CreditConsumptionPolicies.Add(policy);
        await db.SaveChangesAsync();

        seed.CourseClass.RequiredCreditTypeId = creditType.Id;
        seed.CourseClass.CreditConsumptionPolicyId = policy.Id;
        seed.Event.RequiredCreditTypeId = creditType.Id;
        seed.Event.CreditConsumptionPolicyId = policy.Id;
        db.UserCreditLots.Add(new UserCreditLot
        {
            UserId = seed.Student.Id,
            CreditTypeId = creditType.Id,
            GrantedQuantity = 3,
            RemainingQuantity = 3,
            Scope = CreditGrantScope.Course,
            CourseId = seed.Course.Id,
            GrantedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var actor = new ScheduleActor(seed.Student.Id, false, false);

        var booked = await service.BookAsync(actor, seed.Event.Id);
        Assert.True(booked.Success);
        Assert.Equal(1, await db.UserCreditLots.Select(item => item.RemainingQuantity).SingleAsync());

        var cancelled = await service.CancelBookingAsync(actor, seed.Event.Id);

        Assert.True(cancelled.Success);
        Assert.Equal(3, await db.UserCreditLots.Select(item => item.RemainingQuantity).SingleAsync());
        Assert.Equal(2, await db.UserCreditTransactions.CountAsync());
        Assert.True(await db.EventBookingCreditAllocations.AllAsync(item => item.IsReturned));
    }

    [Fact]
    public async Task SaveRosterRuleAsync_GeneratesRecurringFutureMeetings()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        db.ScheduledEvents.Remove(seed.Event);
        await db.SaveChangesAsync();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        db.TeacherAvailabilityWindows.Add(new TeacherAvailabilityWindow
        {
            TeacherUserId = seed.Teacher.Id,
            DayOfWeek = tomorrow.DayOfWeek,
            LocalStartTime = new TimeOnly(9, 0),
            LocalEndTime = new TimeOnly(17, 0),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SaveRosterRuleAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            new ScheduleRosterRuleInput
            {
                CourseClassId = seed.CourseClass.Id,
                DayOfWeek = tomorrow.DayOfWeek,
                LocalStartTime = new TimeOnly(12, 0),
                TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
                ActiveFromDate = tomorrow,
                ActiveUntilDate = tomorrow.AddDays(28),
                DurationMinutes = 45,
                Capacity = 4,
                DeliveryType = ScheduleDeliveryType.Zoom,
                MeetingUrl = "https://zoom.us/j/123456",
                BookingOpenDaysBefore = 30,
                BookingCloseHoursBefore = 1,
                GenerateWeeksAhead = 4,
                IsActive = true
            });

        Assert.True(result.Success, result.ErrorMessage);
        var generated = await db.ScheduledEvents
            .Where(item => item.Source == ScheduleEventSource.Roster)
            .OrderBy(item => item.StartAtUtc)
            .ToListAsync();
        Assert.NotEmpty(generated);
        Assert.All(generated, item => Assert.Equal(seed.CourseClass.DurationMinutes, (item.EndAtUtc - item.StartAtUtc).TotalMinutes));
        Assert.All(generated, item => Assert.Equal(seed.CourseClass.Capacity, item.Capacity));
        Assert.Equal(generated.Count, generated.Select(item => item.StartAtUtc).Distinct().Count());
        Assert.Equal(generated.Count, generated.Select(item => item.MeetingUrl).Distinct().Count());
        Assert.All(generated, item => Assert.False(string.IsNullOrWhiteSpace(item.ZoomMeetingId)));
    }

    [Fact]
    public async Task SaveRosterRuleAsync_RequiresEndDate()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var service = CreateService(db);

        var result = await service.SaveRosterRuleAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            new ScheduleRosterRuleInput
            {
                CourseClassId = seed.CourseClass.Id,
                DayOfWeek = tomorrow.DayOfWeek,
                LocalStartTime = new TimeOnly(12, 0),
                TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
                ActiveFromDate = tomorrow,
                DeliveryType = ScheduleDeliveryType.Zoom
            });

        Assert.False(result.Success);
        Assert.Contains("ends", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveRosterRuleAsync_DoesNotBlockTeacherOutsideAvailability()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        db.ScheduledEvents.Remove(seed.Event);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        db.TeacherAvailabilityWindows.Add(new TeacherAvailabilityWindow
        {
            TeacherUserId = seed.Teacher.Id,
            DayOfWeek = tomorrow.DayOfWeek,
            LocalStartTime = new TimeOnly(9, 0),
            LocalEndTime = new TimeOnly(12, 0),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SaveRosterRuleAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            new ScheduleRosterRuleInput
            {
                CourseClassId = seed.CourseClass.Id,
                DayOfWeek = tomorrow.DayOfWeek,
                LocalStartTime = new TimeOnly(11, 30),
                TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
                ActiveFromDate = tomorrow,
                ActiveUntilDate = tomorrow.AddDays(7),
                DeliveryType = ScheduleDeliveryType.Zoom
            });

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Fact]
    public async Task CreateManualEventAsync_WithStudent_CreatesDirectBookingAndGeneratedZoomMeeting()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var studentRole = new IdentityRole("Student") { Id = "role-student", NormalizedName = "STUDENT" };
        db.Roles.Add(studentRole);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = seed.Student.Id, RoleId = studentRole.Id });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.CreateManualEventAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            new ManualScheduleEventInput
            {
                StudentUserId = seed.Student.Id,
                StartLocal = DateTime.UtcNow.AddDays(2),
                TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
                DurationMinutes = 45,
                DeliveryType = ScheduleDeliveryType.Zoom
            });

        Assert.True(result.Success, result.ErrorMessage);
        var directEvent = await db.ScheduledEvents
            .Include(item => item.Bookings)
            .SingleAsync(item => item.CourseClassId == null);
        Assert.Equal(1, directEvent.Capacity);
        Assert.NotNull(directEvent.ZoomMeetingId);
        Assert.Equal(seed.Student.Id, Assert.Single(directEvent.Bookings).UserId);
        Assert.Contains(seed.Student.Email!, directEvent.Title);
    }

    [Fact]
    public async Task GetStudentCalendarItemsAsync_StaffCanViewBookedMeetingsReadOnly()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var studentRole = new IdentityRole("Student") { Id = "role-student", NormalizedName = "STUDENT" };
        db.Roles.Add(studentRole);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = seed.Student.Id, RoleId = studentRole.Id });
        db.EventBookings.Add(new EventBooking
        {
            ScheduledEventId = seed.Event.Id,
            UserId = seed.Student.Id,
            Status = EventBookingStatus.Confirmed,
            BookedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var items = await service.GetStudentCalendarItemsAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            seed.Student.Id,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(30));

        var item = Assert.Single(items);
        Assert.True(item.IsBooked);
        Assert.False(item.CanBook);
        Assert.False(item.CanCancelBooking);
        Assert.False(item.CanJoin);
        Assert.False(item.CanManage);
        Assert.Equal("booked", item.Category);
    }

    [Fact]
    public async Task CalendarActions_OnlyAllowCancellationForActiveBookingAndJoinInsideWindow()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, eventStart: DateTime.UtcNow.AddDays(14));
        var service = CreateService(db);
        var actor = new ScheduleActor(seed.Student.Id, false, false);

        var unbooked = Assert.Single(await service.GetCalendarItemsAsync(
            actor,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(30)));
        Assert.False(unbooked.CanCancelBooking);
        Assert.False(unbooked.CanJoin);

        db.EventBookings.Add(new EventBooking
        {
            ScheduledEventId = seed.Event.Id,
            UserId = seed.Student.Id,
            Status = EventBookingStatus.Confirmed,
            BookedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var futureBooking = Assert.Single(await service.GetCalendarItemsAsync(
            actor,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(30)));
        Assert.True(futureBooking.CanCancelBooking);
        Assert.False(futureBooking.CanJoin);
        var earlyJoin = await service.GetJoinUrlAsync(actor, seed.Event.Id);
        Assert.False(earlyJoin.Success);
        Assert.Contains("starts", earlyJoin.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        seed.Event.StartAtUtc = DateTime.UtcNow.AddMinutes(-1);
        seed.Event.EndAtUtc = seed.Event.StartAtUtc.AddHours(1);
        await db.SaveChangesAsync();
        var liveBooking = Assert.Single(await service.GetCalendarItemsAsync(
            actor,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(2)));
        Assert.False(liveBooking.CanCancelBooking);
        Assert.True(liveBooking.CanJoin);
    }

    [Fact]
    public async Task GetCalendarItemsAsync_CancelledMeetingUsesCancelledCategoryForStudent()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, eventStart: DateTime.UtcNow.AddDays(14));
        db.EventBookings.Add(new EventBooking
        {
            ScheduledEventId = seed.Event.Id,
            UserId = seed.Student.Id,
            Status = EventBookingStatus.Cancelled,
            BookedAtUtc = DateTime.UtcNow.AddDays(-1),
            CancelledAtUtc = DateTime.UtcNow
        });
        seed.Event.Status = ScheduleEventStatus.Cancelled;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var item = Assert.Single(await service.GetCalendarItemsAsync(
            new ScheduleActor(seed.Student.Id, false, false),
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(30)));

        Assert.Equal("cancelled", item.Category);
        Assert.False(item.IsBooked);
        Assert.False(item.CanBook);
        Assert.False(item.CanCancelBooking);
        Assert.False(item.CanJoin);
    }

    [Fact]
    public async Task GetOverviewAsync_SeparatesParticipatingAndAvailableEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, eventStart: DateTime.UtcNow.AddDays(7));
        var service = CreateService(db);
        var actor = new ScheduleActor(seed.Student.Id, false, false);

        var beforeBooking = await service.GetOverviewAsync(actor);

        Assert.Equal(0, beforeBooking.UpcomingCount);
        Assert.Empty(beforeBooking.UpcomingItems);
        Assert.Equal(seed.Event.Id, Assert.Single(beforeBooking.AvailableItems).Id);

        db.EventBookings.Add(new EventBooking
        {
            ScheduledEventId = seed.Event.Id,
            UserId = seed.Student.Id,
            Status = EventBookingStatus.Confirmed,
            BookedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var afterBooking = await service.GetOverviewAsync(actor);

        Assert.Equal(1, afterBooking.UpcomingCount);
        Assert.Equal(seed.Event.Id, Assert.Single(afterBooking.UpcomingItems).Id);
        Assert.Empty(afterBooking.AvailableItems);
    }

    [Fact]
    public async Task GetOverviewAsync_TeacherIncludesAssignedAndBookedEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, eventStart: DateTime.UtcNow.AddDays(7));
        var otherTeacher = CreateUser("other-teacher", "other@example.com", "Other", "Teacher");
        var bookedStart = DateTime.UtcNow.AddDays(8);
        var bookedEvent = new ScheduledEvent
        {
            TeacherUserId = otherTeacher.Id,
            Title = "Teacher development meeting",
            CourseNameSnapshot = "Staff development",
            ClassNameSnapshot = "Teacher development meeting",
            TeacherNameSnapshot = otherTeacher.FullName,
            StartAtUtc = bookedStart,
            EndAtUtc = bookedStart.AddHours(1),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
            Capacity = 5,
            DeliveryType = ScheduleDeliveryType.Zoom,
            MeetingUrl = "https://zoom.us/j/teacher-booking",
            BookingOpensAtUtc = bookedStart.AddDays(-30),
            BookingClosesAtUtc = bookedStart.AddHours(-1)
        };
        db.Users.Add(otherTeacher);
        db.ScheduledEvents.Add(bookedEvent);
        db.EventBookings.Add(new EventBooking
        {
            ScheduledEvent = bookedEvent,
            UserId = seed.Teacher.Id,
            Status = EventBookingStatus.Confirmed,
            BookedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var overview = await service.GetOverviewAsync(
            new ScheduleActor(seed.Teacher.Id, false, true));

        Assert.Equal(2, overview.UpcomingCount);
        Assert.Contains(overview.UpcomingItems, item => item.Id == seed.Event.Id && item.CanManage);
        Assert.Contains(overview.UpcomingItems, item => item.Id == bookedEvent.Id && item.IsBooked);
        Assert.Empty(overview.AvailableItems);
    }

    [Fact]
    public async Task GetCourseClassItemsAsync_WhenBookedShowsOnlySelectedSlot()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, eventStart: DateTime.UtcNow.AddDays(7));
        var secondStart = DateTime.UtcNow.AddDays(8);
        var secondEvent = new ScheduledEvent
        {
            CourseClassId = seed.CourseClass.Id,
            CourseId = seed.Course.Id,
            TeacherUserId = seed.Teacher.Id,
            Title = seed.Event.Title,
            CourseNameSnapshot = seed.Event.CourseNameSnapshot,
            ClassNameSnapshot = seed.Event.ClassNameSnapshot,
            TeacherNameSnapshot = seed.Event.TeacherNameSnapshot,
            StartAtUtc = secondStart,
            EndAtUtc = secondStart.AddHours(1),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
            Capacity = 3,
            DeliveryType = ScheduleDeliveryType.Zoom,
            MeetingUrl = "https://zoom.us/j/second",
            BookingOpensAtUtc = secondStart.AddDays(-30),
            BookingClosesAtUtc = secondStart.AddHours(-1),
            BookingAccess = CourseClassBookingAccess.CourseEnrollment,
            CreditCost = 1
        };
        db.ScheduledEvents.Add(secondEvent);
        db.EventBookings.Add(new EventBooking
        {
            ScheduledEventId = seed.Event.Id,
            UserId = seed.Student.Id,
            Status = EventBookingStatus.Confirmed,
            BookedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var items = await service.GetCourseClassItemsAsync(
            new ScheduleActor(seed.Student.Id, false, false),
            seed.CourseClass.Id);

        var selectedSlot = Assert.Single(items);
        Assert.Equal(seed.Event.Id, selectedSlot.Id);
        Assert.True(selectedSlot.IsBooked);
    }

    [Fact]
    public async Task SaveAvailabilityAsync_AlwaysStoresAmsterdamTime()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var service = CreateService(db);

        var result = await service.SaveAvailabilityAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            new TeacherAvailabilityInput
            {
                TimeZoneId = "Asia/Bangkok",
                Days =
                [
                    new TeacherAvailabilityDayInput
                    {
                        DayOfWeek = DayOfWeek.Monday,
                        IsAvailable = true,
                        LocalStartTime = new TimeOnly(9, 0),
                        LocalEndTime = new TimeOnly(17, 0)
                    }
                ]
            });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(
            SchedulingService.AmsterdamTimeZoneId,
            await db.TeacherAvailabilityWindows.Select(item => item.TimeZoneId).SingleAsync());
    }

    [Fact]
    public async Task StaffCalendar_CanShowAllTeachersOrOneTeacherWithoutGrantingCrossTeacherActions()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var otherTeacher = CreateUser("teacher-2", "teacher2@example.com", "Other", "Teacher");
        var teacherRole = new IdentityRole("Teacher") { Id = "role-teacher", NormalizedName = "TEACHER" };
        db.Users.Add(otherTeacher);
        db.Roles.Add(teacherRole);
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = seed.Teacher.Id, RoleId = teacherRole.Id },
            new IdentityUserRole<string> { UserId = otherTeacher.Id, RoleId = teacherRole.Id });
        db.ScheduledEvents.Add(new ScheduledEvent
        {
            CourseClassId = seed.CourseClass.Id,
            CourseId = seed.Course.Id,
            TeacherUserId = otherTeacher.Id,
            Title = "Other teacher class",
            CourseNameSnapshot = seed.Course.Name,
            ClassNameSnapshot = "Other teacher class",
            TeacherNameSnapshot = otherTeacher.FullName,
            StartAtUtc = DateTime.UtcNow.AddDays(8),
            EndAtUtc = DateTime.UtcNow.AddDays(8).AddHours(1),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
            Capacity = 3,
            DeliveryType = ScheduleDeliveryType.Zoom,
            MeetingUrl = "https://zoom.example.test/other",
            BookingOpensAtUtc = DateTime.UtcNow,
            BookingClosesAtUtc = DateTime.UtcNow.AddDays(8).AddHours(-1)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var actor = new ScheduleActor(seed.Teacher.Id, false, true);

        var all = await service.GetStaffCalendarItemsAsync(
            actor, null, true, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));
        var selected = await service.GetStaffCalendarItemsAsync(
            actor, otherTeacher.Id, false, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        Assert.Equal(2, all.Count);
        var other = Assert.Single(selected);
        Assert.Equal(otherTeacher.Id, (await db.ScheduledEvents.SingleAsync(item => item.Id == other.Id)).TeacherUserId);
        Assert.False(other.CanManage);
        Assert.False(other.CanJoin);
        Assert.False(other.CanCancelBooking);
    }

    [Fact]
    public async Task GetAvailableTeachersAsync_FiltersByStartTimeAndDuration()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var localDate = DateTime.UtcNow.Date.AddDays(1);
        db.TeacherAvailabilityWindows.Add(new TeacherAvailabilityWindow
        {
            TeacherUserId = seed.Teacher.Id,
            DayOfWeek = localDate.DayOfWeek,
            LocalStartTime = new TimeOnly(9, 0),
            LocalEndTime = new TimeOnly(12, 0),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var admin = new ScheduleActor("admin", true, false);

        var available = await service.GetAvailableTeachersAsync(
            admin,
            localDate.AddHours(10).AddMinutes(30),
            "UTC",
            60);
        var tooLate = await service.GetAvailableTeachersAsync(
            admin,
            localDate.AddHours(11).AddMinutes(30),
            "UTC",
            60);

        Assert.Equal(seed.Teacher.Id, Assert.Single(available).Id);
        Assert.Empty(tooLate);
    }

    [Fact]
    public async Task SaveRosterRuleAsync_TeacherCannotAssignAnotherTeacher()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var otherTeacher = CreateUser("teacher-2", "teacher2@example.com", "Other", "Teacher");
        db.Users.Add(otherTeacher);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SaveRosterRuleAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            new ScheduleRosterRuleInput
            {
                TeacherUserId = otherTeacher.Id,
                CourseClassId = seed.CourseClass.Id,
                DayOfWeek = DayOfWeek.Monday,
                LocalStartTime = new TimeOnly(9, 0),
                TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
                ActiveFromDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                DeliveryType = ScheduleDeliveryType.Zoom
            });

        Assert.False(result.Success);
        Assert.Contains("themselves", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ScheduleRosterRules.ToListAsync());
    }

    [Fact]
    public async Task CreateManualEventAsync_TeacherCannotAssignAnotherTeacher()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db);
        var otherTeacher = CreateUser("teacher-2", "teacher2@example.com", "Other", "Teacher");
        db.Users.Add(otherTeacher);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.CreateManualEventAsync(
            new ScheduleActor(seed.Teacher.Id, false, true),
            new ManualScheduleEventInput
            {
                TeacherUserId = otherTeacher.Id,
                CourseClassId = seed.CourseClass.Id,
                StartLocal = DateTime.UtcNow.AddDays(2),
                TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
                DeliveryType = ScheduleDeliveryType.Zoom,
                MeetingUrl = "https://zoom.example.test/j/manual"
            });

        Assert.False(result.Success);
        Assert.Contains("themselves", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.ScheduledEvents.CountAsync());
    }

    [Fact]
    public async Task GetCalendarItemsAsync_IgnoresLegacyRecommendationSettings()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var accessDate = DateTime.UtcNow.Date;
        var seed = await SeedCourseAsync(db, eventStart: accessDate.AddDays(37).AddHours(10));
        seed.CourseClass.IsRecommended = true;
        seed.CourseClass.RecommendedAfterValue = 5;
        seed.CourseClass.RecommendedAfterUnit = CourseUnlockUnit.Weeks;
        seed.CourseClass.RecommendationWindowValue = 1;
        seed.CourseClass.RecommendationWindowUnit = CourseUnlockUnit.Weeks;
        var access = await db.UserCourses.SingleAsync(item => item.UserId == seed.Student.Id);
        access.GrantedAtUtc = accessDate;
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var items = await service.GetCalendarItemsAsync(
            new ScheduleActor(seed.Student.Id, false, false),
            accessDate.AddDays(30),
            accessDate.AddDays(50));

        var item = Assert.Single(items);
        Assert.False(item.IsRecommended);
        Assert.Equal("available", item.Category);
        Assert.False(item.CanBook);
        Assert.Contains("Booking opens", item.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BookAsync_WhenPreviousSectionMustUnlock_EnforcesCourseTimeline()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedCourseAsync(db, eventStart: DateTime.UtcNow.AddDays(20));
        var firstSection = await db.CourseSections.SingleAsync(section => section.Id == seed.CourseClass.CourseSectionId);
        firstSection.Title = "Foundation";
        firstSection.UnlockAfterValue = 7;
        firstSection.UnlockAfterUnit = CourseUnlockUnit.Days;
        var classSection = new CourseSection
        {
            CourseId = seed.Course.Id,
            Title = "Live practice",
            OrderNumber = 2,
            UnlockAfterValue = 14,
            UnlockAfterUnit = CourseUnlockUnit.Days
        };
        db.CourseSections.Add(classSection);
        seed.CourseClass.CourseSection = classSection;
        seed.CourseClass.BookingEligibility = CourseClassBookingEligibility.WhenPreviousSectionUnlocks;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var actor = new ScheduleActor(seed.Student.Id, false, false);

        var tooEarly = await service.BookAsync(actor, seed.Event.Id);

        Assert.False(tooEarly.Success);
        Assert.Contains("Booking becomes available", tooEarly.ErrorMessage);

        var enrollment = await db.UserCourses.SingleAsync(access => access.UserId == seed.Student.Id);
        enrollment.GrantedAtUtc = DateTime.UtcNow.AddDays(-8);
        await db.SaveChangesAsync();

        var available = await service.BookAsync(actor, seed.Event.Id);

        Assert.True(available.Success);
    }

    private static SchedulingService CreateService(iD_Develops.Data.ApplicationDbContext db)
        => new(db, new FakeSchedulingIntegrationService(), NullLogger<SchedulingService>.Instance);

    private static async Task<ScheduleSeed> SeedCourseAsync(
        iD_Develops.Data.ApplicationDbContext db,
        int capacity = 3,
        CourseClassBookingAccess bookingAccess = CourseClassBookingAccess.CourseEnrollment,
        int creditCost = 1,
        DateTime? eventStart = null)
    {
        var teacher = CreateUser("teacher", "teacher@example.com", "Taylor", "Teacher");
        var student = CreateUser("student", "student@example.com", "Sam", "Student");
        var course = new Course
        {
            Name = "Speaking Course",
            CreatedByUserId = teacher.Id
        };
        var section = new CourseSection
        {
            Course = course,
            Title = "Live classes",
            OrderNumber = 1
        };
        var courseClass = new CourseClass
        {
            CourseSection = section,
            Title = "Speaking class",
            OrderNumber = 1,
            DurationMinutes = 60,
            Capacity = capacity,
            BookingAccess = bookingAccess,
            CreditCost = creditCost
        };
        db.Users.AddRange(teacher, student);
        db.Courses.Add(course);
        db.CourseSections.Add(section);
        db.CourseClasses.Add(courseClass);
        await db.SaveChangesAsync();
        db.UserCourses.Add(
            new UserCourse
            {
                UserId = student.Id,
                CourseId = course.Id,
                GrantedAtUtc = DateTime.UtcNow,
                AssignmentSource = CourseAssignmentSource.Purchase
            });

        var startsAt = DateTime.SpecifyKind(eventStart ?? DateTime.UtcNow.AddDays(7), DateTimeKind.Utc);
        var scheduleEvent = new ScheduledEvent
        {
            CourseClassId = courseClass.Id,
            CourseId = course.Id,
            TeacherUserId = teacher.Id,
            Title = courseClass.Title,
            CourseNameSnapshot = course.Name,
            ClassNameSnapshot = courseClass.Title,
            TeacherNameSnapshot = teacher.FullName,
            StartAtUtc = startsAt,
            EndAtUtc = startsAt.AddHours(1),
            TimeZoneId = SchedulingService.AmsterdamTimeZoneId,
            Capacity = capacity,
            DeliveryType = ScheduleDeliveryType.Zoom,
            MeetingUrl = "https://zoom.us/j/123456",
            BookingOpensAtUtc = startsAt.AddDays(-30),
            BookingClosesAtUtc = startsAt.AddHours(-1),
            BookingAccess = bookingAccess,
            CreditCost = creditCost
        };
        db.ScheduledEvents.Add(scheduleEvent);
        await db.SaveChangesAsync();
        return new ScheduleSeed(teacher, student, course, courseClass, scheduleEvent);
    }

    private static ApplicationUser CreateUser(string id, string email, string firstName, string lastName)
        => new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true
        };

    private sealed record ScheduleSeed(
        ApplicationUser Teacher,
        ApplicationUser Student,
        Course Course,
        CourseClass CourseClass,
        ScheduledEvent Event);
}
