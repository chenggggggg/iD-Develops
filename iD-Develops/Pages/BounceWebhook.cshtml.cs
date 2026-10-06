using iD_Develops.Configuration;
using iD_Develops.Data;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace iD_Develops.Pages
{
    [IgnoreAntiforgeryToken]
    public class BounceWebhookModel : PageModel
    {
        private readonly MailSettings _settings;
        private readonly ILogger<BounceWebhookModel> _logger;
        private readonly IProspectService _prospectService;

        public BounceWebhookModel(IOptions<MailSettings> settings, ILogger<BounceWebhookModel> logger, IProspectService prospectService)
        {
            _settings = settings.Value;
            _logger = logger;
            _prospectService = prospectService;
        }

        public async Task<IActionResult> OnPostAsync([FromBody] JObject payload)
        {
            if (!Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey) || extractedApiKey != _settings.WebhookApiKey)
            {
                _logger.LogWarning("Unauthorized access attempt.");
                return Unauthorized();
            }

            if (payload == null)
            {
                _logger.LogError("Payload is null");
                return BadRequest("Payload cannot be null");
            }

            var emailToken = payload["Email"];
            var bounceTypeToken = payload["Type"];

            if (emailToken == null || bounceTypeToken == null)
            {
                _logger.LogError("Email or Type is missing in the payload");
                return BadRequest("Invalid payload data");
            }

            var email = emailToken.ToString();
            var bounceType = bounceTypeToken.ToString();
            _logger.LogInformation("Received bounce webhook of type {BounceType}.", bounceType);
            var isHardBounce = bounceType == "HardBounce";

            var prospect = await _prospectService.UpdateProspectHardBounceAsync(email, isHardBounce);

            if (prospect != null)
            {
                return new JsonResult(new { success = false });
            }

            return new JsonResult(new { success = true });
        }

    }
}
