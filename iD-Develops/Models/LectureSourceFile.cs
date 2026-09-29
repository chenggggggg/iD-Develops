using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("LectureSourceFiles")]
    public class LectureSourceFile
    {
        public int Id { get; set; }

        public int LectureId { get; set; }

        public Lecture Lecture { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(1024)]
        public string FileReference { get; set; } = string.Empty;

        public int OrderNumber { get; set; }
    }
}
