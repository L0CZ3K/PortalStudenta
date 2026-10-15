namespace Portal_Studenta.Models
{
    public class Lesson
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Place { get; set; } = string.Empty;
        public string Person { get; set; } = string.Empty;
        public string Desc { get; set; } = string.Empty;
        public string Group { get; set; } = "ALL";
        public bool IsCanceled { get; set; }
        public bool IsPersonal { get; set; }
        public bool IsHighPriority { get; set; }

        // --- ORYGINALNE DANE Z USOS ---
        public DateTime OriginalStart { get; set; }
        public DateTime OriginalEnd { get; set; }
        public string OriginalPlace { get; set; } = string.Empty;

        // --- DODATKI DO ZAJĘĆ ---
        public bool HasExam { get; set; }
        public bool HasTask { get; set; }
        public string TaskDetails { get; set; } = string.Empty;

        // --- ID KURSU I JEDNOSTKI USOS ---
        public string CourseId { get; set; } = string.Empty;
        public string CourseUnitId { get; set; } = string.Empty;
    }
}
