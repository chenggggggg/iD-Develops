using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("LearningMaterials")]
    public class LearningMaterial
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(1024)]
        public string FileReference { get; set; } = string.Empty;

        public int CourseId { get; set; }

        public Course Course { get; set; } = null!;
    }
}
