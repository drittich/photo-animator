using System.Collections.Generic;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Provides persistence for simple application settings such as last/opened folders.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Most recently opened folder path (if any).
    /// </summary>
    string? LastFolder { get; }

    /// <summary>
    /// Recently opened folder paths ordered by recency.
    /// </summary>
    IReadOnlyList<string> RecentFolders { get; }

    /// <summary>
    /// Records a folder as the most recently used and persists it.
    /// </summary>
    /// <param name="folderPath">Absolute folder path.</param>
    void RecordFolder(string folderPath);
}
