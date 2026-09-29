using System.ComponentModel.DataAnnotations.Schema;

namespace iD_Develops.Models
{
    [Table("AppointmentTypeTeachers")]
    public class AppointmentTypeTeacher
    {
        public int AppointmentTypeId { get; set; }
        public AppointmentType AppointmentType { get; set; } = null!;

        public string TeacherUserId { get; set; } = string.Empty;
        public ApplicationUser TeacherUser { get; set; } = null!;
    }
}
