using iD_Develops.Models;

namespace iD_Develops.Services
{
    public sealed class AttemptAccessState
    {
        public static AttemptAccessState Missing { get; } = new()
        {
            Exists = false,
            UserMatches = false,
            ExamStatus = ExamStatus.Invalid
        };

        public bool Exists { get; init; }
        public bool UserMatches { get; init; }
        public int ExamId { get; init; }
        public int? ExamVersionId { get; init; }
        public ExamStatus ExamStatus { get; init; }
        public DateTime? EndDateTime { get; init; }
    }
}
