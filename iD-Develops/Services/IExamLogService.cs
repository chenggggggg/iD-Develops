using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface IExamLogService
    {
        void EnqueueSaveExamLog(ExamLog log);
    }
}
