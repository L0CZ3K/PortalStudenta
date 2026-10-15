using System;
using System.Collections.Generic;
using System.Text.Json;
using SQLite;

namespace Portal_Studenta.Models
{
    public enum NoteSharingMode
    {
        LocalOnly = 0,        // 🔒 Lokalnie (tylko na tym urządzeniu)
        BetweenDevices = 1,   // 📱💻 Między moimi urządzeniami (prywatna chmura mojego konta)
        SharedWithAll = 2     // 👥 Udostępnij wszystkim (w grupie studenckiej)
    }

    public class Note
    {
        [PrimaryKey]
        public Guid Id { get; set; } = Guid.NewGuid();

        // Lokalizacja w folderach akademickich
        [Indexed]
        public string Subject { get; set; } = string.Empty;
        public string LessonType { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public DateTime LessonDate { get; set; }

        // Zawartość notatki
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        // Metadane
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string Author { get; set; } = "Ja";

        // Flagi i integracja z chmurą / grupą
        public bool IsPinned { get; set; }

        [Indexed]
        public NoteSharingMode SharingMode { get; set; } = NoteSharingMode.BetweenDevices;

        public bool IsSharedWithGroup
        {
            get => SharingMode == NoteSharingMode.SharedWithAll;
            set
            {
                if (value) SharingMode = NoteSharingMode.SharedWithAll;
                else if (SharingMode == NoteSharingMode.SharedWithAll) SharingMode = NoteSharingMode.BetweenDevices;
            }
        }

        public string SharedGroupCode { get; set; } = string.Empty;
        public string CourseId { get; set; } = string.Empty;

        // Serializowane załączniki do SQLite
        public string AttachmentsJson { get; set; } = "[]";

        [Ignore]
        public List<NoteAttachment> Attachments
        {
            get
            {
                if (string.IsNullOrWhiteSpace(AttachmentsJson) || AttachmentsJson == "[]")
                    return _cachedAttachments ??= new List<NoteAttachment>();
                if (_cachedAttachments == null)
                {
                    try
                    {
                        _cachedAttachments = JsonSerializer.Deserialize<List<NoteAttachment>>(AttachmentsJson) ?? new List<NoteAttachment>();
                    }
                    catch
                    {
                        _cachedAttachments = new List<NoteAttachment>();
                    }
                }
                return _cachedAttachments;
            }
            set
            {
                _cachedAttachments = value ?? new List<NoteAttachment>();
                try
                {
                    AttachmentsJson = JsonSerializer.Serialize(_cachedAttachments);
                }
                catch
                {
                    AttachmentsJson = "[]";
                }
            }
        }

        private List<NoteAttachment>? _cachedAttachments;
    }
}
