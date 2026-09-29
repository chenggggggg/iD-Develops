using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Pages.Portal.Examination;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace iD_Develops.Tests.Pages;

public sealed class CompletedModelTests
{
    [Fact]
    public async Task OnPostSubmitFormAsync_AnonymousWithoutInputs_ReturnsPageWithValidationErrors()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam);

        var harness = await TestHarness.CreateAsync(db);
        var model = harness.CreateModel();
        model.RecordId = record.Id;
        model.Name = string.Empty;
        model.Email = string.Empty;

        var result = await model.OnPostSubmitFormAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Contains(nameof(CompletedModel.Name), model.ModelState.Keys);
        Assert.Contains(nameof(CompletedModel.Email), model.ModelState.Keys);
        Assert.Empty(harness.MailService.SentMessages);
    }

    [Fact]
    public async Task OnPostSubmitFormAsync_SignedInUser_AttachesRecordAndSkipsRegistrationInputs()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam);

        var harness = await TestHarness.CreateAsync(db);
        var user = await harness.CreateUserAsync("student@example.com", "Portal", "Student", role: "Student", emailConfirmed: true);
        var principal = TestHarness.CreatePrincipal(user, "Student");

        var model = harness.CreateModel(principal);
        model.RecordId = record.Id;
        model.SubscribeNewsletter = true;

        var result = await model.OnPostSubmitFormAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);

        var updatedRecord = await db.Records.SingleAsync(r => r.Id == record.Id);
        Assert.Equal(user.Id, updatedRecord.UserId);
        Assert.Single(harness.MailService.SentMessages);

        var mail = Assert.IsType<TestResultData>(harness.MailService.SentMessages[0].Model);
        Assert.Equal("View my results", mail.ActionText);
        Assert.Contains("/en-us/examination/results/", mail.ActionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("student@example.com", harness.MailService.SentMessages[0].RecipientEmail);
    }

    [Fact]
    public async Task OnPostSubmitFormAsync_AnonymousExistingEmail_DoesNotAttachRecordAndEmailsClaimLink()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam);

        var harness = await TestHarness.CreateAsync(db);
        var existingUser = await harness.CreateUserAsync("existing@example.com", "Existing", "User", role: "Student", emailConfirmed: true);

        var model = harness.CreateModel();
        model.RecordId = record.Id;
        model.Name = "Someone Else";
        model.Email = existingUser.Email!;

        var result = await model.OnPostSubmitFormAsync();

        Assert.IsType<RedirectToPageResult>(result);

        var updatedRecord = await db.Records.SingleAsync(r => r.Id == record.Id);
        Assert.Null(updatedRecord.UserId);

        Assert.Single(harness.MailService.SentMessages);
        var mail = Assert.IsType<TestResultData>(harness.MailService.SentMessages[0].Model);
        Assert.Equal("View full results", mail.ActionText);
        Assert.Contains("/en-us/examination/results/", mail.ActionUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("view full details", mail.ActionIntro, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnGetClaimLevelTestResultAsync_ConfirmedUser_AttachesRecord()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam);

        var harness = await TestHarness.CreateAsync(db);
        var user = await harness.CreateUserAsync("claim@example.com", "Claim", "User", role: "Student", emailConfirmed: true);
        var token = harness.CreateClaimToken(record.Id, user.Email!);

        var model = harness.CreateModel();

        var result = await model.OnGetClaimLevelTestResultAsync(record.Id, token);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Portal/Exams/Record", redirect.PageName);

        var updatedRecord = await db.Records.SingleAsync(r => r.Id == record.Id);
        Assert.Equal(user.Id, updatedRecord.UserId);
    }

    private static Exam SeedLevelTestExam(ApplicationDbContext db)
    {
        var exam = new Exam
        {
            Name = "Level Test",
            CreatedByUserId = "teacher-1",
            DifficultyValue = 0,
            IntroductionPrimaryLanguage = "Primary intro",
            IntroductionSecondaryLanguage = "Secondary intro",
            PublicSlug = "level-test",
            PublishStatus = ExamPublishStatus.Published
        };

        db.Exams.Add(exam);
        db.SaveChanges();
        return exam;
    }

    private static iD_Develops.Models.Record SeedCompletedRecord(ApplicationDbContext db, Exam exam)
    {
        var record = new iD_Develops.Models.Record
        {
            Id = Guid.NewGuid(),
            ExamId = exam.Id,
            ExamStatus = ExamStatus.Completed,
            StartDateTime = DateTime.UtcNow.AddMinutes(-30),
            EndDateTime = DateTime.UtcNow,
            StatusReason = RecordStatusReason.ManualSubmit,
            StatusChangedAtUtc = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow,
            Score = 48
        };

        db.Records.Add(record);
        db.SaveChanges();
        return record;
    }

    private sealed class TestHarness
    {
        private readonly ApplicationDbContext _db;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly IOptions<MailSettings> _mailOptions;
        private readonly IExamVersionService _examVersionService;
        private readonly IParticipantAnswerService _participantAnswerService;
        private readonly IExamEvaluationService _examEvaluationService;
        private readonly IProspectService _prospectService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IServiceProvider _serviceProvider;

        public RecordingMailService MailService { get; }

        private TestHarness(
            ApplicationDbContext db,
            IDataProtectionProvider dataProtectionProvider,
            IOptions<MailSettings> mailOptions,
            IExamVersionService examVersionService,
            IParticipantAnswerService participantAnswerService,
            IExamEvaluationService examEvaluationService,
            IProspectService prospectService,
            UserManager<ApplicationUser> userManager,
            RecordingMailService mailService,
            IServiceProvider serviceProvider)
        {
            _db = db;
            _dataProtectionProvider = dataProtectionProvider;
            _mailOptions = mailOptions;
            _examVersionService = examVersionService;
            _participantAnswerService = participantAnswerService;
            _examEvaluationService = examEvaluationService;
            _prospectService = prospectService;
            _userManager = userManager;
            MailService = mailService;
            _serviceProvider = serviceProvider;
        }

        public static async Task<TestHarness> CreateAsync(ApplicationDbContext db)
        {
            var services = new ServiceCollection();
            var mailOptions = Options.Create(new MailSettings
            {
                From = "info@id-develops.com",
                PublicBaseUrl = "https://localhost:5001",
                SupportEmail = "info@id-develops.com"
            });

            services.AddSingleton<IOptions<MailSettings>>(mailOptions);
            services.AddSingleton<EmailBrandingFactory>();

            var serviceProvider = services.BuildServiceProvider();

            var userStore = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db);
            var roleStore = new RoleStore<IdentityRole, ApplicationDbContext, string>(db);

            var userManager = new UserManager<ApplicationUser>(
                userStore,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                new[] { new UserValidator<ApplicationUser>() },
                new[] { new PasswordValidator<ApplicationUser>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                serviceProvider,
                NullLogger<UserManager<ApplicationUser>>.Instance);

            var roleManager = new RoleManager<IdentityRole>(
                roleStore,
                new[] { new RoleValidator<IdentityRole>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                NullLogger<RoleManager<IdentityRole>>.Instance);

            var dataProtectionProvider = new EphemeralDataProtectionProvider();
            var mailService = new RecordingMailService();

            await EnsureRoleAsync(roleManager, "Student");

            return new TestHarness(
                db,
                dataProtectionProvider,
                mailOptions,
                new StubExamVersionService(db.Exams.Include(e => e.Questions).First()),
                new StubParticipantAnswerService(),
                new StubExamEvaluationService(),
                new ProspectService(db),
                userManager,
                mailService,
                serviceProvider);
        }

        public CompletedModel CreateModel(ClaimsPrincipal? principal = null)
        {
            var httpContextAccessor = new HttpContextAccessor();
            var model = new CompletedModel(
                _db,
                _participantAnswerService,
                _examEvaluationService,
                _examVersionService,
                httpContextAccessor,
                MailService,
                _mailOptions,
                NullLogger<CompletedModel>.Instance,
                _prospectService,
                _userManager,
                _dataProtectionProvider,
                new NoOpTurnstileService());

            var httpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider,
                User = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
            };
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost:5001");
            httpContext.Request.RouteValues["culture"] = "en-us";
            httpContextAccessor.HttpContext = httpContext;

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor(),
                new ModelStateDictionary());

            model.PageContext = new PageContext(actionContext);

            model.Url = new TestUrlHelper(actionContext);
            return model;
        }

        public async Task<ApplicationUser> CreateUserAsync(string email, string firstName, string lastName, string role, bool emailConfirmed)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = emailConfirmed
            };

            var create = await _userManager.CreateAsync(user, "Temp!12345aA");
            Assert.True(create.Succeeded, string.Join(" ", create.Errors.Select(e => e.Description)));

            var addRole = await _userManager.AddToRoleAsync(user, role);
            Assert.True(addRole.Succeeded, string.Join(" ", addRole.Errors.Select(e => e.Description)));
            return user;
        }

        public string CreateClaimToken(Guid recordId, string email)
        {
            var protector = _dataProtectionProvider.CreateProtector("LevelTestResultClaim:v1");
            var payload = $"{recordId:N}|{email.Trim().ToLowerInvariant()}";
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protector.Protect(payload)));
        }

        public static ClaimsPrincipal CreatePrincipal(ApplicationUser user, string role)
        {
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email ?? string.Empty));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            return new ClaimsPrincipal(identity);
        }

        private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string roleName)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                return;

            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    private sealed class RecordingMailService : IMailService
    {
        public List<SentMail> SentMessages { get; } = new();

        public Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default)
        {
            SentMessages.Add(new SentMail(templatePath, model!, recipientEmail, subject));
            return Task.FromResult(new OperationResult { Success = true });
        }

        public Task<OperationResult> SendAsync<TModel>(string templatePath, TModel model, string recipientEmail, string subject, string? fromEmail, byte[]? attachment = null, string? attachmentFileName = null, CancellationToken ct = default)
            => SendAsync(templatePath, model, recipientEmail, subject, attachment, attachmentFileName, ct);
    }

    private sealed record SentMail(string TemplatePath, object Model, string RecipientEmail, string Subject);

    private sealed class NoOpTurnstileService : ITurnstileService
    {
        public bool IsEnabled => false;

        public Task<bool> ValidateAsync(HttpContext httpContext, ModelStateDictionary modelState, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class StubExamVersionService : IExamVersionService
    {
        private readonly Exam _exam;

        public StubExamVersionService(Exam exam)
        {
            _exam = exam;
        }

        public Task<PublishedExamDescriptor?> GetPublishedDescriptorAsync(int examId, int? examVersionId = null)
            => Task.FromResult<PublishedExamDescriptor?>(null);

        public Task<List<QuestionMetadata>> GetQuestionMetadataAsync(int examId, int? examVersionId = null)
            => Task.FromResult(new List<QuestionMetadata>());

        public Task<Question?> GetQuestionForTakeAsync(int examId, int questionId, int? examVersionId = null)
            => Task.FromResult<Question?>(null);

        public Task<Exam?> GetExamForEvaluationAsync(int examId, int? examVersionId = null)
            => Task.FromResult<Exam?>(_exam.Id == examId ? _exam : null);

        public Task<OperationResult> PublishVersionAsync(Exam exam, IReadOnlyCollection<Question> questions)
            => throw new NotSupportedException();
    }

    private sealed class StubParticipantAnswerService : IParticipantAnswerService
    {
        public Task<ParticipantAnswer?> GetParticipantAnswerByRecordIdAsync(int questionId, Guid recordId)
            => Task.FromResult<ParticipantAnswer?>(null);

        public Task<string?> GetParticipantAnswerTextAsync(int questionId, Guid recordId)
            => Task.FromResult<string?>(null);

        public Task<List<ParticipantAnswer>> GetParticipantAnswersByRecordIdAsync(Guid recordId)
            => Task.FromResult(new List<ParticipantAnswer>());

        public Task<bool> SaveParticipantAnswerAsync(ParticipantAnswer participantAnswer)
            => throw new NotSupportedException();

        public Task<List<int>> GetAnsweredQuestionsAsync(Guid recordId)
            => Task.FromResult(new List<int>());
    }

    private sealed class StubExamEvaluationService : IExamEvaluationService
    {
        public ExamResults EvaluateExam(Exam exam, List<ParticipantAnswer> answerList)
            => new()
            {
                Questions = exam.Questions.ToList(),
                ParticipantAnswers = answerList,
                Score = 48,
                MaxScore = 60
            };

        public double EvaluateScore(Exam exam, List<ParticipantAnswer> answerList)
            => 48;
    }

    private sealed class TestUrlHelper : IUrlHelper
    {
        public TestUrlHelper(ActionContext actionContext)
        {
            ActionContext = actionContext;
        }

        public ActionContext ActionContext { get; }

        public string? Action(UrlActionContext actionContext) => "/action";

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url)
            => !string.IsNullOrWhiteSpace(url) &&
               (url.StartsWith("/", StringComparison.Ordinal) || url.StartsWith("~/", StringComparison.Ordinal));

        public string? Link(string? routeName, object? values) => "/link";

        public string? RouteUrl(UrlRouteContext routeContext)
        {
            var values = new RouteValueDictionary(routeContext.Values);

            if (values.TryGetValue("page", out var pageObj) && pageObj is string page)
            {
                return page switch
                {
                    "/Account/ConfirmEmail" => $"https://localhost:5001/Identity/Account/ConfirmEmail?userId={values["userId"]}&code={values["code"]}&returnUrl={Uri.EscapeDataString(values["returnUrl"]?.ToString() ?? "/")}",
                    "/Portal/Examination/Completed" => $"https://localhost:5001/en-us/portal/examination/completed?handler={values["pageHandler"]}&recordId={values["recordId"]}&token={values["token"]}",
                    "/Portal/Exams/Results" => $"/en-us/portal/exams/results/{values["recordId"]}",
                    "/Examination/Results" => $"https://localhost:5001/en-us/examination/results/{values["recordId"]}?token={values["token"]}",
                    _ => page
                };
            }

            return "/route";
        }
    }
}
