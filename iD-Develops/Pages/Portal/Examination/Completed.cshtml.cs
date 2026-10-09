using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using iD_Develops.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace iD_Develops.Pages.Portal.Examination
{
    public class CompletedModel : PageModel
    {
        private const string LevelTestPublicSlug = "level-test";
        private const string ClaimPurpose = "LevelTestResultClaim:v1";

        [BindProperty(SupportsGet = true)]
        public Guid RecordId { get; set; }

        [BindProperty]
        public string? Name { get; set; }

        [BindProperty]
        public string? Email { get; set; }

        [BindProperty]
        public bool SubscribeNewsletter { get; set; }

        public bool ShowLevelTestCompletedPartial { get; private set; }
        public bool IsSignedInPortalUser { get; private set; }

        [TempData]
        public string? LevelTestSuccessMessage { get; set; }

        [TempData]
        public string? LevelTestErrorMessage { get; set; }

        [TempData]
        public string? LevelTestResendName { get; set; }

        [TempData]
        public string? LevelTestResendEmail { get; set; }

        private readonly ApplicationDbContext _dbContext;
        private readonly IParticipantAnswerService _participantAnswerService;
        private readonly IExamEvaluationService _examEvaluationService;
        private readonly IExamVersionService _examVersionService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMailService _mailService;
        private readonly MailSettings _mailSettings;
        private readonly ILogger<CompletedModel> _logger;
        private readonly IProspectService _prospectService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly IDataProtector _claimProtector;
        private readonly ITurnstileService _turnstileService;
        private readonly IApplicationUrlService _applicationUrls;
        private readonly IStringLocalizer<CompletedModel> _localizer;

        public CompletedModel(
            ApplicationDbContext dbContext,
            IParticipantAnswerService participantAnswerService,
            IExamEvaluationService examEvaluationService,
            IExamVersionService examVersionService,
            IHttpContextAccessor httpContextAccessor,
            IMailService mailService,
            IOptions<MailSettings> mailSettings,
            ILogger<CompletedModel> logger,
            IProspectService prospectService,
            UserManager<ApplicationUser> userManager,
            IDataProtectionProvider dataProtectionProvider,
            ITurnstileService turnstileService,
            IApplicationUrlService applicationUrls,
            IStringLocalizer<CompletedModel> localizer)
        {
            _dbContext = dbContext;
            _participantAnswerService = participantAnswerService;
            _examEvaluationService = examEvaluationService;
            _examVersionService = examVersionService;
            _httpContextAccessor = httpContextAccessor;
            _mailService = mailService;
            _mailSettings = mailSettings.Value;
            _logger = logger;
            _prospectService = prospectService;
            _userManager = userManager;
            _dataProtectionProvider = dataProtectionProvider;
            _claimProtector = dataProtectionProvider.CreateProtector(ClaimPurpose);
            _turnstileService = turnstileService;
            _applicationUrls = applicationUrls;
            _localizer = localizer;
        }

        protected virtual bool IsPortalSurface
            => true;

        public async Task<IActionResult> OnGetAsync()
        {
            var (record, accessResult) = await GetAccessibleRecordAsync();
            if (accessResult != null)
                return accessResult;

            ShowLevelTestCompletedPartial = IsCompletedLevelTestRecord(record);
            IsSignedInPortalUser = IsPortalUser();

            if (IsSignedInPortalUser)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    Name = user.FullName;
                    Email = user.Email ?? string.Empty;
                }
            }

            KeepLevelTestResendState();

            return Page();
        }

        public async Task<IActionResult> OnPostSubmitFormAsync()
        {
            var (record, accessResult) = await GetAccessibleRecordAsync();
            if (accessResult != null)
                return accessResult;

            if (!IsCompletedLevelTestRecord(record))
                return RedirectToPage(new { recordId = RecordId });

            ShowLevelTestCompletedPartial = true;
            IsSignedInPortalUser = IsPortalUser();

            Name = Name?.Trim() ?? string.Empty;
            Email = Email?.Trim() ?? string.Empty;

            if (!IsSignedInPortalUser)
            {
                await _turnstileService.ValidateAsync(HttpContext, ModelState);
            }

            if (!ValidateLevelTestSubmission())
                return Page();

            var identityResult = await ResolveIdentityFlowAsync(record!);
            if (!identityResult.Success)
            {
                ModelState.AddModelError(string.Empty, identityResult.ErrorMessage ?? _localizer["RequestProcessingError"]);
                return Page();
            }

            var exam = await _examVersionService.GetExamForEvaluationAsync(record!.ExamId, record.ExamVersionId);
            if (exam == null)
            {
                ModelState.AddModelError(string.Empty, _localizer["LevelTestLoadError"]);
                return Page();
            }

            var answerList = await _participantAnswerService.GetParticipantAnswersByRecordIdAsync(RecordId) ?? new List<ParticipantAnswer>();
            var examResults = _examEvaluationService.EvaluateExam(exam, answerList);

            try
            {
                await _prospectService.SaveProspectAsync(Email, isUnsubscribed: !SubscribeNewsletter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save prospect subscription for level test completion. RecordId={RecordId}", RecordId);
            }

            var emailResult = await SubmitResultEmailAsync(exam, examResults.Score, examResults.MaxScore, identityResult.EmailAction);
            if (!emailResult.Success)
            {
                LevelTestErrorMessage = emailResult.ErrorMessage ?? _localizer["EmailSendError"];
                return RedirectToPage(new { recordId = RecordId });
            }

            if (!IsSignedInPortalUser)
            {
                LevelTestResendName = Name;
                LevelTestResendEmail = Email;
            }

            LevelTestSuccessMessage = identityResult.SuccessMessage;
            return RedirectToPage(new { recordId = RecordId });
        }

        public async Task<IActionResult> OnPostResendAsync()
        {
            var (record, accessResult) = await GetAccessibleRecordAsync();
            if (accessResult != null)
                return accessResult;

            if (!IsCompletedLevelTestRecord(record))
                return RedirectToPage(new { recordId = RecordId });

            if (IsPortalUser())
                return RedirectToPage(new { recordId = RecordId });

            Name = (LevelTestResendName ?? string.Empty).Trim();
            Email = (LevelTestResendEmail ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email))
            {
                LevelTestErrorMessage = _localizer["ResendDetailsMissingError"];
                return RedirectToPage(new { recordId = RecordId });
            }

            var emailAction = new LevelTestEmailAction(
                _localizer["EmailActionIntroduction"],
                BuildPublicResultUrl(record!.Id, Email),
                _localizer["ViewFullResultsAction"]);

            var exam = await _examVersionService.GetExamForEvaluationAsync(record.ExamId, record.ExamVersionId);
            if (exam == null)
            {
                LevelTestErrorMessage = _localizer["LevelTestLoadError"];
                return RedirectToPage(new { recordId = RecordId });
            }

            var answerList = await _participantAnswerService.GetParticipantAnswersByRecordIdAsync(RecordId) ?? new List<ParticipantAnswer>();
            var examResults = _examEvaluationService.EvaluateExam(exam, answerList);

            var emailResult = await SubmitResultEmailAsync(exam, examResults.Score, examResults.MaxScore, emailAction);
            if (!emailResult.Success)
            {
                LevelTestErrorMessage = emailResult.ErrorMessage ?? _localizer["EmailResendError"];
                return RedirectToPage(new { recordId = RecordId });
            }

            LevelTestSuccessMessage = _localizer["EmailResentMessage"];
            LevelTestResendName = Name;
            LevelTestResendEmail = Email;
            return RedirectToPage(new { recordId = RecordId });
        }

        public async Task<IActionResult> OnGetClaimLevelTestResultAsync(Guid recordId, string token)
        {
            if (recordId == Guid.Empty || string.IsNullOrWhiteSpace(token))
                return BadRequest();

            (Guid TokenRecordId, string Email)? payload;
            try
            {
                payload = UnprotectClaimToken(token);
            }
            catch
            {
                return BadRequest();
            }

            if (payload == null || payload.Value.TokenRecordId != recordId)
                return BadRequest();

            var record = await _dbContext.Records
                .FirstOrDefaultAsync(r => r.Id == recordId && !r.IsDeleted);

            if (record == null)
                return NotFound();

            var user = await _userManager.FindByEmailAsync(payload.Value.Email);
            if (user == null || !user.EmailConfirmed)
                return Challenge();

            if (!string.IsNullOrWhiteSpace(record.UserId) && !string.Equals(record.UserId, user.Id, StringComparison.Ordinal))
                return Forbid();

            record.UserId = user.Id;
            await _dbContext.SaveChangesAsync();

            return Redirect(_applicationUrls.PortalUrl($"/exams/results/{recordId:D}"));
        }

        private async Task<(bool Success, LevelTestEmailAction EmailAction, string SuccessMessage, string? ErrorMessage)> ResolveIdentityFlowAsync(Record record)
        {
            if (IsSignedInPortalUser)
            {
                var signedInUser = await _userManager.GetUserAsync(User);
                if (signedInUser == null || string.IsNullOrWhiteSpace(signedInUser.Email))
                    return (false, LevelTestEmailAction.Empty, string.Empty, _localizer["AccountLoadError"]);

                record.UserId = signedInUser.Id;
                await _dbContext.SaveChangesAsync();

                Name = signedInUser.FullName;
                Email = signedInUser.Email;

                return (
                    true,
                    new LevelTestEmailAction(
                        _localizer["EmailActionIntroduction"],
                        BuildPublicResultUrl(record.Id, signedInUser.Email),
                        _localizer["ViewMyResultsAction"]),
                    _localizer["FollowUpEmailMessage"],
                    null);
            }

            var submittedEmail = Email ?? string.Empty;
            return (
                true,
                new LevelTestEmailAction(
                    _localizer["EmailActionIntroduction"],
                    BuildPublicResultUrl(record.Id, submittedEmail),
                    _localizer["ViewFullResultsAction"]),
                _localizer["SecureLinkEmailMessage"],
                null);
        }

        private bool ValidateLevelTestSubmission()
        {
            if (IsSignedInPortalUser)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(Name))
                ModelState.AddModelError(nameof(Name), _localizer["NameRequiredError"]);

            if (string.IsNullOrWhiteSpace(Email))
            {
                ModelState.AddModelError(nameof(Email), _localizer["EmailRequiredError"]);
            }
            else if (!new EmailAddressAttribute().IsValid(Email))
            {
                ModelState.AddModelError(nameof(Email), _localizer["EmailInvalidError"]);
            }

            return ModelState.IsValid;
        }

        private async Task<(Record? Record, IActionResult? AccessResult)> GetAccessibleRecordAsync()
        {
            var record = await GetRecordWithExamAsync();
            if (record == null)
                return (null, NotFound());

            var isLevelTest = IsCompletedLevelTestRecord(record);
            var isPortalRequest = IsPortalSurface;
            if (isLevelTest && isPortalRequest)
            {
                return (record, Redirect(_applicationUrls.PublicUrl(
                    $"/en-us/examination/completed/{record.Id:D}")));
            }

            if (!isLevelTest && !isPortalRequest)
                return (record, NotFound());

            if (isLevelTest)
                return (record, null);

            if (User?.Identity?.IsAuthenticated != true)
                return (record, Challenge());

            if (!IsPortalUser())
                return (record, Forbid());

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId) || !string.Equals(record.UserId, userId, StringComparison.Ordinal))
                return (record, Forbid());

            return (record, null);
        }

        private async Task<Record?> GetRecordWithExamAsync()
        {
            if (RecordId == Guid.Empty)
                return null;

            return await _dbContext.Records
                .Include(r => r.Exam)
                .FirstOrDefaultAsync(r => r.Id == RecordId && !r.IsDeleted);
        }

        private static bool IsCompletedLevelTestRecord(Record? record)
        {
            return record != null
                && record.ExamStatus == ExamStatus.Completed
                && string.Equals(record.Exam?.PublicSlug, LevelTestPublicSlug, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPortalUser()
            => (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")) || User.IsInRole("Teacher") || User.IsInRole("Student");

        public string SupportEmail
            => string.IsNullOrWhiteSpace(_mailSettings.SupportEmail) ? _mailSettings.From : _mailSettings.SupportEmail!;

        private async Task<OperationResult> SubmitResultEmailAsync(Exam exam, double score, double maxScore, LevelTestEmailAction emailAction)
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request == null)
                return new OperationResult { Success = false, ErrorMessage = _localizer["EmailBuildError"] };

            var recipientEmail = Email ?? string.Empty;
            if (string.IsNullOrWhiteSpace(recipientEmail))
                return new OperationResult { Success = false, ErrorMessage = _localizer["RecipientEmailError"] };

            var baseUrl = $"{request.Scheme}://{request.Host}";
            var culture = _httpContextAccessor.HttpContext?.Request.RouteValues["culture"]?.ToString() ?? "en-US";
            var resultLevel = ResolveLevelTestResult(score);

            var levelTestFormData = new TestResultData
            {
                DisplayName = Name,
                From = _mailSettings.From,
                Body = _localizer["EmailBodyFormat", score],
                Subject = _localizer["EmailSubject"],
                ResultURL = BuildPublicResultUrl(RecordId, recipientEmail),
                ProductsURL = $"{baseUrl}/{culture}/pricing",
                ContactURL = $"{baseUrl}/{culture}/contact",
                ActionIntro = emailAction.IntroText,
                ActionUrl = emailAction.Url,
                ActionText = emailAction.ButtonText,
                Score = score,
                MaxScore = maxScore,
                ResultLevel = resultLevel.Level,
                ResultAdvice = resultLevel.Advice,
                Branding = HttpContext.RequestServices.GetRequiredService<EmailBrandingFactory>().Create(baseUrl)
            };

            try
            {
                var result = await _mailService.SendAsync(
                    "/wwwroot/templates/TestResultMailTemplate.cshtml",
                    levelTestFormData,
                    recipientEmail,
                    levelTestFormData.Subject);

                if (result.Success)
                {
                    _logger.LogInformation("Email with exam results has been sent successfully.");
                    return new OperationResult { Success = true, ErrorMessage = _localizer["EmailSentMessage"] };
                }

                _logger.LogError("Error occurred while sending email with exam results: {ErrorMessage}", result.ErrorMessage);
                return new OperationResult { Success = false, ErrorMessage = result.ErrorMessage };
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Error occurred while sending email with exam results.");
                return new OperationResult { Success = false, ErrorMessage = _localizer["EmailSendError"] };
            }
        }

        private (string Level, string Advice) ResolveLevelTestResult(double score)
        {
            if (score >= 51 && score <= 60)
            {
                return (
                    _localizer["HighResultLevel"],
                    _localizer["HighResultAdvice"]);
            }

            if (score >= 45 && score <= 50)
            {
                return (
                    _localizer["MiddleResultLevel"],
                    _localizer["MiddleResultAdvice"]);
            }

            return (
                _localizer["LowResultLevel"],
                _localizer["LowResultAdvice"]);
        }

        private void KeepLevelTestResendState()
        {
            if (!string.IsNullOrWhiteSpace(LevelTestResendName))
                TempData.Keep(nameof(LevelTestResendName));

            if (!string.IsNullOrWhiteSpace(LevelTestResendEmail))
                TempData.Keep(nameof(LevelTestResendEmail));
        }

        private string BuildPublicResultUrl(Guid recordId, string email)
        {
            var token = iD_Develops.Pages.Examination.ResultsModel.CreatePublicResultToken(_dataProtectionProvider, recordId, email);
            return Url.Page(
                "/Examination/Results",
                pageHandler: null,
                values: new { recordId, token },
                protocol: Request.Scheme) ?? string.Empty;
        }

        private string ProtectClaimToken(Guid recordId, string email)
        {
            var payload = $"{recordId:N}|{email.Trim().ToLowerInvariant()}";
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(_claimProtector.Protect(payload)));
        }

        private (Guid TokenRecordId, string Email)? UnprotectClaimToken(string token)
        {
            var protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            var payload = _claimProtector.Unprotect(protectedPayload);
            var parts = payload.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var recordId))
                return null;

            return (recordId, parts[1]);
        }

        private readonly record struct LevelTestEmailAction(string IntroText, string Url, string ButtonText)
        {
            public static LevelTestEmailAction Empty => new(string.Empty, string.Empty, string.Empty);
        }
    }
}
