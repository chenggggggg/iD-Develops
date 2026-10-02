using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class ExamAccessServiceTests
{
    [Fact]
    public async Task PublishedCourseExam_RequiresEnrollmentOrDirectAssignment()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var student = new ApplicationUser
        {
            Id = "exam-access-student",
            UserName = "student@example.com",
            Email = "student@example.com"
        };
        var enrolledCourse = new Course { Name = "Enrolled course" };
        var restrictedCourse = new Course { Name = "Restricted course" };
        db.Users.Add(student);
        db.Courses.AddRange(enrolledCourse, restrictedCourse);
        await db.SaveChangesAsync();

        var standalone = CreateExam("Standalone", null);
        var enrolled = CreateExam("Enrolled", enrolledCourse.Id);
        var direct = CreateExam("Direct", restrictedCourse.Id);
        var placed = CreateExam("Placed", null);
        var unavailable = CreateExam("Unavailable", restrictedCourse.Id);
        db.Exams.AddRange(standalone, enrolled, direct, placed, unavailable);
        await db.SaveChangesAsync();

        var section = new CourseSection
        {
            CourseId = enrolledCourse.Id,
            Title = "Assessment",
            OrderNumber = 1
        };
        section.Exams.Add(new CourseSectionExam { ExamId = placed.Id, OrderNumber = 1 });
        db.CourseSections.Add(section);

        db.UserCourses.Add(new UserCourse
        {
            UserId = student.Id,
            CourseId = enrolledCourse.Id,
            AssignmentSource = CourseAssignmentSource.Purchase
        });
        db.UserExams.Add(new UserExam
        {
            UserId = student.Id,
            ExamId = direct.Id
        });
        await db.SaveChangesAsync();

        var accessService = new ExamAccessService(db);
        var examService = new ExamService(db);
        var available = await examService.GetPublishedExamsForUserAsync(student.Id);

        Assert.Equal(
            ["Enrolled", "Direct", "Placed"],
            available.OrderBy(exam => exam.Id).Select(exam => exam.Name).ToArray());
        Assert.False(await accessService.CanTakeExamAsync(student.Id, standalone.Id));
        Assert.True(await accessService.CanTakeExamAsync(student.Id, enrolled.Id));
        Assert.True(await accessService.CanTakeExamAsync(student.Id, direct.Id));
        Assert.True(await accessService.CanTakeExamAsync(student.Id, placed.Id));
        Assert.False(await accessService.CanTakeExamAsync(student.Id, unavailable.Id));
    }

    [Fact]
    public async Task LockedAssignments_AppearInLibraryButCannotBeTakenUntilUnlocked()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var now = DateTime.UtcNow;
        var student = new ApplicationUser { Id = "locked-student", UserName = "locked@example.com" };
        var course = new Course { Name = "Timed course" };
        var section = new CourseSection { Course = course, Title = "Later", OrderNumber = 1 };
        var courseExam = CreateExam("Course exam", null);
        var directExam = CreateExam("Direct exam", null);
        section.Exams.Add(new CourseSectionExam { Exam = courseExam, OrderNumber = 1 });
        course.Sections.Add(section);
        db.AddRange(student, course, directExam);
        await db.SaveChangesAsync();
        db.UserCourses.Add(new UserCourse { UserId = student.Id, CourseId = course.Id, GrantedAtUtc = now });
        var sectionAccess = new CourseSectionUserAccess
        {
            UserId = student.Id,
            CourseSectionId = section.Id,
            UnlockAtUtc = now.AddDays(3)
        };
        var directAccess = new UserExam
        {
            UserId = student.Id,
            ExamId = directExam.Id,
            UnlockAtUtc = now.AddDays(3),
            DueAtUtc = now.AddDays(6)
        };
        db.AddRange(sectionAccess, directAccess);
        await db.SaveChangesAsync();

        var accessService = new ExamAccessService(db);
        var examService = new ExamService(db);

        Assert.Equal(2, (await examService.GetPublishedExamsForUserAsync(student.Id)).Count);
        Assert.False(await accessService.CanTakeExamAsync(student.Id, courseExam.Id));
        Assert.False(await accessService.CanTakeExamAsync(student.Id, directExam.Id));

        sectionAccess.UnlockAtUtc = now.AddMinutes(-1);
        directAccess.UnlockAtUtc = now.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.True(await accessService.CanTakeExamAsync(student.Id, courseExam.Id));
        Assert.True(await accessService.CanTakeExamAsync(student.Id, directExam.Id));

        directAccess.DueAtUtc = now.AddMinutes(-1);
        await db.SaveChangesAsync();

        var expired = await accessService.GetAccessDecisionAsync(student.Id, directExam.Id);
        Assert.False(expired.CanTake);
        Assert.Equal("The due date for this exam has passed.", expired.BlockReason);
    }

    private static Exam CreateExam(string name, int? courseId)
        => new()
        {
            Name = name,
            CreatedByUserId = "exam-owner",
            DifficultyValue = (int)DifficultyLevel.A1,
            IntroductionPrimaryLanguage = string.Empty,
            PublishStatus = ExamPublishStatus.Published,
            CourseId = courseId
        };
}
