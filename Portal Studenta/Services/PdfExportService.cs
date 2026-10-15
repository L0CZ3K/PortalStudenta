using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
#if !TEST_PROJECT
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
#endif
using Portal_Studenta.Models;

#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;
#endif

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Service generating high-quality academic PDF documents from student notes.
    /// Supports embedded whiteboard photo scans, styled syntax code blocks, checklists,
    /// and university course headers.
    /// </summary>
    public static class PdfExportService
    {
        /// <summary>
        /// Generates a PDF file from the given note, saves it to the device cache,
        /// and opens it in the system viewer / share sheet.
        /// </summary>
        /// <param name="note">The note to export.</param>
        /// <param name="noteService">NoteService used for resolving attachment local data URIs.</param>
        /// <returns>Path to the generated PDF file.</returns>
        public static async Task<string?> ExportAndOpenPdfAsync(Note note, NoteService? noteService = null)
        {
            if (note == null) return null;

            try
            {
                var safeTitle = string.IsNullOrWhiteSpace(note.Title) ? "Notatka" : note.Title;
                foreach (var invalidChar in Path.GetInvalidFileNameChars())
                {
                    safeTitle = safeTitle.Replace(invalidChar, '_');
                }
                if (safeTitle.Length > 40) safeTitle = safeTitle.Substring(0, 40);

                var fileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
#if !TEST_PROJECT
                var exportDir = Path.Combine(FileSystem.CacheDirectory, "UniAvio_Export");
#else
                var exportDir = Path.Combine(Path.GetTempPath(), "UniAvio_Export");
#endif
                if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);
                var targetPdfPath = Path.Combine(exportDir, fileName);

                var html = await GeneratePrintableHtmlAsync(note, noteService);

#if IOS || MACCATALYST
                var success = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    return RenderHtmlToPdfOnApple(html, targetPdfPath);
                });

                if (!success || !File.Exists(targetPdfPath))
                {
                    Console.WriteLine("[PdfExportService] Platform render failed, saving HTML fallback.");
                    var fallbackHtmlPath = Path.ChangeExtension(targetPdfPath, ".html");
                    await File.WriteAllTextAsync(fallbackHtmlPath, html, Encoding.UTF8);
                    targetPdfPath = fallbackHtmlPath;
                }
#else
                var htmlPath = Path.ChangeExtension(targetPdfPath, ".html");
                await File.WriteAllTextAsync(htmlPath, html, Encoding.UTF8);
                targetPdfPath = htmlPath;
#endif

#if !TEST_PROJECT
                if (File.Exists(targetPdfPath))
                {
                    var isPdf = targetPdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                    var mimeType = isPdf ? "application/pdf" : "text/html";
                    bool opened = false;
                    try
                    {
                        opened = await Launcher.Default.OpenAsync(new OpenFileRequest
                        {
                            Title = Path.GetFileName(targetPdfPath),
                            File = new ReadOnlyFile(targetPdfPath, mimeType)
                        });
                    }
                    catch (Exception launchEx)
                    {
                        Console.WriteLine($"[PdfExportService] Launcher error: {launchEx.Message}");
                    }

                    if (!opened)
                    {
                        try
                        {
                            await Share.Default.RequestAsync(new ShareFileRequest
                            {
                                Title = Path.GetFileName(targetPdfPath),
                                File = new ShareFile(targetPdfPath, mimeType)
                            });
                        }
                        catch (Exception shareEx)
                        {
                            Console.WriteLine($"[PdfExportService] Share error: {shareEx.Message}");
                        }
                    }

                    return targetPdfPath;
                }
#else
                return targetPdfPath;
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfExportService] Export error: {ex.Message}");
            }

            return null;
        }

#if IOS || MACCATALYST
        private static bool RenderHtmlToPdfOnApple(string htmlContent, string targetFilePath)
        {
            try
            {
                // Standardowy format A4: 595.2 x 841.8 punktów typograficznych
                var paperRect = new CGRect(0, 0, 595.2, 841.8);
                // Margines 36 punktów (0.5 cala) z każdej strony
                var printableRect = new CGRect(36, 36, 523.2, 769.8);

                var printFormatter = new UIMarkupTextPrintFormatter(htmlContent);
                var renderer = new ApplePdfPageRenderer(paperRect, printableRect);
                renderer.AddPrintFormatter(printFormatter, 0);

                var pdfData = new NSMutableData();
                UIGraphics.BeginPDFContext(pdfData, paperRect, null);
                renderer.PrepareForDrawingPages(new NSRange(0, renderer.NumberOfPages));

                for (int i = 0; i < renderer.NumberOfPages; i++)
                {
                    UIGraphics.BeginPDFPage();
                    renderer.DrawPage(i, paperRect);
                }

                UIGraphics.EndPDFContext();

                var fileUrl = NSUrl.FromFilename(targetFilePath);
                return pdfData.Save(fileUrl, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfExportService] RenderHtmlToPdfOnApple error: {ex.Message}");
                return false;
            }
        }

        private class ApplePdfPageRenderer : UIPrintPageRenderer
        {
            private readonly CGRect _paperRect;
            private readonly CGRect _printableRect;

            public ApplePdfPageRenderer(CGRect paperRect, CGRect printableRect)
            {
                _paperRect = paperRect;
                _printableRect = printableRect;
            }

            public override CGRect PaperRect => _paperRect;
            public override CGRect PrintableRect => _printableRect;
        }
#endif

        /// <summary>
        /// Assembles standalone, CSS-styled HTML with embedded photos and academic headers.
        /// </summary>
        public static async Task<string> GeneratePrintableHtmlAsync(Note note, NoteService? noteService = null)
        {
            var contentHtml = note.Content ?? string.Empty;

            // Wstrzyknij lokalne URI obrazów dla tagów z atrybutem data-attachment-id
            if (note.Attachments != null && note.Attachments.Count > 0 && noteService != null)
            {
                foreach (var att in note.Attachments)
                {
                    if (string.IsNullOrEmpty(att.Id)) continue;
                    var path = !string.IsNullOrEmpty(att.LocalPath) && File.Exists(att.LocalPath)
                        ? att.LocalPath
                        : att.ThumbnailPath;

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var dataUri = await NoteService.GetAttachmentDataUriAsync(path, att.ContentType);
                        if (!string.IsNullOrEmpty(dataUri))
                        {
                            var pattern = $@"<img([^>]*\bdata-attachment-id=[""']{att.Id}[""'][^>]*)>";
                            contentHtml = Regex.Replace(contentHtml, pattern, m =>
                            {
                                var tag = m.Value;
                                if (!tag.Contains("src=", StringComparison.OrdinalIgnoreCase))
                                {
                                    return tag.Insert(4, $" src=\"{dataUri}\"");
                                }
                                return Regex.Replace(tag, @"src=""[^""]*""", $"src=\"{dataUri}\"");
                            }, RegexOptions.IgnoreCase);
                        }
                    }
                }
            }

            var subjectBadge = !string.IsNullOrWhiteSpace(note.Subject)
                ? $"<span class=\"badge badge-subject\">📚 {WebUtility.HtmlEncode(note.Subject)}</span>"
                : "";

            var typeBadge = !string.IsNullOrWhiteSpace(note.LessonType)
                ? $"<span class=\"badge\">{WebUtility.HtmlEncode(note.LessonType)}</span>"
                : "";

            var groupBadge = !string.IsNullOrWhiteSpace(note.GroupName)
                ? $"<span class=\"badge\">👥 {WebUtility.HtmlEncode(note.GroupName)}</span>"
                : "";

            var titleHtml = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(note.Title) ? "Notatka akademicka" : note.Title);
            var dateStr = note.UpdatedAt.ToString("dd.MM.yyyy, HH:mm");

            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang=""pl"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>{titleHtml}</title>
    <style>
        @page {{
            size: A4;
            margin: 18mm 16mm 18mm 16mm;
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            color: #111827;
            background-color: #ffffff;
            line-height: 1.6;
            margin: 0;
            padding: 0;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
        }}
        .header {{
            border-bottom: 2px solid #2563eb;
            padding-bottom: 12px;
            margin-bottom: 22px;
        }}
        .header-top {{
            display: flex;
            justify-content: space-between;
            align-items: baseline;
            margin-bottom: 6px;
        }}
        .app-name {{
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.08em;
            color: #2563eb;
        }}
        .doc-date {{
            font-size: 12px;
            color: #6b7280;
        }}
        .title {{
            font-size: 24px;
            font-weight: 800;
            letter-spacing: -0.02em;
            margin: 4px 0 10px 0;
            color: #0f172a;
        }}
        .meta-badges {{
            display: flex;
            gap: 6px;
            flex-wrap: wrap;
            align-items: center;
        }}
        .badge {{
            display: inline-block;
            background: #f1f5f9;
            color: #475569;
            font-size: 11px;
            font-weight: 600;
            padding: 2px 8px;
            border-radius: 4px;
            border: 1px solid #e2e8f0;
        }}
        .badge-subject {{
            background: #eff6ff;
            color: #1d4ed8;
            border-color: #bfdbfe;
        }}
        .note-content {{
            font-size: 14px;
            color: #1f2937;
        }}
        .note-content p {{
            margin: 0 0 10px 0;
        }}
        .note-content h1 {{
            font-size: 20px;
            font-weight: 700;
            margin: 18px 0 8px 0;
            color: #0f172a;
            border-bottom: 1px solid #f1f5f9;
            padding-bottom: 4px;
        }}
        .note-content h2 {{
            font-size: 16px;
            font-weight: 700;
            margin: 14px 0 6px 0;
            color: #1e293b;
        }}
        .note-inline-image {{
            max-width: 100%;
            height: auto;
            border-radius: 8px;
            margin: 10px 0;
            border: 1px solid #e2e8f0;
            display: block;
            page-break-inside: avoid;
        }}
        .notes-code-card {{
            background: #0f172a;
            color: #f8fafc;
            border-radius: 8px;
            padding: 12px 16px;
            margin: 14px 0;
            font-family: ui-monospace, Menlo, Monaco, 'Courier New', monospace;
            font-size: 12px;
            line-height: 1.5;
            page-break-inside: avoid;
        }}
        .notes-code-card pre {{
            margin: 0;
            white-space: pre-wrap;
            word-break: break-word;
        }}
        .notes-code-card code {{
            font-family: inherit;
        }}
        .code-header-actions, .code-lang-select {{
            display: none !important;
        }}
        .notes-checklist-item {{
            display: flex;
            align-items: center;
            gap: 8px;
            margin: 4px 0;
            font-size: 13.5px;
        }}
        .notes-checklist-item.checked span {{
            text-decoration: line-through;
            color: #94a3b8;
        }}
        mark.notes-highlighter {{
            background-color: #fef08a;
            padding: 1px 3px;
            border-radius: 2px;
        }}
        .footer {{
            margin-top: 36px;
            padding-top: 10px;
            border-top: 1px solid #e5e7eb;
            font-size: 10px;
            color: #9ca3af;
            display: flex;
            justify-content: space-between;
        }}
    </style>
</head>
<body>
    <div class=""header"">
        <div class=""header-top"">
            <span class=""app-name"">Portal Studenta • UniAvio</span>
            <span class=""doc-date"">{dateStr}</span>
        </div>
        <h1 class=""title"">{titleHtml}</h1>
        <div class=""meta-badges"">
            {subjectBadge}
            {typeBadge}
            {groupBadge}
            <span class=""badge"">Autor: {WebUtility.HtmlEncode(note.Author)}</span>
        </div>
    </div>

    <div class=""note-content"">
        {contentHtml}
    </div>

    <div class=""footer"">
        <span>Wygenerowano z aplikacji Portal Studenta</span>
        <span>Strona 1</span>
    </div>
</body>
</html>");

            return sb.ToString();
        }
    }
}
