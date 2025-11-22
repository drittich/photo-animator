using System;
using WinForms = System.Windows.Forms;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Provides folder selection functionality using the modern CommonOpenFileDialog (Windows API Code Pack)
    /// if available via reflection. If the API Code Pack assembly is not present or any error occurs, falls
    /// back to the classic <see cref="WinForms.FolderBrowserDialog"/>. Returns the chosen absolute folder
    /// path or null when the user cancels.
    /// </summary>
    public sealed class FolderDialogService : IFolderDialogService
    {
        public string? SelectFolder()
        {
            // Attempt reflection-based use of CommonOpenFileDialog
            try
            {
                var type = Type.GetType("Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog, Microsoft.WindowsAPICodePack.Shell");
                if (type != null)
                {
                    dynamic dialog = Activator.CreateInstance(type)!;
                    dialog.IsFolderPicker = true;
                    var result = dialog.ShowDialog();
                    // Compare by string to avoid referencing enum type directly.
                    if (result != null && result.ToString() == "Ok")
                    {
                        // Requirement specifies SelectedFolder; attempt it first, fallback to FileName.
                        string? selected = null;
                        try
                        {
                            selected = dialog.SelectedFolder as string;
                        }
                        catch
                        {
                            try { selected = dialog.FileName as string; } catch { /* ignore */ }
                        }

                        if (!string.IsNullOrWhiteSpace(selected))
                        {
                            return selected;
                        }
                    }
                }
            }
            catch
            {
                // Swallow and fall through to fallback.
            }

            // Fallback to FolderBrowserDialog
            try
            {
                using var dlg = new WinForms.FolderBrowserDialog
                {
                    Description = "Select folder containing JPEG frames",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false
                };
                var result = dlg.ShowDialog();
                if (result == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    return dlg.SelectedPath;
                }
            }
            catch
            {
                // Swallow; return null.
            }

            return null;
        }
    }
}