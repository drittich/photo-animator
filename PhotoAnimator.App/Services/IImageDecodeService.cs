using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Provides JPEG image decoding utilities with optional single-axis scaling (width preferred over height when both provided).
/// Assumes source images are in sRGB color space; no color profile correction is performed.
/// Returned <see cref="BitmapSource"/> instances are frozen when possible to minimize cross-thread overhead.
/// </summary>
public interface IImageDecodeService
{
    /// <summary>
    /// Decodes the JPEG image at <paramref name="filePath"/> into a <see cref="BitmapSource"/> optionally applying scaling
    /// by setting WPF decode pixel properties. If both <paramref name="targetPixelWidth"/> and <paramref name="targetPixelHeight"/> are supplied
    /// the width takes precedence to preserve aspect ratio. Assumes sRGB color space.
    /// The resulting bitmap is fully loaded into memory and frozen if possible.
    /// </summary>
    /// <param name="filePath">Absolute path to a .jpg or .jpeg file.</param>
    /// <param name="targetPixelWidth">Optional target width in pixels (preferred if also providing height).</param>
    /// <param name="targetPixelHeight">Optional target height in pixels (ignored if width is also provided).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A decoded, frozen (when possible) <see cref="BitmapSource"/>.</returns>
    Task<BitmapSource> DecodeAsync(string filePath, int? targetPixelWidth, int? targetPixelHeight, CancellationToken ct);

    /// <summary>
    /// Probes intrinsic pixel dimensions of the JPEG image at <paramref name="filePath"/> without forcing full pixel decode.
    /// Uses delayed creation frame loading and assumes sRGB color space.
    /// </summary>
    /// <param name="filePath">Absolute path to a .jpg or .jpeg file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple containing (<c>pixelWidth</c>, <c>pixelHeight</c>).</returns>
    Task<(int pixelWidth, int pixelHeight)> ProbeDimensionsAsync(string filePath, CancellationToken ct);
}