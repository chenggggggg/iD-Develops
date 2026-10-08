using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace iD_Develops.Pages.Examination
{
    public sealed class CompletedModel : iD_Develops.Pages.Portal.Examination.CompletedModel
    {
        public CompletedModel(
            ApplicationDbContext dbContext,
            IParticipantAnswerService participantAnswerService,
            IExamEvaluationService examEvaluationService,
            IExamVersionService examVersionService,
            IHttpContextAccessor httpContextAccessor,
            IMailService mailService,
            IOptions<MailSettings> mailSettings,
            ILogger<iD_Develops.Pages.Portal.Examination.CompletedModel> logger,
            IProspectService prospectService,
            UserManager<ApplicationUser> userManager,
            IDataProtectionProvider dataProtectionProvider,
            ITurnstileService turnstileService,
            IApplicationUrlService applicationUrls)
            : base(
                dbContext,
                participantAnswerService,
                examEvaluationService,
                examVersionService,
                httpContextAccessor,
                mailService,
                mailSettings,
                logger,
                prospectService,
                userManager,
                dataProtectionProvider,
                turnstileService,
                applicationUrls)
        {
        }

        protected override bool IsPortalSurface
            => false;
    }
}
