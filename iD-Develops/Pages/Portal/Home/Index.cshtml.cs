using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;

namespace iD_Develops.Pages.Portal.Home
{
    [Authorize(Policy = "PortalUser")]
    public class IndexModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
