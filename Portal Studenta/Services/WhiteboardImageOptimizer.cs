using System;
using System.IO;
using System.Threading.Tasks;
#if ANDROID || IOS || MACCATALYST || WINDOWS
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.ApplicationModel;
#endif

namespace Portal_Studenta.Services
{
    /// <summary>
    /// High-performance hardware-accelerated image optimizer for university whiteboard photos.
    /// Resizes multi-megapixel photos to an optimal balance of clarity and file size (1800px / ~200KB),
    /// generates crisp thumbnails, and avoids UI freezes.
    /// </summary>
    public static class WhiteboardImageOptimizer
    {
        /// <summary>
        /// Maximum resolution (pixels) for the longest side of a whiteboard image.
        /// Retains pin-sharp readability of chalkboard/whiteboard equations while keeping size under 250 KB.
        /// </summary>
        public const int MaxWhiteboardResolution = 1800;

        /// <summary>
        /// JPEG compression quality ratio (0.0 - 1.0).
        /// </summary>
        public const float CompressionQuality = 0.78f;

        /// <summary>
        /// Optimizes an incoming photo stream and persists the compressed main image and its thumbnail to disk.
        /// </summary>
        /// <param name="inputPhotoStream">Raw photo input stream.</param>
        /// <param name="targetFilePath">Path where the optimized main image should be written.</param>
        /// <param name="thumbnailFilePath">Path where the thumbnail image should be written.</param>
        /// <param name="maxResolution">Longest edge limit.</param>
        /// <param name="quality">JPEG compression quality.</param>
        /// <returns>Tuple of main image file size and thumbnail file size in bytes.</returns>
        public static async Task<(long fileSize, long thumbSize)> OptimizeAndSaveAsync(
            Stream inputPhotoStream, 
            string targetFilePath, 
            string thumbnailFilePath, 
            int maxResolution = MaxWhiteboardResolution, 
            float quality = CompressionQuality)
        {
            var dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var thumbDir = Path.GetDirectoryName(thumbnailFilePath);
            if (!string.IsNullOrEmpty(thumbDir) && !Directory.Exists(thumbDir))
            {
                Directory.CreateDirectory(thumbDir);
            }

            // Skopiuj wejściowy strumień do bufora pamięci, aby uniknąć problemów z blokowaniem strumienia
            using var memoryStream = new MemoryStream();
            await inputPhotoStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

#if IOS || MACCATALYST
            // Na platformach Apple operacje CoreGraphics/UIKit (np. UIImage, UIGraphics, PlatformImage)
            // muszą być wywoływane na wątku głównym (MainThread), aby uniknąć wyjątku UIKit Consistency error.
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                return await ProcessPlatformStreamAsync(memoryStream, targetFilePath, thumbnailFilePath, maxResolution, quality);
            });
#elif ANDROID || WINDOWS
            return await Task.Run(async () =>
            {
                return await ProcessPlatformStreamAsync(memoryStream, targetFilePath, thumbnailFilePath, maxResolution, quality);
            });
#else
            return await Task.Run(async () =>
            {
                return await FallbackCopyAsync(memoryStream, targetFilePath, thumbnailFilePath);
            });
#endif
        }

#if ANDROID || IOS || MACCATALYST || WINDOWS
        private static async Task<(long fileSize, long thumbSize)> ProcessPlatformStreamAsync(
            MemoryStream memoryStream,
            string targetFilePath,
            string thumbnailFilePath,
            int maxResolution,
            float quality)
        {
            try
            {
                memoryStream.Position = 0;
                using var image = PlatformImage.FromStream(memoryStream);
                if (image != null)
                {
                    var targetWidth = image.Width;
                    var targetHeight = image.Height;
                    var maxSide = Math.Max(targetWidth, targetHeight);

                    Microsoft.Maui.Graphics.IImage optimizedImage = image;
                    if (maxSide > maxResolution)
                    {
                        optimizedImage = image.Downsize((float)maxResolution);
                    }

                    try
                    {
                        using (var outFs = File.Create(targetFilePath))
                        {
                            optimizedImage.Save(outFs, ImageFormat.Jpeg, quality);
                        }
                    }
                    finally
                    {
                        if (optimizedImage != image && optimizedImage is IDisposable disp)
                        {
                            disp.Dispose();
                        }
                    }

                    // Miniatura (maks 280px)
                    using var thumbImage = image.Downsize(280f);
                    using (var thumbFs = File.Create(thumbnailFilePath))
                    {
                        thumbImage.Save(thumbFs, ImageFormat.Jpeg, 0.70f);
                    }

                    var mainInfo = new FileInfo(targetFilePath);
                    var thumbInfo = new FileInfo(thumbnailFilePath);
                    return (mainInfo.Length, thumbInfo.Length);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WhiteboardImageOptimizer] Platform error, falling back to raw copy: {ex.Message}");
            }

            return await FallbackCopyAsync(memoryStream, targetFilePath, thumbnailFilePath);
        }
#endif

        private static async Task<(long fileSize, long thumbSize)> FallbackCopyAsync(
            MemoryStream memoryStream,
            string targetFilePath,
            string thumbnailFilePath)
        {
            memoryStream.Position = 0;
            using (var outFs = File.Create(targetFilePath))
            {
                await memoryStream.CopyToAsync(outFs);
            }
            File.Copy(targetFilePath, thumbnailFilePath, true);

            var fallbackInfo = new FileInfo(targetFilePath);
            return (fallbackInfo.Length, fallbackInfo.Length);
        }

        /// <summary>
        /// Formats raw byte count into human-readable B, KB, or MB string.
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}
