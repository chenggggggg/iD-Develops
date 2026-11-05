using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace iD_Develops.Tests.Services;

public sealed class AppointmentServiceTests
{
    [Fact]
    public async Task StudentOptions_ShowRequiredCreditDurationAndBalance()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var service = CreateService(db);

        var options = await service.GetStudentOptionsAsync(new ScheduleActor(seed.Student.Id, false, false));

        var option = Assert.Single(options);
        Assert.Equal("Private coaching", option.Name);
        Assert.Equal(60, option.DurationMinutes);
        Assert.Equal(2, option.CreditCost);
        Assert.Equal(3, option.AvailableCreditQuantity);
        Assert.Equal(seed.Teacher.Id, Assert.Single(option.Teachers).Id);
    }

    [Fact]
    public async Task Slots_DisableTimesThatOverlapPortalEvents()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var blockedStart = AmsterdamUtc(localDate, new TimeOnly(10, 0));
        db.ScheduledEvents.Add(new ScheduledEvent
        {
            TeacherUserId = seed.Teacher.Id,
            Title = "Existing",
            CourseNameSnapshot = "Existing",
            ClassNameSnapshot = "Existing",
            TeacherNameSnapshot = seed.Teacher.FullName,
            StartAtUtc = blockedStart,
            EndAtUtc = blockedStart.AddHours(1),
            BookingOpensAtUtc = DateTime.UtcNow,
            BookingClosesAtUtc = blockedStart
        });
        db.TeacherAvailabilityWindows.Add(new TeacherAvailabilityWindow
        {
            TeacherUserId = seed.Teacher.Id,
            DayOfWeek = localDate.DayOfWeek,
            LocalStartTime = new TimeOnly(9, 0),
            LocalEndTime = new TimeOnly(12, 0)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var slots = await service.GetSlotsAsync(
            new ScheduleActor(seed.Student.Id, false, false),
            seed.Type.Id,
            seed.Teacher.Id,
            localDate,
            1);

        Assert.Contains(slots, item => item.StartUtc == blockedStart && !item.IsAvailable && item.UnavailableReason == "Already scheduled");
        Assert.Contains(slots, item => item.StartUtc == AmsterdamUtc(localDate, new TimeOnly(9, 0)) && item.IsAvailable);
    }

    [Fact]
    public async Task BookAppointment_CreatesPrivateEventAndReservesCredits()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        db.TeacherAvailabilityWindows.Add(new TeacherAvailabilityWindow
        {
            TeacherUserId = seed.Teacher.Id,
            DayOfWeek = localDate.DayOfWeek,
            LocalStartTime = new TimeOnly(9, 0),
            LocalEndTime = new TimeOnly(12, 0)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var startUtc = AmsterdamUtc(localDate, new TimeOnly(9, 0));

        var result = await service.BookAppointmentAsync(
            new ScheduleActor(seed.Student.Id, false, false),
            seed.Type.Id,
            seed.Teacher.Id,
            startUtc);

        Assert.True(result.Success, result.ErrorMessage);
        var scheduleEvent = await db.ScheduledEvents.Include(item => item.Bookings).SingleAsync();
        Assert.Equal(ScheduleEventSource.StudentAppointment, scheduleEvent.Source);
        Assert.Equal(seed.Type.Id, scheduleEvent.AppointmentTypeId);
        Assert.Equal(seed.Student.Id, Assert.Single(scheduleEvent.Bookings).UserId);
        Assert.Equal(1, (await db.UserCreditLots.SingleAsync()).RemainingQuantity);
    }

    [Fact]
    public async Task SaveFreeType_NeedsNoCreditCatalogOrPolicy()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        db.AppointmentTypes.Remove(seed.Type);
        db.UserCreditLots.RemoveRange(db.UserCreditLots);
        await db.SaveChangesAsync();
        db.CreditTypes.RemoveRange(db.CreditTypes);
        db.CreditConsumptionPolicies.RemoveRange(db.CreditConsumptionPolicies);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SaveAppointmentTypeAsync(new(seed.Teacher.Id, false, true), new()
        {
            Name = "Free introduction",
            DurationMinutes = 20,
            CreditCost = 99,
            CreditConsumptionPolicyId = 999
        });

        Assert.True(result.Success, result.ErrorMessage);
        var saved = await db.AppointmentTypes.Include(item => item.Teachers).SingleAsync();
        Assert.Null(saved.RequiredCreditTypeId);
        Assert.Null(saved.CreditConsumptionPolicyId);
        Assert.Equal(0, saved.CreditCost);
        Assert.Equal(seed.Teacher.Id, Assert.Single(saved.Teachers).TeacherUserId);
        var option = Assert.Single(await CreateService(db).GetStudentOptionsAsync(new(seed.Student.Id, false, false)));
        Assert.False(option.RequiresCredits);
        Assert.Equal(0, option.AvailableCreditQuantity);
    }

    [Theory]
    [InlineData("zero-cost")]
    [InlineData("large-cost")]
    [InlineData("missing-credit")]
    [InlineData("inactive-credit")]
    [InlineData("missing-policy")]
    [InlineData("inactive-policy")]
    public async Task SavePaidType_RejectsInvalidCreditConfiguration(string invalid)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var input = PaidInput(seed.Type);
        input.Id = 0;
        if (invalid == "zero-cost") input.CreditCost = 0;
        if (invalid == "large-cost") input.CreditCost = 1001;
        if (invalid == "missing-credit") input.RequiredCreditTypeId = int.MaxValue;
        if (invalid == "inactive-credit") seed.Type.RequiredCreditType!.IsActive = false;
        if (invalid == "missing-policy") input.CreditConsumptionPolicyId = null;
        if (invalid == "inactive-policy") seed.Type.CreditConsumptionPolicy!.IsActive = false;
        await db.SaveChangesAsync();

        var result = await CreateService(db).SaveAppointmentTypeAsync(new(seed.Teacher.Id, false, true), input);

        Assert.False(result.Success);
        Assert.Equal(1, await db.AppointmentTypes.CountAsync());
    }

    [Fact]
    public async Task TeacherCannotAssignOthersOrChangeAnotherOwnersType()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var other = await AddTeacherAsync(db);
        var service = CreateService(db);
        var created = await service.SaveAppointmentTypeAsync(new(other.Id, false, true), new()
        {
            Name = "Teacher-owned",
            TeacherUserIds = [seed.Teacher.Id]
        });
        Assert.True(created.Success, created.ErrorMessage);
        var saved = await db.AppointmentTypes.Include(item => item.Teachers).SingleAsync(item => item.CreatedByUserId == other.Id);
        Assert.Equal(other.Id, Assert.Single(saved.Teachers).TeacherUserId);
        var forbidden = PaidInput(seed.Type);
        forbidden.Name = "Unauthorized change";
        Assert.False((await service.SaveAppointmentTypeAsync(new(other.Id, false, true), forbidden)).Success);
        Assert.False((await service.DeactivateAppointmentTypeAsync(new(other.Id, false, true), seed.Type.Id)).Success);
        Assert.Equal("Private coaching", seed.Type.Name);
        Assert.True(seed.Type.IsActive);
        Assert.Equal(saved.Id, Assert.Single((await service.GetManagementDataAsync(new(other.Id, false, true))).AppointmentTypes).Id);
    }

    [Fact]
    public async Task AdminCanChangeAssignments_TeacherEditsPreserveThoseAssignments()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var other = await AddTeacherAsync(db);
        var service = CreateService(db);
        var input = PaidInput(seed.Type);
        input.TeacherUserIds = [seed.Teacher.Id, other.Id];
        Assert.True((await service.SaveAppointmentTypeAsync(new(other.Id, true, false), input)).Success);
        input.Name = "Updated by owner";
        input.TeacherUserIds = [seed.Student.Id];
        Assert.True((await service.SaveAppointmentTypeAsync(new(seed.Teacher.Id, false, true), input)).Success);
        Assert.Equal(2, await db.AppointmentTypeTeachers.CountAsync(item => item.AppointmentTypeId == seed.Type.Id));
        input.TeacherUserIds = [other.Id];
        Assert.True((await service.SaveAppointmentTypeAsync(new(other.Id, true, false), input)).Success);
        Assert.Equal(other.Id, (await db.AppointmentTypeTeachers.SingleAsync()).TeacherUserId);
    }

    [Fact]
    public async Task StudentsCannotManageTypesAndDeletedTeachersCannotBeAssigned()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var service = CreateService(db);
        Assert.Empty((await service.GetManagementDataAsync(new(seed.Student.Id, false, false))).AppointmentTypes);
        Assert.False((await service.SaveAppointmentTypeAsync(new(seed.Student.Id, false, false), PaidInput(seed.Type))).Success);
        Assert.False((await service.DeactivateAppointmentTypeAsync(new(seed.Student.Id, false, false), seed.Type.Id)).Success);
        seed.Teacher.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.False((await service.SaveAppointmentTypeAsync(new(seed.Student.Id, true, false), PaidInput(seed.Type))).Success);
    }

    [Fact]
    public async Task BookFreeType_NoBalanceOrCreditTransactionsNeededIncludingCancellation()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        seed.Type.RequiredCreditTypeId = null;
        seed.Type.RequiredCreditType = null;
        seed.Type.CreditConsumptionPolicyId = null;
        seed.Type.CreditConsumptionPolicy = null;
        seed.Type.CreditCost = 0;
        db.UserCreditLots.RemoveRange(db.UserCreditLots);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        db.TeacherAvailabilityWindows.Add(new() { TeacherUserId = seed.Teacher.Id, DayOfWeek = date.DayOfWeek, LocalStartTime = new(9, 0), LocalEndTime = new(12, 0) });
        await db.SaveChangesAsync();
        var actor = new ScheduleActor(seed.Student.Id, false, false);
        var booked = await CreateService(db).BookAppointmentAsync(actor, seed.Type.Id, seed.Teacher.Id, AmsterdamUtc(date, new(9, 0)));
        Assert.True(booked.Success, booked.ErrorMessage);
        var meeting = await db.ScheduledEvents.Include(item => item.Bookings).SingleAsync();
        Assert.Equal(CourseClassBookingAccess.Free, meeting.BookingAccess);
        Assert.Null(meeting.RequiredCreditTypeId);
        Assert.Null(meeting.CreditConsumptionPolicyId);
        Assert.Equal(0, meeting.CreditCost);
        Assert.Single(meeting.Bookings);
        var integration = new FakeSchedulingIntegrationService();
        var cancelled = await new SchedulingService(db, integration, NullLogger<SchedulingService>.Instance).CancelBookingAsync(actor, meeting.Id);
        Assert.True(cancelled.Success, cancelled.ErrorMessage);
        Assert.Empty(await db.EventBookingCreditAllocations.ToListAsync());
        Assert.Empty(await db.UserCreditTransactions.ToListAsync());
    }

    [Fact]
    public async Task EditingPaidTypeToFreeAndDeactivating_PreservesExistingBookingSnapshots()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        db.TeacherAvailabilityWindows.Add(new() { TeacherUserId = seed.Teacher.Id, DayOfWeek = date.DayOfWeek, LocalStartTime = new(9, 0), LocalEndTime = new(12, 0) });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        Assert.True((await service.BookAppointmentAsync(new(seed.Student.Id, false, false), seed.Type.Id, seed.Teacher.Id, AmsterdamUtc(date, new(9, 0)))).Success);
        var creditId = seed.Type.RequiredCreditTypeId;
        var policyId = seed.Type.CreditConsumptionPolicyId;
        var changed = await service.SaveAppointmentTypeAsync(new(seed.Teacher.Id, false, true), new() { Id = seed.Type.Id, Name = "Now free", DurationMinutes = 20 });
        Assert.True(changed.Success, changed.ErrorMessage);
        Assert.True((await service.DeactivateAppointmentTypeAsync(new(seed.Teacher.Id, false, true), seed.Type.Id)).Success);
        Assert.Empty(await service.GetStudentOptionsAsync(new(seed.Student.Id, false, false)));
        var meeting = await db.ScheduledEvents.Include(item => item.Bookings).SingleAsync();
        Assert.Equal("Private coaching", meeting.Title);
        Assert.Equal(TimeSpan.FromHours(1), meeting.EndAtUtc - meeting.StartAtUtc);
        Assert.Equal(creditId, meeting.RequiredCreditTypeId);
        Assert.Equal(policyId, meeting.CreditConsumptionPolicyId);
        Assert.Equal(2, meeting.CreditCost);
        Assert.Single(meeting.Bookings);
        Assert.Equal(1, (await db.UserCreditLots.SingleAsync()).RemainingQuantity);
    }

    [Fact]
    public async Task MissingTypeReturnsValidationFailure()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var seed = await SeedAsync(db);
        Assert.False((await CreateService(db).SaveAppointmentTypeAsync(new(seed.Teacher.Id, false, true), new() { Id = int.MaxValue, Name = "Missing" })).Success);
    }

    private static AppointmentTypeInput PaidInput(AppointmentType type) => new()
    {
        Id = type.Id,
        Name = type.Name,
        DurationMinutes = type.DurationMinutes,
        RequiredCreditTypeId = type.RequiredCreditTypeId,
        CreditCost = type.CreditCost,
        CreditConsumptionPolicyId = type.CreditConsumptionPolicyId,
        TeacherUserIds = type.Teachers.Select(item => item.TeacherUserId).ToArray()
    };

    private static async Task<ApplicationUser> AddTeacherAsync(iD_Develops.Data.ApplicationDbContext db)
    {
        var other = User("other-teacher", "other-teacher@example.com", "Other", "Teacher");
        db.Users.Add(other);
        db.UserRoles.Add(new() { UserId = other.Id, RoleId = "appointment-teacher-role" });
        await db.SaveChangesAsync();
        return other;
    }

    private static AppointmentService CreateService(iD_Develops.Data.ApplicationDbContext db)
    {
        var integration = new FakeSchedulingIntegrationService();
        var scheduling = new SchedulingService(db, integration, NullLogger<SchedulingService>.Instance);
        return new AppointmentService(db, scheduling, integration, NullLogger<AppointmentService>.Instance);
    }

    private static async Task<Seed> SeedAsync(iD_Develops.Data.ApplicationDbContext db)
    {
        var teacher = User("appointment-teacher", "appointment-teacher@example.com", "Taylor", "Teacher");
        var student = User("appointment-student", "appointment-student@example.com", "Sam", "Student");
        var teacherRole = new IdentityRole("Teacher") { Id = "appointment-teacher-role", NormalizedName = "TEACHER" };
        var studentRole = new IdentityRole("Student") { Id = "appointment-student-role", NormalizedName = "STUDENT" };
        var credit = new CreditType { Name = "Private lesson", NormalizedName = "PRIVATE LESSON", SingularLabel = "lesson credit", PluralLabel = "lesson credits" };
        var policy = new CreditConsumptionPolicy { Name = "Private lesson policy", NormalizedName = "PRIVATE LESSON POLICY" };
        db.Users.AddRange(teacher, student);
        db.Roles.AddRange(teacherRole, studentRole);
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = teacher.Id, RoleId = teacherRole.Id },
            new IdentityUserRole<string> { UserId = student.Id, RoleId = studentRole.Id });
        db.CreditTypes.Add(credit);
        db.CreditConsumptionPolicies.Add(policy);
        await db.SaveChangesAsync();
        var type = new AppointmentType
        {
            Name = "Private coaching",
            DurationMinutes = 60,
            RequiredCreditTypeId = credit.Id,
            CreditCost = 2,
            CreditConsumptionPolicyId = policy.Id,
            CreatedByUserId = teacher.Id,
            Teachers = [new AppointmentTypeTeacher { TeacherUserId = teacher.Id }]
        };
        db.AppointmentTypes.Add(type);
        db.UserCreditLots.Add(new UserCreditLot
        {
            UserId = student.Id,
            CreditTypeId = credit.Id,
            GrantedQuantity = 3,
            RemainingQuantity = 3,
            Scope = CreditGrantScope.Global
        });
        await db.SaveChangesAsync();
        return new(teacher, student, type);
    }

    private static DateTime AmsterdamUtc(DateOnly date, TimeOnly time)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), zone);
    }

    private static ApplicationUser User(string id, string email, string first, string last) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FirstName = first,
        LastName = last,
        EmailConfirmed = true
    };

    private sealed record Seed(ApplicationUser Teacher, ApplicationUser Student, AppointmentType Type);
}
