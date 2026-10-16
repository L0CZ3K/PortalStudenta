using System;
using System.Collections.Generic;

namespace Portal_Studenta.Models
{
    public class NoteAttachment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "image/jpeg"; // image/jpeg, application/pdf
        public long SizeBytes { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public string ThumbnailPath { get; set; } = "";
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public class CloudNoteAttachmentPayload
    {
        public string Id { get; set; } = "";
        public string NoteId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "image/jpeg";
        public long SizeBytes { get; set; }
        public string Base64Data { get; set; } = "";
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public enum NoteFolderType
    {
        All,
        Pinned,
        LocalOnly,
        BetweenDevices,
        Shared,
        Uncategorized,
        Trash,
        Subject,
        LessonType,
        Group
    }

    public class NoteFolderNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "📁";
        public string? Subject { get; set; }
        public string? LessonType { get; set; }
        public string? GroupName { get; set; }
        public NoteFolderType FolderType { get; set; }
        public int NoteCount { get; set; }
        public List<NoteFolderNode> Children { get; set; } = new();
        public bool IsExpanded { get; set; } = true;
    }
}
