using iD_Develops.Models;
using iD_Develops.Services;

namespace iD_Develops.Tests.Services;

public sealed class ExamAttemptServiceTests
{
    [Fact]
    public async Task GetStateAsync_UsesLatestVersionForAttemptCounting()
    {
        var examVersionService = new StubExamVersionService(
            new PublishedExamDescriptor(
                ExamId: 12,
                ExamVersionId: 7,
                ExamTitle: "Placement test",
                CourseName: "Dutch",
                DurationSeconds: 1800,
                IntroductionPrimary: "Intro",
                IntroductionSecondary: null,
                MaxAttempts: 3,
                TimeLimit: 30,
                PublicSlug: null));

        var recordService = new StubRecordService
        {
            InProgressRecords = [],
            AttemptsUsed = 1
        };

        var service = new ExamAttemptService(examVersionService, recordService, new StubExamAccessService());

        var state = await service.GetStateAsync("student-1", 12);

        Assert.Equal(7, recordService.LastExamVersionIdSeen);
        Assert.Equal(3, state.MaxAttempts);
        Assert.Equal(1, state.AttemptsUsed);
        Assert.Equal(2, state.AttemptsLeft);
        Assert.True(state.CanStart);
        Assert.False(state.CanContinue);
    }

    [Fact]
    public async Task StartAsync_BindsNewRecordToLatestPublishedVersion()
    {
        var examVersionService = new StubExamVersionService(
            new PublishedExamDescriptor(
                ExamId: 12,
                ExamVersionId: 9,
                ExamTitle: "Placement test",
                CourseName: "Dutch",
                DurationSeconds: 1800,
                IntroductionPrimary: "Intro",
                IntroductionSecondary: null,
                MaxAttempts: 3,
                TimeLimit: 30,
                PublicSlug: null));

        var createdRecordId = Guid.NewGuid();
        var recordService = new StubRecordService
        {
            InProgressRecords = [],
            AttemptsUsed = 0,
            StartAttemptResult = createdRecordId
        };

        var service = new ExamAttemptService(examVersionService, recordService, new StubExamAccessService());

        var result = await service.StartAsync("student-1", 12);

        Assert.True(result.Success);
        Assert.Equal(createdRecordId, result.RecordId);
        Assert.Equal(9, recordService.LastStartedExamVersionId);
    }

    [Fact]
    public async Task RestartAsync_CancelsAndRestartsWithinCurrentVersion()
    {
        var inProgressId = Guid.NewGuid();
        var restartedId = Guid.NewGuid();

        var examVersionService = new StubExamVersionService(
            new PublishedExamDescriptor(
                ExamId: 12,
                ExamVersionId: 5,
                ExamTitle: "Placement test",
                CourseName: "Dutch",
                DurationSeconds: 1800,
                IntroductionPrimary: "Intro",
                IntroductionSecondary: null,
                MaxAttempts: 3,
                TimeLimit: 30,
                PublicSlug: null));

        var recordService = new StubRecordService
        {
            InProgressRecords =
            [
                new Models.Record { Id = inProgressId, StartDateTime = DateTime.UtcNow }
            ],
            AttemptsUsed = 1,
            CancelInProgressResult = true,
            StartAttemptResult = restartedId
        };

        var service = new ExamAttemptService(examVersionService, recordService, new StubExamAccessService());

        var result = await service.RestartAsync("student-1", 12);

        Assert.True(result.Success);
        Assert.Equal(restartedId, result.RecordId);
        Assert.Equal(inProgressId, recordService.LastCancelledRecordId);
        Assert.Equal(5, recordService.LastCancelledExamVersionId);
        Assert.Equal(5, recordService.LastStartedExamVersionId);
    }

    [Fact]
    public async Task StartAsync_RejectsUserWithoutExamEntitlement()
    {
        var examVersionService = new StubExamVersionService(
            new PublishedExamDescriptor(
                ExamId: 12,
                ExamVersionId: 5,
                ExamTitle: "Restricted test",
                CourseName: "Dutch",
                DurationSeconds: 1800,
                IntroductionPrimary: "Intro",
                IntroductionSecondary: null,
                MaxAttempts: 3,
                TimeLimit: 30,
                PublicSlug: null));
        var recordService = new StubRecordService();
        var service = new ExamAttemptService(
            examVersionService,
            recordService,
            new StubExamAccessService { CanTake = false });

        var result = await service.StartAsync("student-1", 12);

        Assert.False(result.Success);
        Assert.Null(result.RecordId);
        Assert.Equal("This exam is not assigned to your account.", result.ErrorMessage);
    }

    private sealed class StubExamVersionService : IExamVersionService
    {
        private readonly PublishedExamDescriptor? _descriptor;

        public StubExamVersionService(PublishedExamDescriptor? descriptor)
        {
            _descriptor = descriptor;
        }

        public Task<PublishedExamDescriptor?> GetPublishedDescriptorAsync(int examId, int? examVersionId = null)
            => Task.FromResult(_descriptor);

        public Task<List<QuestionMetadata>> GetQuestionMetadataAsync(int examId, int? examVersionId = null)
            => throw new NotSupportedException();

        public Task<Models.Question?> GetQuestionForTakeAsync(int examId, int questionId, int? examVersionId = null)
            => throw new NotSupportedException();

        public Task<Models.Exam?> GetExamForEvaluationAsync(int examId, int? examVersionId = null)
            => throw new NotSupportedException();

        public Task<Utilities.OperationResult> PublishVersionAsync(Models.Exam exam, IReadOnlyCollection<Models.Question> questions)
            => throw new NotSupportedException();
    }

    private sealed class StubExamAccessService : IExamAccessService
    {
        public bool CanTake { get; init; } = true;

        public Task<bool> CanTakeExamAsync(string userId, int examId, CancellationToken cancellationToken = default)
            => Task.FromResult(CanTake);
    }

    private sealed class StubRecordService : IRecordService
    {
        public List<Models.Record> InProgressRecords { get; set; } = [];
        public int AttemptsUsed { get; set; }
        public Guid StartAttemptResult { get; set; } = Guid.NewGuid();
        public bool CancelInProgressResult { get; set; } = true;
        public int? LastExamVersionIdSeen { get; private set; }
        public int? LastStartedExamVersionId { get; private set; }
        public int? LastCancelledExamVersionId { get; private set; }
        public Guid? LastCancelledRecordId { get; private set; }

        public Task<AttemptAccessState> GetAttemptAccessStateAsync(Guid recordId, int? examId = null, string? userId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Guid> CreateRecordAsync(Models.Record record)
            => throw new NotSupportedException();

        public Task CompleteRecordAsync(Guid id, DateTime submitTime)
            => throw new NotSupportedException();

        public Task<int> MarkExpiredRecordsOverdueAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ExamStatus> GetExamStatusAsync(Guid recordId, int? examId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DateTime?> GetRecordEndTimeAsync(Guid recordId)
            => throw new NotSupportedException();

        public Task<int> CountAttemptsAsync(string userId, int examId, int? examVersionId = null)
        {
            LastExamVersionIdSeen = examVersionId;
            return Task.FromResult(AttemptsUsed);
        }

        public Task<bool> CancelInProgressAsync(Guid recordId, string userId, int examId, int? examVersionId = null)
        {
            LastCancelledRecordId = recordId;
            LastCancelledExamVersionId = examVersionId;
            return Task.FromResult(CancelInProgressResult);
        }

        public Task<List<Models.Record>> GetInProgressRecordsAsync(string userId, int examId, int? examVersionId = null)
        {
            LastExamVersionIdSeen = examVersionId;
            return Task.FromResult(InProgressRecords);
        }

        public Task<int> CancelRecordsAsync(IEnumerable<Guid> recordIds, string userId, int examId, int? examVersionId = null)
            => Task.FromResult(0);

        public Task<Guid> StartAttemptAtomicAsync(string userId, int examId, int? examVersionId, DateTime startTimeUtc, DateTime? endTimeUtc)
        {
            LastStartedExamVersionId = examVersionId;
            return Task.FromResult(StartAttemptResult);
        }

        public Task<IReadOnlyList<UserExamRecordSummary>> GetUserResultRecordsAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserExamRecordSummary>>(Array.Empty<UserExamRecordSummary>());
    }
}
