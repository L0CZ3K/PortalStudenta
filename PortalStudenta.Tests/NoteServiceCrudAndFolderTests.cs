using System;
using System.Linq;
using System.Threading.Tasks;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class NoteServiceCrudAndFolderTests
    {
        [Fact]
        public void AddNote_AddsNoteSuccessfully()
        {
            var service = new NoteService();
            var note = new Note
            {
                Subject = "Informatyka",
                LessonType = "Wykład",
                Title = "Wprowadzenie",
                Content = "Pierwszy wykład z algorytmów.",
                LessonDate = new DateTime(2025, 10, 1)
            };

            service.AddNote(note);

            var all = service.GetAllNotes();
            Assert.Single(all);
            Assert.Equal("Informatyka", all[0].Subject);
            Assert.Equal("Pierwszy wykład z algorytmów.", all[0].Content);
        }

        [Fact]
        public void AddNote_Throws_WhenNoteIsNull()
        {
            var service = new NoteService();
            Assert.Throws<ArgumentNullException>(() => service.AddNote(null!));
        }

        [Fact]
        public void GetNotesForLesson_Throws_WhenSubjectOrTypeIsInvalid()
        {
            var service = new NoteService();
            Assert.Throws<ArgumentException>(() => service.GetNotesForLesson("", "Wykład", DateTime.Today));
            Assert.Throws<ArgumentException>(() => service.GetNotesForLesson("   ", "Wykład", DateTime.Today));
            Assert.Throws<ArgumentException>(() => service.GetNotesForLesson("Matematyka", "", DateTime.Today));
            Assert.Throws<ArgumentException>(() => service.GetNotesForLesson("Matematyka", "   ", DateTime.Today));
        }

        [Fact]
        public void GetNotesForLesson_FiltersCorrectly_BySubjectTypeAndDate()
        {
            var service = new NoteService();
            var targetDate = new DateTime(2025, 10, 15, 10, 0, 0);

            // Pasująca notatka 1
            service.AddNote(new Note
            {
                Subject = "Fizyka",
                LessonType = "Laboratorium",
                Title = "Lab 1",
                LessonDate = new DateTime(2025, 10, 15, 8, 15, 0), // Ta sama data, inna godzina
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            });

            // Pasująca notatka 2 (nowsza)
            service.AddNote(new Note
            {
                Subject = "Fizyka",
                LessonType = "Laboratorium",
                Title = "Lab 1 Notatki końcowe",
                LessonDate = new DateTime(2025, 10, 15, 14, 30, 0),
                CreatedAt = DateTime.UtcNow
            });

            // Inny przedmiot
            service.AddNote(new Note
            {
                Subject = "Matematyka",
                LessonType = "Laboratorium",
                LessonDate = new DateTime(2025, 10, 15, 10, 0, 0)
            });

            // Inny typ
            service.AddNote(new Note
            {
                Subject = "Fizyka",
                LessonType = "Wykład",
                LessonDate = new DateTime(2025, 10, 15, 10, 0, 0)
            });

            // Inny dzień
            service.AddNote(new Note
            {
                Subject = "Fizyka",
                LessonType = "Laboratorium",
                LessonDate = new DateTime(2025, 10, 16, 10, 0, 0)
            });

            var result = service.GetNotesForLesson("Fizyka", "Laboratorium", targetDate);

            Assert.Equal(2, result.Count);
            // Sprawdzenie sortowania malejąco po CreatedAt
            Assert.Equal("Lab 1 Notatki końcowe", result[0].Title);
            Assert.Equal("Lab 1", result[1].Title);
        }

        [Fact]
        public void DeleteNote_RemovesExistingNote_AndIgnoresNonExistent()
        {
            var service = new NoteService();
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            service.AddNote(new Note { Id = id1, Subject = "S1", LessonType = "T1" });
            service.AddNote(new Note { Id = id2, Subject = "S2", LessonType = "T2" });

            Assert.Equal(2, service.GetAllNotes().Count);

            // Usunięcie istniejącej
            service.DeleteNote(id1);
            Assert.Single(service.GetAllNotes());
            Assert.Equal(id2, service.GetAllNotes()[0].Id);

            // Usunięcie nieistniejącej (brak błędu)
            service.DeleteNote(Guid.NewGuid());
            Assert.Single(service.GetAllNotes());
        }

        [Fact]
        public void GetAllNotes_ReturnsCopy_DoesNotMutateInternalListDirectly()
        {
            var service = new NoteService();
            service.AddNote(new Note { Subject = "S1", LessonType = "T1" });

            var list = service.GetAllNotes();
            list.Clear(); // czyszczenie zwróconej listy

            // Wewnętrzna lista w serwisie nadal ma 1 notatkę
            Assert.Single(service.GetAllNotes());
        }

        [Fact]
        public void FolderGrouping_CalculatesAccurateCounts()
        {
            var service = new NoteService();
            service.AddNote(new Note { Subject = "Programowanie", LessonType = "Lab" });
            service.AddNote(new Note { Subject = "Programowanie", LessonType = "Lab" });
            service.AddNote(new Note { Subject = "Programowanie", LessonType = "Wykład" });
            service.AddNote(new Note { Subject = "Bazy Danych", LessonType = "Projekt" });

            var all = service.GetAllNotes();
            var progLabCount = all.Count(n => n.Subject == "Programowanie" && n.LessonType == "Lab");
            var progWykCount = all.Count(n => n.Subject == "Programowanie" && n.LessonType == "Wykład");
            var bdCount = all.Count(n => n.Subject == "Bazy Danych");

            Assert.Equal(2, progLabCount);
            Assert.Equal(1, progWykCount);
            Assert.Equal(1, bdCount);
        }

        [Fact]
        public async Task UploadImageToCloudAsync_Throws_OnNullOrWhitespace()
        {
            var service = new NoteService();
            await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageToCloudAsync(""));
            await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageToCloudAsync("   "));
            await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageToCloudAsync(null!));
        }

        [Fact]
        public async Task UploadImageToCloudAsync_ReturnsBase64String()
        {
            var service = new NoteService();
            string mockBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

            var result = await service.UploadImageToCloudAsync(mockBase64);
            Assert.Equal(mockBase64, result);
        }

        [Fact]
        public void BuildFolderHierarchy_SingleGroup_DoesNotCreateGroupSubfolders()
        {
            var service = new NoteService();
            var lessons = new List<Lesson>
            {
                new Lesson { Title = "Analiza Matematyczna", Type = "WYK", Group = "ALL" },
                new Lesson { Title = "Analiza Matematyczna", Type = "WYK", Group = "" },
                new Lesson { Title = "Analiza Matematyczna", Type = "CW", Group = "Gr. 1" }
            };

            var hierarchy = service.BuildFolderHierarchy(lessons);
            var subjNode = hierarchy.FirstOrDefault(f => f.Subject == "Analiza Matematyczna");

            Assert.NotNull(subjNode);
            // Wykład i Ćwiczenia mają po 1 grupie -> żadna nie powinna tworzyć podfolderów grup
            var wykNode = subjNode.Children.FirstOrDefault(c => c.LessonType == "WYK");
            Assert.NotNull(wykNode);
            Assert.Empty(wykNode.Children);

            var cwNode = subjNode.Children.FirstOrDefault(c => c.LessonType == "CW");
            Assert.NotNull(cwNode);
            Assert.Empty(cwNode.Children);
        }

        [Fact]
        public void BuildFolderHierarchy_MultipleGroups_CreatesGroupSubfolders()
        {
            var service = new NoteService();
            var lessons = new List<Lesson>
            {
                new Lesson { Title = "Programowanie Obiektowe", Type = "LAB", Group = "Gr. 1" },
                new Lesson { Title = "Programowanie Obiektowe", Type = "LAB", Group = "Gr. 2" },
                new Lesson { Title = "Programowanie Obiektowe", Type = "WYK", Group = "ALL" }
            };

            var hierarchy = service.BuildFolderHierarchy(lessons);
            var subjNode = hierarchy.FirstOrDefault(f => f.Subject == "Programowanie Obiektowe");

            Assert.NotNull(subjNode);
            var labNode = subjNode.Children.FirstOrDefault(c => c.LessonType == "LAB");
            Assert.NotNull(labNode);
            // Dla laboratorium są 2 grupy -> muszą powstać podfoldery grup!
            Assert.Equal(2, labNode.Children.Count);
            Assert.Contains(labNode.Children, c => c.GroupName == "Gr. 1");
            Assert.Contains(labNode.Children, c => c.GroupName == "Gr. 2");

            // Wykład ma tylko 1 grupę -> brak podfolderów
            var wykNode = subjNode.Children.FirstOrDefault(c => c.LessonType == "WYK");
            Assert.NotNull(wykNode);
            Assert.Empty(wykNode.Children);
        }

        [Fact]
        public void BuildFolderHierarchy_CalculatesAccurateNoteCounts()
        {
            var service = new NoteService();
            var noteId = Guid.NewGuid();
            service.AddNote(new Note
            {
                Id = noteId,
                Subject = "Fizyka",
                LessonType = "LAB",
                GroupName = "Gr. 1",
                IsPinned = true,
                Title = "Sprawozdanie"
            });
            service.AddNote(new Note
            {
                Title = "Luźna notatka bez przedmiotu"
            });

            var lessons = new List<Lesson>
            {
                new Lesson { Title = "Fizyka", Type = "LAB", Group = "Gr. 1" },
                new Lesson { Title = "Fizyka", Type = "LAB", Group = "Gr. 2" }
            };

            var hierarchy = service.BuildFolderHierarchy(lessons);

            var allNode = hierarchy.First(f => f.FolderType == NoteFolderType.All);
            Assert.Equal(2, allNode.NoteCount);

            var pinnedNode = hierarchy.First(f => f.FolderType == NoteFolderType.Pinned);
            Assert.Equal(1, pinnedNode.NoteCount);

            var uncategorizedNode = hierarchy.First(f => f.FolderType == NoteFolderType.Uncategorized);
            Assert.Equal(1, uncategorizedNode.NoteCount);

            var fizykaNode = hierarchy.First(f => f.Subject == "Fizyka");
            Assert.Equal(1, fizykaNode.NoteCount);

            var labNode = fizykaNode.Children.First(c => c.LessonType == "LAB");
            Assert.Equal(1, labNode.NoteCount);

            var gr1Node = labNode.Children.First(c => c.GroupName == "Gr. 1");
            Assert.Equal(1, gr1Node.NoteCount);

            var gr2Node = labNode.Children.First(c => c.GroupName == "Gr. 2");
            Assert.Equal(0, gr2Node.NoteCount);
        }

        [Fact]
        public async Task TogglePinAsync_TogglesPinStateCorrectly()
        {
            var service = new NoteService();
            var id = Guid.NewGuid();
            service.AddNote(new Note { Id = id, Title = "Test", IsPinned = false });

            await service.TogglePinAsync(id);
            var n = service.GetAllNotes().First(x => x.Id == id);
            Assert.True(n.IsPinned);

            await service.TogglePinAsync(id);
            n = service.GetAllNotes().First(x => x.Id == id);
            Assert.False(n.IsPinned);
        }

        [Fact]
        public void Attachments_SerializesAndDeserializesJsonCorrectly()
        {
            var note = new Note();
            note.Attachments = new List<NoteAttachment>
            {
                new NoteAttachment { FileName = "tablica.jpg", ContentType = "image/jpeg", SizeBytes = 1024 }
            };

            Assert.Contains("tablica.jpg", note.AttachmentsJson);

            // Symulacja odczytu z bazy z JSON-a
            var noteFromDb = new Note
            {
                AttachmentsJson = note.AttachmentsJson
            };

            Assert.Single(noteFromDb.Attachments);
            Assert.Equal("tablica.jpg", noteFromDb.Attachments[0].FileName);
            Assert.Equal("image/jpeg", noteFromDb.Attachments[0].ContentType);
        }

        [Fact]
        public void WhiteboardImageOptimizer_FormatsBytesAccurately()
        {
            Assert.Equal("500 B", WhiteboardImageOptimizer.FormatBytes(500));
            Assert.Equal("850,0 KB", WhiteboardImageOptimizer.FormatBytes(850 * 1024).Replace(".", ","));
            Assert.Equal("1,50 MB", WhiteboardImageOptimizer.FormatBytes((long)(1.5 * 1024 * 1024)).Replace(".", ","));
        }

        [Fact]
        public async Task NoteService_AddAndDeleteAttachment_UpdatesNoteState()
        {
            var service = new NoteService();
            var note = new Note { Title = "Notatka z tablicą", Subject = "Matematyka", LessonType = "Wykład" };
            service.AddNote(note);

            var fakeContent = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            using var stream = new MemoryStream(fakeContent);

            var attachment = await service.AddAttachmentAsync(note.Id, stream, "wzory_calkowe.jpg", "image/jpeg");
            Assert.NotNull(attachment);
            Assert.Equal("wzory_calkowe.jpg", attachment.FileName);
            Assert.True(File.Exists(attachment.LocalPath));

            // Notatka powinna mieć teraz 1 załącznik
            var updatedNote = service.GetAllNotes().First(n => n.Id == note.Id);
            Assert.Single(updatedNote.Attachments);

            // Usunięcie załącznika
            var deleteResult = await service.DeleteAttachmentAsync(note.Id, attachment.Id);
            Assert.True(deleteResult);
            Assert.False(File.Exists(attachment.LocalPath));

            var finalNote = service.GetAllNotes().First(n => n.Id == note.Id);
            Assert.Empty(finalNote.Attachments);
        }

        [Fact]
        public void StripHeavyDataUris_StripsBase64FromAttachmentImage()
        {
            var rawHtml = @"<p>Tekst przed</p><p><img data-attachment-id=""att_123"" src=""data:image/jpeg;base64,AAAA1234567890"" class=""note-inline-image"" alt=""Zdjęcie"" /></p><p>Tekst po</p>";
            var result = NoteService.StripHeavyDataUris(rawHtml);

            Assert.DoesNotContain("data:image/jpeg;base64", result);
            Assert.Contains(@"data-attachment-id=""att_123""", result);
            Assert.Contains(@"class=""note-inline-image""", result);
        }

        [Fact]
        public void StripHeavyDataUris_StripsBase64WhenSrcPrecedesAttachmentId()
        {
            var rawHtml = @"<p><img src=""data:image/png;base64,BBBB987654321"" data-attachment-id=""att_456"" class=""note-inline-image"" /></p>";
            var result = NoteService.StripHeavyDataUris(rawHtml);

            Assert.DoesNotContain("data:image/png;base64", result);
            Assert.Contains(@"data-attachment-id=""att_456""", result);
        }

        [Fact]
        public void StripHeavyDataUris_PreservesNormalHtmlAndRegularImages()
        {
            var normalHtml = @"<p>Zwykły tekst <img src=""https://example.com/photo.jpg"" /> oraz <b>pogrubienie</b></p>";
            var result = NoteService.StripHeavyDataUris(normalHtml);

            Assert.Equal(normalHtml, result);
        }

        [Fact]
        public async Task SaveNoteAsync_StripsHeavyDataUris_WhenPersistingNote()
        {
            var service = new NoteService();
            var note = new Note
            {
                Title = "Test ciężkiej grafiki",
                Content = @"<p>Treść</p><img data-attachment-id=""xyz999"" src=""data:image/jpeg;base64,VERY_HEAVY_BASE64_DATA"" class=""note-inline-image"" />"
            };

            await service.SaveNoteAsync(note);

            var saved = service.GetAllNotes().First(n => n.Id == note.Id);
            Assert.DoesNotContain("VERY_HEAVY_BASE64_DATA", saved.Content);
            Assert.Contains(@"data-attachment-id=""xyz999""", saved.Content);
        }

        [Fact]
        public void SharingMode_DefaultsToBetweenDevices_AndMapsToIsSharedWithGroup()
        {
            var note = new Note();
            Assert.Equal(NoteSharingMode.BetweenDevices, note.SharingMode);
            Assert.False(note.IsSharedWithGroup);

            note.SharingMode = NoteSharingMode.SharedWithAll;
            Assert.True(note.IsSharedWithGroup);

            note.IsSharedWithGroup = false;
            Assert.Equal(NoteSharingMode.BetweenDevices, note.SharingMode);

            note.SharingMode = NoteSharingMode.LocalOnly;
            Assert.False(note.IsSharedWithGroup);

            note.IsSharedWithGroup = true;
            Assert.Equal(NoteSharingMode.SharedWithAll, note.SharingMode);
        }

        [Fact]
        public void BuildFolderHierarchy_IncludesLocalOnlyAndBetweenDevicesCounts()
        {
            var service = new NoteService();
            service.AddNote(new Note { Title = "N1", SharingMode = NoteSharingMode.LocalOnly });
            service.AddNote(new Note { Title = "N2", SharingMode = NoteSharingMode.BetweenDevices });
            service.AddNote(new Note { Title = "N3", SharingMode = NoteSharingMode.BetweenDevices });
            service.AddNote(new Note { Title = "N4", SharingMode = NoteSharingMode.SharedWithAll });

            var hierarchy = service.BuildFolderHierarchy(null);

            var localNode = hierarchy.FirstOrDefault(f => f.FolderType == NoteFolderType.LocalOnly);
            Assert.NotNull(localNode);
            Assert.Equal(1, localNode.NoteCount);

            var devicesNode = hierarchy.FirstOrDefault(f => f.FolderType == NoteFolderType.BetweenDevices);
            Assert.NotNull(devicesNode);
            Assert.Equal(2, devicesNode.NoteCount);

            var sharedNode = hierarchy.FirstOrDefault(f => f.FolderType == NoteFolderType.Shared);
            Assert.NotNull(sharedNode);
            Assert.Equal(1, sharedNode.NoteCount);
        }

        [Fact]
        public async Task SyncWithCloudAsync_MergesUserAndGroupCloudNotes_Correctly()
        {
            var service = new NoteService();
            var cloudNoteId = Guid.NewGuid();
            var groupNoteId = Guid.NewGuid();

            var mockSync = new MockFirebaseNoteSync();
            mockSync.UserNotes.Add(new Note
            {
                Id = cloudNoteId,
                Title = "Z drugiego urządzenia",
                Content = "Treść z telefonu",
                SharingMode = NoteSharingMode.BetweenDevices,
                UpdatedAt = DateTime.Now
            });

            mockSync.GroupNotes.Add(new Note
            {
                Id = groupNoteId,
                Title = "Wspólna notatka grupy",
                Content = "Wykład 1",
                SharingMode = NoteSharingMode.SharedWithAll,
                UpdatedAt = DateTime.Now
            });

            await service.SyncWithCloudAsync(mockSync, "12345", "GR_1");

            var all = service.GetAllNotes();
            Assert.Contains(all, n => n.Id == cloudNoteId && n.Title == "Z drugiego urządzenia");
            Assert.Contains(all, n => n.Id == groupNoteId && n.Title == "Wspólna notatka grupy");
        }

        [Fact]
        public async Task SyncWithCloudAsync_DoesNotResurrectDeletedNotes_AndRemovesThemLocally()
        {
            var service = new NoteService();
            var deletedNoteId = Guid.NewGuid();
            var localNote = new Note
            {
                Id = deletedNoteId,
                Title = "Notatka usunięta na telefonie",
                Content = "Powinna zniknąć z PC",
                SharingMode = NoteSharingMode.BetweenDevices,
                UpdatedAt = DateTime.Now.AddMinutes(-5)
            };
            service.AddNote(localNote);

            var mockSync = new MockFirebaseNoteSync();
            // Telefon skasował notatkę i zapisał tombstone w chmurze
            mockSync.CloudDeletedNotes.Add(deletedNoteId);

            await service.SyncWithCloudAsync(mockSync, "12345", "GR_1");

            var all = service.GetAllNotes();
            // Notatka powinna zostać usunięta lokalnie na PC
            Assert.DoesNotContain(all, n => n.Id == deletedNoteId);
            // PC NIE może ponownie wysłać skasowanej notatki do chmury (brak zmartwychwstania)
            Assert.DoesNotContain(mockSync.SyncedNotes, n => n.Id == deletedNoteId);
        }

        [Fact]
        public async Task SyncWithCloudAsync_WhenDeletedLocally_DoesNotReAddFromCloud_AndDeletesFromCloud()
        {
            var service = new NoteService();
            var noteId = Guid.NewGuid();
            var note = new Note
            {
                Id = noteId,
                Title = "Notatka do skasowania lokalnie",
                SharingMode = NoteSharingMode.BetweenDevices
            };
            service.AddNote(note);
            service.DeleteNote(noteId);

            var mockSync = new MockFirebaseNoteSync();
            // Chmura jeszcze ma starą wersję notatki
            mockSync.UserNotes.Add(note);

            await service.SyncWithCloudAsync(mockSync, "12345", "GR_1");

            var all = service.GetAllNotes();
            Assert.DoesNotContain(all, n => n.Id == noteId);
            // Powinno wywołać DeleteNoteFromCloudAsync dla wiszącej notatki
            Assert.Contains(mockSync.DeletedNotes, id => id == noteId);
        }

        private class MockFirebaseNoteSync : IFirebaseNoteSync
        {
            public List<Note> UserNotes { get; } = new();
            public List<Note> GroupNotes { get; } = new();
            public List<Note> SyncedNotes { get; } = new();
            public List<Guid> DeletedNotes { get; } = new();
            public HashSet<Guid> CloudDeletedNotes { get; } = new();
            public HashSet<Guid> GroupDeletedNotes { get; } = new();

            public Task<List<Note>> GetUserNotesFromCloudAsync(string albumNumber) => Task.FromResult(UserNotes.ToList());
            public Task SyncNoteToCloudAsync(string albumNumber, Note note)
            {
                SyncedNotes.Add(note);
                return Task.CompletedTask;
            }
            public Task DeleteNoteFromCloudAsync(string albumNumber, Guid noteId)
            {
                DeletedNotes.Add(noteId);
                return Task.CompletedTask;
            }
            public Task<HashSet<Guid>> GetDeletedNoteIdsFromCloudAsync(string albumNumber) => Task.FromResult(new HashSet<Guid>(CloudDeletedNotes));
            public Task<List<Note>> GetGroupSharedNotesAsync(string groupCode) => Task.FromResult(GroupNotes.ToList());
            public Task ShareNoteToGroupLobbyAsync(string groupCode, Note note) => Task.CompletedTask;
            public Task UnshareNoteFromGroupLobbyAsync(string groupCode, Guid noteId) => Task.CompletedTask;
            public Task<HashSet<Guid>> GetGroupDeletedNoteIdsFromCloudAsync(string groupCode) => Task.FromResult(new HashSet<Guid>(GroupDeletedNotes));
        }
    }
}
