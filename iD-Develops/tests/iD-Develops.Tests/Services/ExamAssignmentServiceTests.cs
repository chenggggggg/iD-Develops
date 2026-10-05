using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;

namespace iD_Develops.Tests.Services;

public sealed class ExamAssignmentServiceTests
{
    [Fact]
    public async Task AssignAsync_OwnerCanAssignWithAvailabilityWindow()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var owner = new ApplicationUser { Id = "assignment-owner", UserName = "owner@example.com" };
        var student = new ApplicationUser { Id = "assignment-student", UserName = "student@example.com" };
        var exam = CreateExam(owner.Id);
        db.AddRange(owner, student, exam);
        await db.SaveChangesAsync();
        var unlockAtUtc = DateTime.UtcNow.AddDays(1);
        var dueAtUtc = unlockAtUtc.AddDays(3);
        var service = new ExamAssignmentService(db);

        var result = await service.AssignAsync(
            exam.Id, student.Id, unlockAtUtc, dueAtUtc, owner.Id, canManageAll: false);

        Assert.True(result.Success);
        var assignment = Assert.Single(db.UserExams);
        Assert.Equal(owner.Id, assignment.AssignedByUserId);
        Assert.Equal(unlockAtUtc, assignment.UnlockAtUtc);
        Assert.Equal(dueAtUtc, assignment.DueAtUtc);
    }

    [Fact]
    public async Task AssignAsync_RejectsNonOwnerAndInvalidDateWindow()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var owner = new ApplicationUser { Id = "exam-owner", UserName = "owner@example.com" };
        var otherTeacher = new ApplicationUser { Id = "other-teacher", UserName = "teacher@example.com" };
        var student = new ApplicationUser { Id = "student", UserName = "student@example.com" };
        var exam = CreateExam(owner.Id);
        db.AddRange(owner, otherTeacher, student, exam);
        await db.SaveChangesAsync();
        var service = new ExamAssignmentService(db);

        var unauthorized = await service.AssignAsync(
            exam.Id, student.Id, null, null, otherTeacher.Id, canManageAll: false);
        var unlockAtUtc = DateTime.UtcNow.AddDays(2);
        var invalidWindow = await service.AssignAsync(
            exam.Id, student.Id, unlockAtUtc, unlockAtUtc.AddHours(-1), owner.Id, canManageAll: false);

        Assert.False(unauthorized.Success);
        Assert.False(invalidWindow.Success);
        Assert.Empty(db.UserExams);
    }

    [Fact]
    public async Task GrantAttemptsAsync_CreatesAuditedLedgerEntry()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var admin = new ApplicationUser { Id = "grant-admin", UserName = "admin@example.com" };
        var student = new ApplicationUser { Id = "grant-student", UserName = "student@example.com" };
        var exam = CreateExam("somebody-else");
        db.AddRange(admin, student, exam);
        await db.SaveChangesAsync();
        var service = new ExamAssignmentService(db);

        var result = await service.GrantAttemptsAsync(
            exam.Id, student.Id, 2, "Extra practice", admin.Id, canManageAll: true);

        Assert.True(result.Success);
        var grant = Assert.Single(db.ExamAttemptGrants);
        Assert.Equal(2, grant.AdditionalAttempts);
        Assert.Equal(admin.Id, grant.GrantedByUserId);
        Assert.Equal("Extra practice", grant.Reason);
    }

    [Fact]
    public async Task GetPageAsync_IncludesEveryCourseSectionPlacement()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var owner = new ApplicationUser { Id = "placement-owner", UserName = "owner@example.com" };
        var exam = CreateExam(owner.Id);
        var course = new Course { Name = "Placed course", CreatedByUserId = owner.Id };
        var firstSection = new CourseSection { Course = course, Title = "Introduction", OrderNumber = 0 };
        var secondSection = new CourseSection { Course = course, Title = "Review", OrderNumber = 1 };
        firstSection.Exams.Add(new CourseSectionExam { Exam = exam, OrderNumber = 2, IsRequiredForCompletion = true });
        secondSection.Exams.Add(new CourseSectionExam { Exam = exam, OrderNumber = 0, UnlockAfterValue = 2, UnlockAfterUnit = iD_Develops.Enums.CourseUnlockUnit.Weeks });
        course.Sections.Add(firstSection);
        course.Sections.Add(secondSection);
        db.AddRange(owner, course);
        await db.SaveChangesAsync();

        var data = await new ExamAssignmentService(db).GetPageAsync(exam.Id, owner.Id, canManageAll: false);

        Assert.NotNull(data);
        Assert.Equal(2, data!.CoursePlacements.Count);
        Assert.Equal(["Introduction", "Review"], data.CoursePlacements.Select(item => item.SectionTitle).ToArray());
        Assert.Equal(course.Id, data.CoursePlacements[0].CourseId);
        Assert.Equal(2, data.CoursePlacements[1].UnlockAfterValue);
    }

    private static Exam CreateExam(string ownerId)
        => new()
        {
            Name = "Assignment exam",
            CreatedByUserId = ownerId,
            DifficultyValue = 1,
            IntroductionPrimaryLanguage = string.Empty,
            PublishStatus = ExamPublishStatus.Published
        };
}
