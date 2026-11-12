using iD_Develops.Data;
using iD_Develops.Pages.Portal.Examination;
using iD_Develops.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace iD_Develops.Utilities;

public sealed class PublicLevelTestAccessFilter(ApplicationDbContext db, IExamVersionService versions) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (!string.Equals(context.ActionDescriptor.ViewEnginePath, "/Portal/Examination/Level-Test", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        // Reject conflicting IDs before any attempt expiry, grading, or answer mutation.
        var ids = new HashSet<Guid>();
        void Add(string? value)
        {
            if (Guid.TryParse(value, out var id) && id != Guid.Empty) ids.Add(id);
        }
        Add(context.RouteData.Values["recordId"]?.ToString());
        foreach (var value in context.HttpContext.Request.Query["recordId"]) Add(value);
        if (context.HandlerInstance is LevelTestModel model && model.RecordId != Guid.Empty) ids.Add(model.RecordId);
        if (context.HandlerArguments.TryGetValue("recordId", out var argument) && argument is Guid idArgument && idArgument != Guid.Empty) ids.Add(idArgument);
        if (context.HandlerArguments.TryGetValue("formData", out var form) && form is IFormCollection values)
            foreach (var value in values["recordId"]) Add(value);

        if (ids.Count != 1)
        {
            context.Result = ids.Count > 1 ? new BadRequestResult() : new NotFoundResult();
            return;
        }

        var recordId = ids.Single();
        var record = await db.Records.AsNoTracking().Where(record => record.Id == recordId && !record.IsDeleted)
            .Select(record => new { record.ExamId, record.ExamVersionId }).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        var descriptor = record == null ? null : await versions.GetPublishedDescriptorAsync(record.ExamId, record.ExamVersionId);
        if (!string.Equals(descriptor?.PublicSlug, "level-test", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
