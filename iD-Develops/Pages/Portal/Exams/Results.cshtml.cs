using iD_Develops.Models;
using iD_Develops.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace iD_Develops.Pages.Portal.Exams
{
    [Authorize(Policy = "PortalUser")]
    public class ResultsModel : PageModel
    {
        public sealed record ResultListItem(
            Guid RecordId,
            int ExamId,
            string ExamName,
            int? ExamVersionNumber,
            DateTime StartDateTime,
            DateTime? EndDateTime,
            ExamStatus ExamStatus,
            double Score,
            double MaxScore);

        private readonly IRecordService _recordService;

        public IReadOnlyList<ResultListItem> Results { get; private set; } = Array.Empty<ResultListItem>();

        public ResultsModel(IRecordService recordService)
        {
            _recordService = recordService;
        }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                Results = Array.Empty<ResultListItem>();
                return;
            }

            var records = await _recordService.GetUserResultRecordsAsync(userId, cancellationToken);
            Results = records
                .GroupBy(r => r.ExamId)
                .SelectMany(group => group
                    .OrderBy(r => r.StartDateTime)
                    .Select(record => new ResultListItem(
                        record.RecordId,
                        record.ExamId,
                        record.ExamName,
                        record.ExamVersionNumber,
                        record.StartDateTime,
                        record.EndDateTime,
                        record.ExamStatus,
                        record.Score,
                        record.MaxScore))
                    .Reverse())
                .OrderByDescending(r => r.EndDateTime ?? r.StartDateTime)
                .ToList();
        }
    }
}
