using iD_Develops.Models;
using iD_Develops.Enums;
using iD_Develops.Pages.Portal.Examination.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Tests.Services;

public sealed class ExamEditMutationServiceTests
{
    [Fact]
    public async Task SaveQuestionAsync_ReusesPersistedDraftWhenCreateRequestIsRetried()
    {
        using var factory = new SqliteTestDbFactory();
        await using var dbContext = factory.CreateDbContext();

        var exam = new Exam
        {
            Name = "Retry test",
            CreatedByUserId = "teacher-owner",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0,
            MaxAttempts = 1
        };

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync();

        var persistedDraft = new OpenQuestion
        {
            ExamId = exam.Id,
            QuestionNumber = 1,
            Text = "First request"
        };

        dbContext.Questions.Add(persistedDraft);
        await dbContext.SaveChangesAsync();

        var questionService = new QuestionService(dbContext);
        var service = new ExamEditMutationService(
            new StubExamService(exam, canTeacherManageExam: true),
            questionService,
            null!,
            null!);

        var result = await service.SaveQuestionAsync(
            new SaveQuestionCommand
            {
                ExamId = exam.Id,
                QuestionId = 0,
                QuestionNumber = 1,
                Kind = "Open",
                Text = "Retried request"
            },
            "admin-1",
            isAdmin: true);

        Assert.True(result.Success);
        Assert.Equal(persistedDraft.Id, result.QuestionId);
        Assert.Single(await dbContext.Questions.Where(q => q.ExamId == exam.Id && !q.IsDeleted).ToListAsync());
        Assert.Equal("Retried request", persistedDraft.Text);
    }

    [Fact]
    public async Task SaveSettingsAsync_ReturnsForbiddenForTeacherWhoDoesNotOwnExam()
    {
        var exam = new Exam
        {
            Id = 42,
            Name = "Original name",
            CreatedByUserId = "teacher-owner",
            PublicSlug = "original-slug",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0
        };

        var examService = new StubExamService(exam, canTeacherManageExam: false);
        var service = new ExamEditMutationService(examService, null!, null!, null!);

        var result = await service.SaveSettingsAsync(
            42,
            new CreateExamInputModel { Name = "Updated name", PublicSlug = "new-slug" },
            "teacher-other",
            isAdmin: false);

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Original name", exam.Name);
        Assert.Equal("original-slug", exam.PublicSlug);
        Assert.False(examService.SaveChangesCalled);
    }

    [Fact]
    public async Task SaveSettingsAsync_AllowsAdminToUpdatePublicSlug()
    {
        var exam = new Exam
        {
            Id = 42,
            Name = "Original name",
            CreatedByUserId = "teacher-owner",
            PublicSlug = "original-slug",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0
        };

        var examService = new StubExamService(exam, canTeacherManageExam: false);
        var service = new ExamEditMutationService(examService, null!, null!, null!);

        var result = await service.SaveSettingsAsync(
            42,
            new CreateExamInputModel
            {
                Name = "Updated name",
                PublicSlug = "New-Slug",
                IntroductionPrimaryLanguage = "Updated intro"
            },
            "admin-1",
            isAdmin: true);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("Updated name", exam.Name);
        Assert.Equal("new-slug", exam.PublicSlug);
        Assert.Equal("Updated intro", exam.IntroductionPrimaryLanguage);
        Assert.True(examService.SaveChangesCalled);
    }

    [Fact]
    public async Task SaveSettingsAsync_AllowsTeacherToUpdateExamButNotPublicSlug()
    {
        var exam = new Exam
        {
            Id = 42,
            Name = "Original name",
            CreatedByUserId = "teacher-owner",
            PublicSlug = "original-slug",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0
        };

        var examService = new StubExamService(exam, canTeacherManageExam: true);
        var service = new ExamEditMutationService(examService, null!, null!, null!);

        var result = await service.SaveSettingsAsync(
            42,
            new CreateExamInputModel
            {
                Name = "Teacher updated name",
                PublicSlug = "teacher-cannot-set-this",
                IntroductionPrimaryLanguage = "Teacher intro"
            },
            "teacher-owner",
            isAdmin: false);

        Assert.True(result.Success);
        Assert.Equal("Teacher updated name", exam.Name);
        Assert.Equal("original-slug", exam.PublicSlug);
        Assert.Equal("Teacher intro", exam.IntroductionPrimaryLanguage);
        Assert.True(examService.SaveChangesCalled);
    }

    [Fact]
    public async Task SaveSettingsAsync_NormalizesMaxAttemptsBelowOneToUnlimited()
    {
        var exam = new Exam
        {
            Id = 42,
            Name = "Original name",
            CreatedByUserId = "teacher-owner",
            PublicSlug = "original-slug",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0,
            MaxAttempts = 3
        };

        var examService = new StubExamService(exam, canTeacherManageExam: true);
        var service = new ExamEditMutationService(examService, null!, null!, null!);

        var result = await service.SaveSettingsAsync(
            42,
            new CreateExamInputModel
            {
                Name = "Updated name",
                IntroductionPrimaryLanguage = "Updated intro",
                MaxAttempts = 0
            },
            "teacher-owner",
            isAdmin: false);

        Assert.True(result.Success);
        Assert.Equal(-1, exam.MaxAttempts);
        Assert.True(examService.SaveChangesCalled);
    }

    [Fact]
    public async Task SaveSettingsAsync_ConvertsUpperScoreBandsIntoStoredMinimumThresholds()
    {
        var exam = new Exam
        {
            Id = 42,
            Name = "Original name",
            CreatedByUserId = "teacher-owner",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0,
            GradeBands =
            [
                new ExamGradeBand { MinimumScore = 0, LabelPrimary = "Old" }
            ]
        };

        var examService = new StubExamService(exam, canTeacherManageExam: true);
        var service = new ExamEditMutationService(examService, null!, null!, null!);

        var input = new CreateExamInputModel
        {
            Name = "Updated name",
            IntroductionPrimaryLanguage = "Updated intro",
            ResultGradeDisplayMode = ResultGradeDisplayMode.GradeBands,
            ResultGradeBands =
            [
                new ResultGradeBandInputModel { MaximumScore = 2.5, LabelPrimary = "Needs work" },
                new ResultGradeBandInputModel { MaximumScore = 5.0, LabelPrimary = "Pass" }
            ]
        };

        var result = await service.SaveSettingsAsync(42, input, "teacher-owner", isAdmin: false);

        Assert.True(result.Success);
        Assert.Equal(ResultGradeDisplayMode.GradeBands, exam.ResultGradeDisplayMode);

        var savedBands = exam.GradeBands.OrderByDescending(b => b.MinimumScore).ToList();
        Assert.Collection(
            savedBands,
            band =>
            {
                Assert.Equal(2.6d, band.MinimumScore, precision: 1);
                Assert.Equal("Pass", band.LabelPrimary);
            },
            band =>
            {
                Assert.Equal(0d, band.MinimumScore, precision: 1);
                Assert.Equal("Needs work", band.LabelPrimary);
            });
    }

    [Fact]
    public async Task SaveSettingsAsync_IgnoresIncompleteGradeRowsWhenPersistingBands()
    {
        var exam = new Exam
        {
            Id = 42,
            Name = "Original name",
            CreatedByUserId = "teacher-owner",
            IntroductionPrimaryLanguage = "Intro",
            DifficultyValue = 0
        };

        var examService = new StubExamService(exam, canTeacherManageExam: true);
        var service = new ExamEditMutationService(examService, null!, null!, null!);

        var input = new CreateExamInputModel
        {
            Name = "Updated name",
            IntroductionPrimaryLanguage = "Updated intro",
            ResultGradeDisplayMode = ResultGradeDisplayMode.GradeBands,
            ResultGradeBands =
            [
                new ResultGradeBandInputModel { MaximumScore = 3.0, LabelPrimary = "Pass" },
                new ResultGradeBandInputModel { MaximumScore = 5.0, LabelPrimary = null },
                new ResultGradeBandInputModel { MaximumScore = null, LabelPrimary = "Unused" }
            ]
        };

        var result = await service.SaveSettingsAsync(42, input, "teacher-owner", isAdmin: false);

        Assert.True(result.Success);
        var savedBand = Assert.Single(exam.GradeBands);
        Assert.Equal(0d, savedBand.MinimumScore, precision: 1);
        Assert.Equal("Pass", savedBand.LabelPrimary);
    }

    private sealed class StubExamService : IExamService
    {
        private readonly Exam? _exam;
        private readonly bool _canTeacherManageExam;

        public StubExamService(Exam? exam, bool canTeacherManageExam)
        {
            _exam = exam;
            _canTeacherManageExam = canTeacherManageExam;
        }

        public bool SaveChangesCalled { get; private set; }

        public Task<bool> CanTeacherManageExamAsync(int examId, string teacherUserId)
            => Task.FromResult(_canTeacherManageExam);

        public Task<OperationResult> CreateExamAsync(Exam exam)
            => throw new NotSupportedException();

        public Task<OperationResult> DeleteExamByIdAsync(int examId)
            => throw new NotSupportedException();

        public Task<OperationResult> ArchiveExamAsync(int examId)
            => throw new NotSupportedException();

        public Task<List<Exam>> GetAllExamsAsync()
            => throw new NotSupportedException();

        public Task<(string ExamTitle, string CourseName, int DurationSeconds)?> GetExamHeaderAsync(int examId)
            => throw new NotSupportedException();

        public Task<Exam?> GetExamForSettingsUpdateAsync(int examId)
            => Task.FromResult(_exam?.Id == examId ? _exam : null);

        public Task<(bool Exists, int MaxAttempts, int? TimeLimit)> GetExamStartSettingsAsync(int examId)
            => throw new NotSupportedException();

        public Task<List<Exam>> GetExamsByTeacherAsync(string teacherUserId)
            => throw new NotSupportedException();

        public Task<Exam?> GetPublicExamBySlugAsync(string publicSlug)
            => throw new NotSupportedException();

        public Task<List<Exam>> GetPublishedExamsAsync()
            => throw new NotSupportedException();

        public Task<List<Exam>> GetPublishedExamsForUserAsync(string userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveChangesAsync()
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }
    }
}
