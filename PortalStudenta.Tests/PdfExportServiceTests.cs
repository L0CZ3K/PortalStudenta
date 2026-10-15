using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class PdfExportServiceTests
    {
        [Fact]
        public async Task GeneratePrintableHtmlAsync_ContainsAcademicHeaderAndBadges()
        {
            // Arrange
            var note = new Note
            {
                Id = Guid.NewGuid(),
                Title = "Wykład 4 - Algorytmy i Struktury Danych",
                Subject = "Algorytmy i Struktury Danych",
                LessonType = "Wykład",
                GroupName = "Grupa 1",
                Author = "Jan Kowalski (123456)",
                Content = "<p>Oto notatka z drzew AVL i rotacji.</p><div class=\"notes-code-card\"><pre><code>var tree = new AvlTree();</code></pre></div>",
                UpdatedAt = new DateTime(2026, 10, 15, 14, 30, 0)
            };

            // Act
            var html = await PdfExportService.GeneratePrintableHtmlAsync(note);

            // Assert
            Assert.NotNull(html);
            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("Wykład 4 - Algorytmy i Struktury Danych", html);
            Assert.Contains("badge-subject", html);
            Assert.Contains("Algorytmy i Struktury Danych", html);
            Assert.Contains("Wykład", html);
            Assert.Contains("Grupa 1", html);
            Assert.Contains("Jan Kowalski (123456)", html);
            Assert.Contains("15.10.2026, 14:30", html);
            Assert.Contains("notes-code-card", html);
            Assert.Contains("var tree = new AvlTree();", html);
            Assert.Contains("Portal Studenta • UniAvio", html);
            Assert.Contains("size: A4;", html);
        }

        [Fact]
        public async Task GeneratePrintableHtmlAsync_HandlesNullAndEmptyFieldsSafely()
        {
            // Arrange
            var emptyNote = new Note
            {
                Id = Guid.NewGuid(),
                Title = "",
                Subject = "",
                LessonType = "",
                GroupName = "",
                Author = "Anonim",
                Content = "",
                UpdatedAt = DateTime.UtcNow
            };

            // Act
            var html = await PdfExportService.GeneratePrintableHtmlAsync(emptyNote);

            // Assert
            Assert.NotNull(html);
            Assert.Contains("Notatka akademicka", html);
            Assert.Contains("Anonim", html);
        }

        [Fact]
        public async Task GeneratePrintableHtmlAsync_EmbedsAttachmentDataUri_WhenAttachmentExists()
        {
            // Arrange
            var tempImgPath = Path.Combine(Path.GetTempPath(), $"test_att_{Guid.NewGuid():N}.jpg");
            var dummyImageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }; // JPG signature
            await File.WriteAllBytesAsync(tempImgPath, dummyImageBytes);

            try
            {
                var attId = Guid.NewGuid().ToString("N");
                var note = new Note
                {
                    Id = Guid.NewGuid(),
                    Title = "Notatka z tablicą",
                    Author = "Student",
                    Content = $"<p>Zdjęcie poniżej:</p><img data-attachment-id=\"{attId}\" class=\"note-inline-image\">",
                    Attachments = new List<NoteAttachment>
                    {
                        new NoteAttachment
                        {
                            Id = attId,
                            FileName = "tablica.jpg",
                            ContentType = "image/jpeg",
                            LocalPath = tempImgPath
                        }
                    }
                };

                var noteService = new NoteService();

                // Act
                var html = await PdfExportService.GeneratePrintableHtmlAsync(note, noteService);

                // Assert
                Assert.NotNull(html);
                Assert.Contains($"data-attachment-id=\"{attId}\"", html);
                Assert.Contains("src=\"data:image/jpeg;base64,", html);
            }
            finally
            {
                if (File.Exists(tempImgPath)) File.Delete(tempImgPath);
            }
        }

        [Fact]
        public async Task ExportAndOpenPdfAsync_GeneratesExportFileOnDisk()
        {
            // Arrange
            var note = new Note
            {
                Id = Guid.NewGuid(),
                Title = "Eksport_Test",
                Subject = "Testowanie Oprogramowania",
                Author = "Tester",
                Content = "<p>Test eksportu do pliku.</p>",
                UpdatedAt = DateTime.Now
            };

            // Act
            var generatedPath = await PdfExportService.ExportAndOpenPdfAsync(note);

            // Assert
            Assert.NotNull(generatedPath);
            Assert.True(File.Exists(generatedPath), $"Wygenerowany plik powinien istnieć: {generatedPath}");
            var content = await File.ReadAllTextAsync(generatedPath);
            Assert.Contains("Testowanie Oprogramowania", content);
            Assert.Contains("Test eksportu do pliku.", content);

            // Cleanup
            try
            {
                if (File.Exists(generatedPath)) File.Delete(generatedPath);
            }
            catch { }
        }
    }
}
