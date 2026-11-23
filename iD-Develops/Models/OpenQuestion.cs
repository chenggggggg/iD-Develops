namespace iD_Develops.Models
{
    public class OpenQuestion : Question
    {
        private List<OpenAnswerField> _openAnswerFields = [];
        public List<OpenAnswerField> OpenAnswerFields
        {
            get => _openAnswerFields;
            set => _openAnswerFields = value ?? [];
        }
    }
}
