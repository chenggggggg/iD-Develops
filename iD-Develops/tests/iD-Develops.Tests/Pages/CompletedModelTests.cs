using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Pages.Examination;
using iD_Develops.Services;
using iD_Develops.Tests.Infrastructure;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Localization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace iD_Develops.Tests.Pages;

public sealed class CompletedModelTests
{
    [Fact]
    public async Task IncrementalSnapshot_CompletionAndResultsUseHistoricalMetadataAfterExamIsArchived()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = SeedLevelTestExam(db);
        exam.TimeLimitSeconds = 3600;
        await db.SaveChangesAsync();
        var versions = new ExamVersionService(db);
        var preserved = (await versions.GetOrCreateAttemptVersionAsync(exam.Id))!;
        var record = SeedCompletedRecord(db, exam);
        record.ExamVersionId = preserved.ExamVersionId;
        exam.Name = "Renamed current exam";
        exam.TimeLimitSeconds = 2400;
        exam.PublicSlug = null;
        exam.PublishStatus = ExamPublishStatus.Archived;
        await db.SaveChangesAsync();
        var harness = await TestHarness.CreateAsync(db);
        var completion = harness.CreateModel(versionService: new ExamVersionService(db));
        completion.RecordId = record.Id;
        Assert.IsType<PageResult>(await completion.OnGetAsync());
        Assert.True(completion.ShowLevelTestCompletedPartial);
        var results = harness.CreateResultsModel(record.Id, new ExamVersionService(db));
        Assert.IsType<PageResult>(await results.OnGetAsync(CancellationToken.None));
        Assert.False(results.IsInvalidResult);
        Assert.Equal("Level Test", results.Summary!.ExamName);
        Assert.Equal(1, results.ExamVersionNumber);
        Assert.Equal(3600, results.TimeLimitSeconds);
    }

    [Theory]
    [InlineData("en-us", RecordStatusReason.ManualSubmit)]
    [InlineData("nl-nl", RecordStatusReason.ManualSubmit)]
    [InlineData("en-us", RecordStatusReason.DeadlinePassed)]
    [InlineData("nl-nl", RecordStatusReason.DeadlinePassed)]
    public async Task OnGetAsync_CompletedPublicLevelTest_RendersResultsForm(string culture, RecordStatusReason reason)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam, reason: reason);
        var harness = await TestHarness.CreateAsync(db);
        var model = harness.CreateModel();
        model.RecordId = record.Id;

        Assert.IsType<PageResult>(await model.OnGetAsync());
        Assert.True(model.ShowLevelTestCompletedPartial);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CompletedModel).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(CompletedModel).Assembly)
            .AddRazorPagesOptions(options => options.Conventions.Add(
                new ApplicationHostPageRouteModelConvention("localhost", "portal.localhost")));
        builder.Services.Configure<iD_Develops.Configuration.TurnstileSettings>(settings =>
        {
            settings.Enabled = true;
            settings.SiteKey = "test-site";
            settings.SecretKey = "test-secret";
        });
        builder.Services.AddSingleton<ITurnstileService, NoOpTurnstileService>();
        builder.Services.AddSingleton<IApplicationUrlService>(new ApplicationUrlService(
            Options.Create(new ApplicationUrlOptions
            {
                PublicBaseUrl = "https://localhost:5001",
                PortalBaseUrl = "https://portal.localhost:5001"
            })));
        await using var app = builder.Build();
        app.UseRouting();
        app.MapRazorPages();
        app.UseEndpoints(_ => { });
        using var scope = app.Services.CreateScope();
        model.HttpContext.RequestServices = scope.ServiceProvider;
        model.RouteData.Values["page"] = "/Examination/Completed";
        model.RouteData.Values["culture"] = culture;
        model.RouteData.Values["recordId"] = record.Id;
        model.HttpContext.Request.RouteValues = model.RouteData.Values;
        model.HttpContext.Request.Path = $"/{culture}/examination/completed/{record.Id}";
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single(endpoint => endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath == "/Examination/Completed");
        model.HttpContext.SetEndpoint(endpoint);
        model.PageContext.ActionDescriptor = new CompiledPageActionDescriptor(
            endpoint.Metadata.GetMetadata<PageActionDescriptor>()!);

        var viewEngine = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
        var view = viewEngine.GetView(null, "/Pages/Examination/Completed.cshtml", isMainPage: true);
        Assert.True(view.Success, string.Join(Environment.NewLine, view.SearchedLocations ?? []));
        using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<CompletedModel>(
            scope.ServiceProvider.GetRequiredService<IModelMetadataProvider>(), model.ModelState)
        {
            Model = model
        };
        var context = new ViewContext(model.PageContext, view.View, viewData,
            new TempDataDictionary(model.HttpContext,
                scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),
            writer, new HtmlHelperOptions());
        model.PageContext.ViewData = viewData;
        var razorPage = Assert.IsAssignableFrom<Page>(((RazorView)view.View).RazorPage);
        razorPage.PageContext = model.PageContext;

        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        string html;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            await view.View.RenderAsync(context);
            html = writer.ToString();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
        Assert.Contains("data-turnstile-field", html);
        Assert.Contains("/js/turnstile-form.js", html);
        Assert.Contains(culture == "nl-nl" ? "De beveiligingscontrole wordt uitgevoerd" : "Completing the security check", html);
        Assert.Contains("handler=SubmitForm", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Name\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Email\"", html, StringComparison.Ordinal);
        Assert.Contains(record.Id.ToString(), html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RecordStatusReason.ManualSubmit)]
    [InlineData(RecordStatusReason.DeadlinePassed)]
    public async Task OnPostSubmitFormAsync_AnonymousWithoutInputs_ReturnsPageWithValidationErrors(RecordStatusReason reason)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam, reason: reason);

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

    [Theory]
    [InlineData(RecordStatusReason.ManualSubmit)]
    [InlineData(RecordStatusReason.DeadlinePassed)]
    public async Task OnPostSubmitFormAsync_SignedInUser_AttachesRecordAndSkipsRegistrationInputs(RecordStatusReason reason)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam, reason: reason);

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

    [Theory]
    [InlineData(RecordStatusReason.ManualSubmit)]
    [InlineData(RecordStatusReason.DeadlinePassed)]
    public async Task OnPostSubmitFormAsync_AnonymousExistingEmail_DoesNotAttachRecordAndEmailsClaimLink(RecordStatusReason reason)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();

        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam, reason: reason);

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

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"https://portal.localhost:5001/exams/results/{record.Id:D}", redirect.Url);

        var updatedRecord = await db.Records.SingleAsync(r => r.Id == record.Id);
        Assert.Equal(user.Id, updatedRecord.UserId);
    }

    [Theory]
    [InlineData(ExamStatus.InProgress)]
    [InlineData(ExamStatus.Cancelled)]
    [InlineData(ExamStatus.NotStarted)]
    public async Task OnGetAsync_UnfinishedPublicAttempt_DoesNotExposeCompletion(ExamStatus status)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = SeedCompletedRecord(db, SeedLevelTestExam(db), status);
        var harness = await TestHarness.CreateAsync(db);
        var model = harness.CreateModel();
        model.RecordId = record.Id;

        Assert.IsType<NotFoundResult>(await model.OnGetAsync());
        Assert.False(model.ShowLevelTestCompletedPartial);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnGetAsync_UsesPublishedAttemptVersionToIdentifyLevelTest(bool publicSnapshot)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var exam = SeedLevelTestExam(db);
        var record = SeedCompletedRecord(db, exam, ExamStatus.Completed);
        exam.PublicSlug = publicSnapshot ? null : "level-test";
        record.ExamVersion = new ExamVersion
        {
            Exam = exam,
            VersionNumber = 1,
            Name = exam.Name,
            PublicSlug = publicSnapshot ? "level-test" : null
        };
        await db.SaveChangesAsync();
        var harness = await TestHarness.CreateAsync(db);
        var model = harness.CreateModel();
        model.RecordId = record.Id;

        var result = await model.OnGetAsync();

        if (publicSnapshot)
            Assert.IsType<PageResult>(result);
        else
            Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostResendAsync_CompletedAttempt_SendsResultsWithoutChangingStatus()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = SeedCompletedRecord(db, SeedLevelTestExam(db), ExamStatus.Completed);
        var harness = await TestHarness.CreateAsync(db);
        var model = harness.CreateModel();
        model.RecordId = record.Id;
        model.LevelTestResendName = "Test learner";
        model.LevelTestResendEmail = "learner@example.test";

        Assert.IsType<RedirectToPageResult>(await model.OnPostResendAsync());

        Assert.Single(harness.MailService.SentMessages);
        Assert.Equal(ExamStatus.Completed, (await db.Records.SingleAsync()).ExamStatus);
    }

    [Theory]
    [InlineData(ExamStatus.Completed, true)]
    [InlineData(ExamStatus.NotStarted, false)]
    [InlineData(ExamStatus.InProgress, false)]
    [InlineData(ExamStatus.Cancelled, false)]
    public async Task PublicResults_ValidToken_OnlyDisplaysFinishedAttempts(ExamStatus status, bool canView)
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = SeedCompletedRecord(db, SeedLevelTestExam(db), status);
        var harness = await TestHarness.CreateAsync(db);
        var model = harness.CreateResultsModel(record.Id);

        Assert.IsType<PageResult>(await model.OnGetAsync(CancellationToken.None));

        Assert.Equal(!canView, model.IsInvalidResult);
        if (canView)
            Assert.Equal(48d, Assert.IsType<ResultsModel.ResultSummaryViewModel>(model.Summary).Score);
        else
            Assert.Null(model.Summary);
    }

    [Fact]
    public async Task PublicResults_CompletedAttemptWithInvalidToken_DoesNotExposeAnswers()
    {
        using var factory = new SqliteTestDbFactory();
        await using var db = factory.CreateDbContext();
        var record = SeedCompletedRecord(db, SeedLevelTestExam(db), ExamStatus.Completed);
        var harness = await TestHarness.CreateAsync(db);
        var model = harness.CreateResultsModel(record.Id);
        model.Token = "invalid";

        Assert.IsType<PageResult>(await model.OnGetAsync(CancellationToken.None));
        Assert.True(model.IsInvalidResult);
        Assert.Null(model.Summary);
        Assert.Empty(model.Questions);
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

    private static iD_Develops.Models.Record SeedCompletedRecord(ApplicationDbContext db, Exam exam, ExamStatus status = ExamStatus.Completed, RecordStatusReason reason = RecordStatusReason.ManualSubmit)
    {
        var record = new iD_Develops.Models.Record
        {
            Id = Guid.NewGuid(),
            ExamId = exam.Id,
            ExamStatus = status,
            StartDateTime = DateTime.UtcNow.AddMinutes(-30),
            EndDateTime = DateTime.UtcNow,
            StatusReason = reason,
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

        public CompletedModel CreateModel(ClaimsPrincipal? principal = null, IExamVersionService? versionService = null)
        {
            var httpContextAccessor = new HttpContextAccessor();
            var model = new CompletedModel(
                _db,
                _participantAnswerService,
                _examEvaluationService,
                versionService ?? _examVersionService,
                httpContextAccessor,
                MailService,
                _mailOptions,
                NullLogger<iD_Develops.Pages.Portal.Examination.CompletedModel>.Instance,
                _prospectService,
                _userManager,
                _dataProtectionProvider,
                new NoOpTurnstileService(),
                new ApplicationUrlService(Options.Create(new ApplicationUrlOptions
                {
                    PublicBaseUrl = "https://localhost:5001",
                    PortalBaseUrl = "https://portal.localhost:5001"
                })),
                new CompletedTestLocalizer());

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

        public ResultsModel CreateResultsModel(Guid recordId, IExamVersionService? versionService = null)
            => new(_db, versionService ?? _examVersionService, _participantAnswerService, _examEvaluationService,
                _dataProtectionProvider, new ResultsTestLocalizer())
            {
                RecordId = recordId,
                Token = ResultsModel.CreatePublicResultToken(_dataProtectionProvider, recordId, "learner@example.test"),
                PageContext = new PageContext
                {
                    HttpContext = new DefaultHttpContext { RequestServices = _serviceProvider },
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                }
            };

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

        public Task<PublishedExamDescriptor?> GetOrCreateAttemptVersionAsync(int examId) => GetPublishedDescriptorAsync(examId);
        public Task<ExamVersion?> GetVersionAsync(int examId, int versionId) => throw new NotSupportedException();
        public Task<bool> IsMediaReferencedAsync(int examId, string reference) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamVersionSummary>> GetVersionHistoryAsync(IReadOnlyCollection<int> examIds) => throw new NotSupportedException();

        public Task<List<QuestionMetadata>> GetQuestionMetadataAsync(int examId, int? examVersionId = null)
            => Task.FromResult(new List<QuestionMetadata>());

        public Task<Question?> GetQuestionForTakeAsync(int examId, int questionId, int? examVersionId = null)
            => Task.FromResult<Question?>(null);

        public Task<Exam?> GetExamForEvaluationAsync(int examId, int? examVersionId = null)
            => Task.FromResult<Exam?>(_exam.Id == examId ? _exam : null);

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

    private sealed class CompletedTestLocalizer : IStringLocalizer<iD_Develops.Pages.Portal.Examination.CompletedModel>
    {
        private static readonly IReadOnlyDictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EmailActionIntroduction"] = "To view full details of your level test, click the button below.",
                ["ViewFullResultsAction"] = "View full results",
                ["ViewMyResultsAction"] = "View my results"
            };

        public LocalizedString this[string name]
            => new(name, Values.TryGetValue(name, out var value) ? value : name, resourceNotFound: !Values.ContainsKey(name));

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var localized = this[name];
                return new LocalizedString(name, string.Format(localized.Value, arguments), localized.ResourceNotFound);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => Values.Select(pair => new LocalizedString(pair.Key, pair.Value));
    }

    private sealed class ResultsTestLocalizer : IStringLocalizer<ResultsModel>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
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
