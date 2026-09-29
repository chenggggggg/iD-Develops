using iD_Develops.Data;
using iD_Develops.Models;

namespace iD_Develops.Services
{
    public class ExamLogService : IExamLogService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ExamLogService(IBackgroundTaskQueue taskQueue, IServiceScopeFactory serviceScopeFactory)
        {
            _taskQueue = taskQueue;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public void EnqueueSaveExamLog(ExamLog log)
        {
            _taskQueue.QueueBackgroundWorkItem(async token =>
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await SaveExamLog(dbContext, log, token);
                }
            });
        }

        private async Task SaveExamLog(ApplicationDbContext dbContext, ExamLog log, CancellationToken cancellationToken)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            await dbContext.ExamLogs.AddAsync(log, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
