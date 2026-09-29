using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("LectureCompletions")]
    public class LectureCompletion
    {
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser ApplicationUser { get; set; } = null!;

        public int LectureId { get; set; }

        public Lecture Lecture { get; set; } = null!;

        public bool IsCompleted { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}
