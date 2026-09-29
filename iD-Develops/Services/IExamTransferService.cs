namespace iD_Develops.Services
{
    public sealed record ExamTransferExport(string FileName, byte[] Content);

    public sealed record ExamTransferImportResult(
        bool Success,
        int? ExamId = null,
        string? ErrorMessage = null,
        IReadOnlyList<string>? Warnings = null,
        string? FailureStage = null,
        IReadOnlyList<string>? DiagnosticDetails = null);

    public sealed class ExamTransferImportException : Exception
    {
        public ExamTransferImportException(string stage, string message, Exception innerException)
            : base(message, innerException)
        {
            Stage = stage;
        }

        public string Stage { get; }
    }

    public interface IExamTransferService
    {
        Task<ExamTransferExport?> ExportAsync(int examId, CancellationToken cancellationToken = default);

        Task<ExamTransferImportResult> ImportAsync(
            Stream jsonStream,
            string createdByUserId,
            CancellationToken cancellationToken = default);
    }
}
