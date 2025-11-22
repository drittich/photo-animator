using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Concrete implementation of <see cref="IImageDecodeService"/> providing JPEG decode and dimension probing.
/// Assumes sRGB color space; no color profile transformation is performed.
/// Scaled decode applies only a single axis with precedence for width if both supplied.
/// Returned bitmaps are frozen when possible to minimize cross-thread marshaling overhead.
/// </summary>
public sealed class ImageDecodeService : IImageDecodeService
{
    /// <summary>
    /// Decodes a JPEG image into a fully loaded <see cref="BitmapSource"/> optionally applying single-axis scaling.
    /// Width takes precedence over height if both are specified to preserve aspect ratio.
    /// The bitmap is loaded off the UI thread, assumes sRGB, then frozen (if possible).
    /// </summary>
    /// <param name="filePath">Absolute path to a .jpg or .jpeg file.</param>
    /// <param name="targetPixelWidth">Optional target width in pixels (preferred over height if both supplied).</param>
    /// <param name="targetPixelHeight">Optional target height in pixels (ignored if width supplied).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A decoded <see cref="BitmapSource"/> that is frozen if possible.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="filePath"/> is null/empty or has invalid extension.</exception>
    /// <exception cref="FileNotFoundException">Thrown if file does not exist.</exception>
    public Task<BitmapSource> DecodeAsync(string filePath, int? targetPixelWidth, int? targetPixelHeight, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateFile(filePath);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.DelayCreation;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            if (targetPixelWidth.HasValue)
            {
                bitmap.DecodePixelWidth = targetPixelWidth.Value;
            }
            else if (targetPixelHeight.HasValue)
            {
                bitmap.DecodePixelHeight = targetPixelHeight.Value;
            }

            bitmap.UriSource = new Uri(filePath);
            bitmap.EndInit();

            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return (BitmapSource)bitmap;
        }, ct);
    }

    /// <summary>
    /// Probes intrinsic pixel dimensions for a JPEG image without fully decoding pixel data.
    /// Uses delayed creation and on-demand caching; assumes sRGB.
    /// </summary>
    /// <param name="filePath">Absolute path to a .jpg or .jpeg file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of (pixelWidth, pixelHeight).</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="filePath"/> is null/empty or has invalid extension.</exception>
    /// <exception cref="FileNotFoundException">Thrown if file does not exist.</exception>
    public Task<(int pixelWidth, int pixelHeight)> ProbeDimensionsAsync(string filePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateFile(filePath);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var frame = BitmapFrame.Create(
                new Uri(filePath),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnDemand);
            return (frame.PixelWidth, frame.PixelHeight);
        }, ct);
    }

    /// <summary>
    /// Validates file path existence and JPEG extension.
    /// </summary>
    /// <param name="filePath">File path to validate.</param>
    /// <exception cref="ArgumentException">Invalid path or extension.</exception>
    /// <exception cref="FileNotFoundException">File missing.</exception>
    private static void ValidateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Image file not found.", filePath);

        var ext = Path.GetExtension(filePath);
        if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .jpg and .jpeg files are supported.", nameof(filePath));
    }
}