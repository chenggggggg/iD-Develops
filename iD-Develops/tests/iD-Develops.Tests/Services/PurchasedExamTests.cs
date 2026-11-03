using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Record = iD_Develops.Models.Record;
using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Enums;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace iD_Develops.Tests.Services;

public sealed class PurchasedExamTests
{
    [Fact]
    public async Task PaidAllowance_IsIndependentOfExamLimitAndUnrelatedHistoricalAttempts()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (user, exam, grant) = await SeedAsync(db, 2);
        exam.MaxAttempts = 99;
        db.Records.Add(new Record { UserId = user.Id, ExamId = exam.Id, ExamStatus = ExamStatus.Completed, StartDateTime = DateTime.UtcNow.AddDays(-2) });
        await db.SaveChangesAsync();
        var attempts = Attempts(db);
        var state = await attempts.GetStateAsync(user.Id, exam.Id);
        Assert.Equal(2, state.MaxAttempts);
        Assert.Equal(2, state.AttemptsLeft);
        Assert.Equal(0, state.AttemptsUsed);
        var started = await attempts.StartAsync(user.Id, exam.Id);
        Assert.True(started.Success, started.ErrorMessage);
        Assert.Equal(grant.Id, (await db.Records.SingleAsync(row => row.Id == started.RecordId)).PurchasedExamEntitlementId);
        Assert.Equal(1, grant.UsedAttempts);
    }

    [Fact]
    public async Task StartedAttempts_KeepPurchasedVersionAndDeadlineAfterExamEditsAndArchival()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (user, exam, grant) = await SeedAsync(db, 1);
        exam.Name = "Changed name";
        exam.TimeLimitSeconds = 15;
        exam.PublishStatus = ExamPublishStatus.Archived;
        await db.SaveChangesAsync();
        var state = await Attempts(db).GetStateAsync(user.Id, exam.Id);
        Assert.True(state.CanStart);
        Assert.Equal(exam.Id, Assert.Single(await new ExamService(db).GetPublishedExamsForUserAsync(user.Id)).Id);
        Assert.Empty(await new ExamService(db).GetPublishedExamsForUserAsync("unassigned-user"));
        Assert.Equal(grant.ExamVersionId, state.ExamVersionId);
        var start = await Attempts(db).StartAsync(user.Id, exam.Id);
        Assert.True(start.Success, start.ErrorMessage);
        var record = await db.Records.SingleAsync();
        Assert.Equal(grant.ExamVersionId, record.ExamVersionId);
        Assert.Equal(120, (record.EndDateTime!.Value - record.StartDateTime).TotalSeconds);
        var descriptor = await new ExamVersionService(db).GetPublishedDescriptorAsync(exam.Id, record.ExamVersionId);
        Assert.Equal("Purchased exam", descriptor!.ExamTitle);
    }

    [Fact]
    public async Task RepeatedStartAndContinue_DoNotConsumeAnotherAttempt_RestartDoes()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (user, exam, grant) = await SeedAsync(db, 2);
        var service = Attempts(db);
        var first = await service.StartAsync(user.Id, exam.Id);
        Assert.Equal(first.RecordId, (await service.StartAsync(user.Id, exam.Id)).RecordId);
        Assert.Equal(first.RecordId, (await service.ContinueAsync(user.Id, exam.Id)).RecordId);
        Assert.Equal(1, grant.UsedAttempts);
        var second = await service.RestartAsync(user.Id, exam.Id);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.NotEqual(first.RecordId, second.RecordId);
        Assert.Equal(2, grant.UsedAttempts);
        Assert.False((await service.GetStateAsync(user.Id, exam.Id)).CanRestart);
        Assert.False((await service.RestartAsync(user.Id, exam.Id)).Success);
        var record = await db.Records.SingleAsync(row => row.Id == second.RecordId);
        record.ExamStatus = ExamStatus.Completed;
        record.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.False((await service.StartAsync(user.Id, exam.Id)).Success);
        Assert.Equal(2, grant.UsedAttempts);
    }

    [Fact]
    public async Task RepeatPurchases_AddAllowancesAndUseTheirOwnSnapshotsInOrder()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (user, exam, first) = await SeedAsync(db, 1);
        exam.TimeLimitSeconds = 40;
        await db.SaveChangesAsync();
        var version = await new ExamVersionService(db).GetOrCreateAttemptVersionAsync(exam.Id);
        var second = Grant(user.Id, exam.Id, version!.ExamVersionId!.Value, 3);
        db.PurchasedExamEntitlements.Add(second);
        await db.SaveChangesAsync();
        var service = Attempts(db);
        Assert.Equal(4, (await service.GetStateAsync(user.Id, exam.Id)).AttemptsLeft);
        var started = await service.StartAsync(user.Id, exam.Id);
        var record = await db.Records.SingleAsync(row => row.Id == started.RecordId);
        Assert.Equal(first.ExamVersionId, record.ExamVersionId);
        record.ExamStatus = ExamStatus.Completed;
        await db.SaveChangesAsync();
        var next = await service.StartAsync(user.Id, exam.Id);
        var nextRecord = await db.Records.SingleAsync(row => row.Id == next.RecordId);
        Assert.Equal(second.ExamVersionId, nextRecord.ExamVersionId);
        Assert.Equal(40, (nextRecord.EndDateTime!.Value - nextRecord.StartDateTime).TotalSeconds);
        Assert.Equal(1, first.UsedAttempts);
        Assert.Equal(1, second.UsedAttempts);
    }

    [Fact]
    public async Task PaidAndOrdinaryAssignments_DoNotCountPurchasedRecordsAgainstOrdinaryAllowance()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (user, exam, grant) = await SeedAsync(db, 1);
        exam.MaxAttempts = 3;
        db.UserExams.Add(new UserExam { UserId = user.Id, ExamId = exam.Id });
        await db.SaveChangesAsync();
        var service = Attempts(db);
        Assert.Equal(4, (await service.GetStateAsync(user.Id, exam.Id)).MaxAttempts);
        var first = await service.StartAsync(user.Id, exam.Id);
        var paidRecord = await db.Records.SingleAsync(row => row.Id == first.RecordId);
        Assert.Equal(grant.Id, (await service.GetStateAsync(user.Id, exam.Id)).CurrentPurchasedEntitlementId);
        paidRecord.ExamStatus = ExamStatus.Completed;
        await db.SaveChangesAsync();
        Assert.Equal(3, (await service.GetStateAsync(user.Id, exam.Id)).AttemptsLeft);
        var ordinary = await service.StartAsync(user.Id, exam.Id);
        Assert.True(ordinary.Success, ordinary.ErrorMessage);
        Assert.Null((await db.Records.SingleAsync(row => row.Id == ordinary.RecordId)).PurchasedExamEntitlementId);
        Assert.Equal(1, grant.UsedAttempts);
    }

    [Fact]
    public async Task AtomicStart_RejectsExhaustedWrongOwnerAndWrongVersionGrants()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (user, exam, grant) = await SeedAsync(db, 1);
        var records = Records(db);
        var now = DateTime.UtcNow;
        Assert.Equal(Guid.Empty, await records.StartPurchasedAttemptAtomicAsync("other-user", exam.Id, grant.Id, grant.ExamVersionId, now, now.AddMinutes(2)));
        Assert.Equal(Guid.Empty, await records.StartPurchasedAttemptAtomicAsync(user.Id, exam.Id, grant.Id, grant.ExamVersionId + 999, now, now.AddMinutes(2)));
        grant.UsedAttempts = grant.TotalAttempts;
        await db.SaveChangesAsync();
        Assert.Equal(Guid.Empty, await records.StartPurchasedAttemptAtomicAsync(user.Id, exam.Id, grant.Id, grant.ExamVersionId, now, now.AddMinutes(2)));
        Assert.Empty(await db.Records.ToListAsync());
    }

    [Fact]
    public async Task UnlimitedPurchasedAllowance_RemainsUnlimitedAndStillTracksUsage()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (user, exam, grant) = await SeedAsync(db, -1);
        var service = Attempts(db);
        var state = await service.GetStateAsync(user.Id, exam.Id);
        Assert.True(state.HasUnlimitedAttempts);
        Assert.Equal(int.MaxValue, state.AttemptsLeft);
        Assert.True((await service.StartAsync(user.Id, exam.Id)).Success);
        Assert.Equal(1, grant.UsedAttempts);
    }

    [Fact]
    public async Task ProductExamSettings_DefaultToExamAllowanceAndCanBeOverriddenOrRemoved()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (_, exam, _) = await SeedAsync(db, 1);
        exam.MaxAttempts = 4;
        await db.SaveChangesAsync();
        var service = new CatalogProductService(db);
        var product = await service.CreateProductAsync(new CatalogProduct
        {
            Name = "Exam product",
            Slug = "exam-product",
            ProductType = CatalogProductType.Standard,
            WorkflowType = CatalogWorkflowType.FormThenStripeCheckout,
            GrantedExamId = exam.Id,
            EnableQuantity = true,
            MaxQuantity = 5
        });
        Assert.Equal(4, product.ExamAttemptsOnPurchase);
        Assert.True(product.RequiresAuthenticatedAccount);
        Assert.False(product.EnableQuantity);
        Assert.Equal(1, product.MaxQuantity);
        product.ExamAttemptsOnPurchase = 2;
        await service.UpdateProductAsync(product);
        Assert.Equal(2, (await service.GetProductByIdAsync(product.Id))!.ExamAttemptsOnPurchase);
        exam.PublishStatus = ExamPublishStatus.Archived;
        await db.SaveChangesAsync();
        await service.UpdateProductAsync(product);
        Assert.Equal(exam.Id, product.GrantedExamId);
        product.GrantedExamId = null;
        await service.UpdateProductAsync(product);
        Assert.Null(product.ExamAttemptsOnPurchase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task ProductExamSettings_RejectInvalidAllowances(int allowance)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var (_, exam, _) = await SeedAsync(db, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CatalogProductService(db).CreateProductAsync(new CatalogProduct
        {
            Name = "Invalid",
            ProductType = CatalogProductType.Standard,
            WorkflowType = CatalogWorkflowType.FormThenStripeCheckout,
            GrantedExamId = exam.Id,
            ExamAttemptsOnPurchase = allowance
        }));
    }


    [Fact]
    public async Task StartWithSerializationRetry_ConsumesExactlyOnePurchasedAttempt()
    {
        using var factory = new SqliteTestDbFactory();
        var interceptor = new FailOnceCommit();
        await using var db = factory.CreateDbContext(builder =>
        {
            builder.ReplaceService<IExecutionStrategyFactory, RecordLifecycleTests.RetryStrategyFactory>();
            builder.AddInterceptors(interceptor);
        });
        var (user, exam, grant) = await SeedAsync(db, 1);
        interceptor.FailNext = true;
        var result = await Attempts(db).StartAsync(user.Id, exam.Id);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Single(await db.Records.AsNoTracking().ToListAsync());
        Assert.Equal(1, (await db.PurchasedExamEntitlements.AsNoTracking().SingleAsync(item => item.Id == grant.Id)).UsedAttempts);
    }

    private sealed class FailOnceCommit : DbTransactionInterceptor
    {
        public bool FailNext { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new PostgresException("Simulated retry", "ERROR", "ERROR", "40001");
            }
            return ValueTask.FromResult(result);
        }
    }

    private static ExamAttemptService Attempts(ApplicationDbContext db) => new(new ExamVersionService(db), Records(db), new ExamAccessService(db), db);
    private static RecordService Records(ApplicationDbContext db)
    {
        var options = Options.Create(new RecordLifecycleOptions());
        return new(db, NullLogger<RecordService>.Instance, options, new ExamVersionService(db), new ParticipantAnswerService(db, options), new ExamEvaluationService());
    }
    private static PurchasedExamEntitlement Grant(string userId, int examId, int versionId, int attempts) => new()
    {
        UserId = userId,
        ExamId = examId,
        ExamVersionId = versionId,
        TotalAttempts = attempts,
        StripeSessionId = "cs_" + Guid.NewGuid().ToString("N"),
        CheckoutAttemptId = Guid.NewGuid()
    };
    private static async Task<(ApplicationUser User, Exam Exam, PurchasedExamEntitlement Grant)> SeedAsync(ApplicationDbContext db, int attempts)
    {
        var user = new ApplicationUser { Id = "purchaser", UserName = "purchaser@example.test", Email = "purchaser@example.test", FirstName = "Paid", LastName = "Learner" };
        var exam = new Exam
        {
            Name = "Purchased exam",
            CreatedByUserId = "teacher",
            IntroductionPrimaryLanguage = "Original instructions",
            TimeLimitSeconds = 120,
            MaxAttempts = 1,
            PublishStatus = ExamPublishStatus.Published,
            Questions = [new OpenQuestion { Text = "Original question", QuestionNumber = 1, Score = 1 }]
        };
        db.AddRange(user, exam);
        await db.SaveChangesAsync();
        var version = await new ExamVersionService(db).GetOrCreateAttemptVersionAsync(exam.Id);
        var grant = Grant(user.Id, exam.Id, version!.ExamVersionId!.Value, attempts);
        db.PurchasedExamEntitlements.Add(grant);
        await db.SaveChangesAsync();
        return (user, exam, grant);
    }
}
