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
