using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace iD_Develops.Services
{
    public interface ITurnstileService
    {
        bool IsEnabled { get; }
        Task<bool> ValidateAsync(HttpContext httpContext, ModelStateDictionary modelState, CancellationToken ct = default);
    }
}
