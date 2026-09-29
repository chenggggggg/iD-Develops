using iD_Develops.Models;

namespace iD_Develops.Pages.Shared.Examination.Questions
{
    public enum QuestionRenderMode
    {
        Take = 0,
        Edit = 1
    }

    public sealed class QuestionShellModel
    {
        // C# 10 friendly: no 'required'
        public Question Question { get; init; } = null!;
        public string? SavedAnswerText { get; init; }

        public QuestionRenderMode Mode { get; init; } = QuestionRenderMode.Take;
    }
}
