using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoAnimator.App.Models;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Defines a service that scans a folder for image frames (.jpg/.jpeg) and returns metadata.
/// </summary>
public interface IFolderScanner
{
    /// <summary>
    /// Scans the specified folder for .jpg and .jpeg files (case-insensitive), sorted alphabetically by filename.
    /// </summary>
    /// <param name="folderPath">Absolute path to the folder to scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of <see cref="FrameMetadata"/> representing the discovered frames.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="folderPath"/> is invalid.</exception>
    Task<IReadOnlyList<FrameMetadata>> ScanAsync(string folderPath, CancellationToken ct);

    /// <summary>
    /// Determines whether the provided folder path is non-empty, exists and is accessible.
    /// </summary>
    /// <param name="folderPath">Absolute path to validate.</param>
    bool IsValidFolder(string folderPath);
}