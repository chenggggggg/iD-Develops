using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace iD_Develops.Pages
{
    public class SuccessModel : PageModel
    {
        public IActionResult OnGet(
            [FromQuery] string? session_id,
            [FromQuery] bool local = false,
            [FromQuery] string? product = null,
            [FromQuery] string? status = null)
        {
            return RedirectToPage("/PaymentResult", new
            {
                session_id,
                local,
                product,
                status
            });
        }
    }
}
