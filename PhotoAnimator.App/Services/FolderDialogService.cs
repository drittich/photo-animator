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
                    var dialogInstance = Activator.CreateInstance(type);
                    if (dialogInstance == null)
                    {
                        return null;
                    }
                    var isFolderPickerProp = type.GetProperty("IsFolderPicker");
                    isFolderPickerProp?.SetValue(dialogInstance, true);

                    var showDialogMethod = type.GetMethod("ShowDialog");
                    var result = showDialogMethod?.Invoke(dialogInstance, null);
                    // Compare by string to avoid referencing enum type directly.
                    if (result != null && result.ToString() == "Ok")
                    {
                        // Requirement specifies SelectedFolder; attempt it first, fallback to FileName.
                        string? selected = type.GetProperty("SelectedFolder")?.GetValue(dialogInstance) as string ??
                                           type.GetProperty("FileName")?.GetValue(dialogInstance) as string;

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
