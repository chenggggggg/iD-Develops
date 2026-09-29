using iD_Develops.Models;

namespace iD_Develops.Services
{
    public interface IExamEvaluationService
    {
        /// <summary>
        /// Evaluates the answers compared with the correct answers from Exam and updates IsCorrect property.
        /// </summary>
        /// <param name="exam"></param>
        /// <param name="answerList"></param>
        /// <returns>ExamResults with updated IsCorrect properties</returns>
        ExamResults EvaluateExam(Exam exam, List<ParticipantAnswer> answerList);

        double EvaluateScore(Exam exam, List<ParticipantAnswer> answerList);

        //void CleanHtmlTags(Exam exam, List<ParticipantAnswer> participantAnswers);
    }
}
