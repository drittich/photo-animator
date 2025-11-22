using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PhotoAnimator.App.Models;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Concrete implementation of <see cref="IFolderScanner"/> that enumerates JPEG image files
/// (.jpg and .jpeg) in a single directory, sorts them alphabetically (case-insensitive) by filename
/// and produces <see cref="FrameMetadata"/> instances with a placeholder asynchronous decode function.
/// </summary>
/// <remarks>
/// Decoding here is deliberately minimal and deferred: it creates a <see cref="BitmapFrame"/> with
/// <see cref="BitmapCreateOptions.DelayCreation"/> and <see cref="BitmapCacheOption.OnDemand"/> so that
/// pixel data is not forced into memory until actually needed. The bitmap is frozen by
/// <see cref="FrameMetadata"/> upon first decode completion.
/// </remarks>
public sealed class FolderScanner : IFolderScanner
{
    /// <summary>
    /// Determines whether the provided folder path is non-empty, exists and can be enumerated.
    /// </summary>
    public bool IsValidFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;
        try
        {
            if (!Directory.Exists(folderPath))
                return false;

            // Attempt a minimal enumeration to ensure accessibility (permission).
            _ = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly).GetEnumerator().MoveNext();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Scans the specified folder for .jpg and .jpeg files (case-insensitive), sorted alphabetically by filename.
    /// </summary>
    /// <param name="folderPath">Absolute path to the folder to scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of <see cref="FrameMetadata"/> for discovered frames.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="folderPath"/> is invalid or inaccessible.</exception>
    public async Task<IReadOnlyList<FrameMetadata>> ScanAsync(string folderPath, CancellationToken ct)
    {
        if (!IsValidFolder(folderPath))
            throw new ArgumentException("Folder does not exist or is not accessible.", nameof(folderPath));

        ct.ThrowIfCancellationRequested();

        // Enumerate .jpg and .jpeg separately per requirement.
        IEnumerable<string> jpgFiles;
        IEnumerable<string> jpegFiles;
        try
        {
            jpgFiles = Directory.EnumerateFiles(folderPath, "*.jpg", SearchOption.TopDirectoryOnly);
            jpegFiles = Directory.EnumerateFiles(folderPath, "*.jpeg", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException("Failed to enumerate files in folder.", nameof(folderPath), ex);
        }

        var allFiles = jpgFiles.Concat(jpegFiles)
            .Where(f =>
                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        ct.ThrowIfCancellationRequested();

        var results = new List<FrameMetadata>(allFiles.Count);
        for (int i = 0; i < allFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = allFiles[i];

            // Placeholder async decode function; will be replaced by ImageDecodeService.
            async Task<BitmapSource> DecodeAsync(CancellationToken decodeCt)
            {
                decodeCt.ThrowIfCancellationRequested();
                await Task.Yield(); // Ensure asynchronous behavior.
                var frame = BitmapFrame.Create(
                    new Uri(filePath),
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.OnDemand);
                return (BitmapSource)frame;
            }

            var metadata = new FrameMetadata(i, filePath, DecodeAsync)
            {
                // OriginalPixelWidth / OriginalPixelHeight intentionally left unset.
            };
            results.Add(metadata);
        }

        // Keep method async for future expansion (e.g., pre-fetching dimensions).
        await Task.Yield();
        return results;
    }
}