namespace iD_Develops.Services
{
    public interface IExamAccessService
    {
        Task<bool> CanTakeExamAsync(
            string userId,
            int examId,
            CancellationToken cancellationToken = default);
    }
}
