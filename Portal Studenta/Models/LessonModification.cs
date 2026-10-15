using System;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Model modyfikacji zajęć (odwołanie, zmiana sali, przeniesienie terminu, zadanie, egzamin).
    /// </summary>
    public class LessonModification
    {
        public string LessonKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime OriginalStart { get; set; }
        public bool IsCanceled { get; set; }
        public string? CancelReason { get; set; }
        public DateTime? ModifiedStart { get; set; }
        public DateTime? ModifiedEnd { get; set; }
        public string? ModifiedPlace { get; set; }
        public bool HasExam { get; set; }
        public bool HasTask { get; set; }
        public string? TaskDetails { get; set; }
        public bool IsGroupModification { get; set; }
        public string? GroupCode { get; set; }
    }
}
