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
        var unavailable = CreateExam("Unavailable", restrictedCourse.Id);
        db.Exams.AddRange(standalone, enrolled, direct, unavailable);
        await db.SaveChangesAsync();

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
            ["Standalone", "Enrolled", "Direct"],
            available.OrderBy(exam => exam.Id).Select(exam => exam.Name).ToArray());
        Assert.True(await accessService.CanTakeExamAsync(student.Id, standalone.Id));
        Assert.True(await accessService.CanTakeExamAsync(student.Id, enrolled.Id));
        Assert.True(await accessService.CanTakeExamAsync(student.Id, direct.Id));
        Assert.False(await accessService.CanTakeExamAsync(student.Id, unavailable.Id));
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
