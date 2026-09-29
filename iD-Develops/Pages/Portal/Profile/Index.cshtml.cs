using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages.Portal.Profile
{
    [Authorize(Policy = "PortalUser")]
    public class IndexModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
