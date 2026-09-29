using iD_Develops.Models;

namespace iD_Develops.Services
{
    public class ExamEvaluationService : IExamEvaluationService
    {
        public ExamResults EvaluateExam(Exam exam, List<ParticipantAnswer> participantAnswers)
        {
            var score = EvaluateScore(exam, participantAnswers);
            var maxScore = ResolveMaxScore(exam);

            return new ExamResults
            {
                ParticipantAnswers = participantAnswers,
                Questions = exam.Questions.ToList(),
                Score = score,
                MaxScore = maxScore
            };
        }

        public double EvaluateScore(Exam exam, List<ParticipantAnswer> participantAnswers)
        {
            double score = 0;

            foreach (var q in exam.Questions)
            {
                var participantAnswer = participantAnswers.Find(a => a.QuestionId == q.Id);
                if (participantAnswer == null) continue;

                if (EvaluateQuestion(q, participantAnswer, out var matchingCorrectAnswer) && matchingCorrectAnswer != null)
                {
                    participantAnswer.IsCorrect = true;
                    score += CalculateScore(q, matchingCorrectAnswer);
                }
            }

            return score;
        }

        private bool EvaluateQuestion(Question q, ParticipantAnswer participantAnswer, out CorrectAnswer? matchingCorrectAnswer)
        {
            // Check if CorrectAnswers collection is null or empty
            if (q.CorrectAnswers == null || !q.CorrectAnswers.Any())
            {
                matchingCorrectAnswer = null;
                return false; // No correct answer defined, so cannot evaluate
            }

            switch (q)
            {
                case MultipleChoiceQuestion _:
                case TrueOrFalseQuestion _:
                    // Case-insensitive comparison for multiple choice and true/false questions
                    matchingCorrectAnswer = q.CorrectAnswers.FirstOrDefault(ca =>
                        ca.Text.Trim().Equals(participantAnswer.AnswerText.Trim(), StringComparison.OrdinalIgnoreCase));
                    break;

                case OpenQuestion _:
                    // Case-sensitive comparison for open questions
                    matchingCorrectAnswer = q.CorrectAnswers.FirstOrDefault(ca =>
                        ca.Text.Trim().Equals(participantAnswer.AnswerText.Trim(), StringComparison.Ordinal));
                    break;

                default:
                    matchingCorrectAnswer = null;
                    return false;
            }

            return matchingCorrectAnswer != null;
        }

        //public void CleanHtmlTags(Exam exam, List<ParticipantAnswer> participantAnswers)
        //{
        //    foreach (var examQuestion in exam.ExamQuestions)
        //    {
        //        CleanQuestionText(examQuestion.Question);
        //    }

        //    // Clean HTML tags from participant answers
        //    participantAnswers.ForEach(answer => answer.AnswerText = RemoveHtmlTags(answer.AnswerText));
        //}

        private void CleanQuestionText(Question q)
        {
            if (q == null) return; // Early exit if the question is null

            // Clean each field if it's not null
            if (!string.IsNullOrEmpty(q.Text))
            {
                q.Text = RemoveHtmlTags(q.Text);
            }

            if (!string.IsNullOrEmpty(q.Scenario))
            {
                q.Scenario = RemoveHtmlTags(q.Scenario);
            }

            if (!string.IsNullOrEmpty(q.Feedback))
            {
                q.Feedback = RemoveHtmlTags(q.Feedback);
            }

            // Clean the correct answers if they exist
            if (q.CorrectAnswers != null)
            {
                CleanCorrectAnswers(q.CorrectAnswers);
            }
        }

        private void CleanCorrectAnswers(IEnumerable<CorrectAnswer> correctAnswers)
        {
            foreach (var answer in correctAnswers)
            {
                answer.Text = RemoveHtmlTags(answer.Text);
            }
        }

        private double CalculateScore(Question question, CorrectAnswer correctAnswer)
        {
            if (question?.Score > 0)
                return question.Score.Value;

            return correctAnswer?.Score > 0 ? correctAnswer.Score.Value : 1;
        }

        private static double ResolveMaxScore(Exam exam)
        {
            var summedQuestionScore = exam?.Questions?
                .Select(q => q.Score.HasValue && q.Score.Value > 0 ? q.Score.Value : 1d)
                .DefaultIfEmpty(0d)
                .Sum() ?? 0d;

            return summedQuestionScore;
        }

        private string RemoveHtmlTags(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}
