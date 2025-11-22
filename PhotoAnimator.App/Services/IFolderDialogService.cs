using System;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Provides an abstraction for selecting a folder from the UI.
/// </summary>
public interface IFolderDialogService
{
    /// <summary>
    /// Shows a folder selection dialog and returns the chosen absolute path, or null if the user cancelled.
    /// </summary>
    string? SelectFolder();
}