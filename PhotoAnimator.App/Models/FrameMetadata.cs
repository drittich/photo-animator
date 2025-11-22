using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PhotoAnimator.App.Infrastructure;

namespace PhotoAnimator.App.Models;

/// <summary>
/// Represents metadata for a single frame/image including file path, index and a lazily decoded bitmap.
/// The bitmap is decoded on first access via an externally supplied decode function and then cached.
/// </summary>
/// <remarks>
/// Decoding logic is not implemented here; supply a <see cref="Func{CancellationToken, Task{BitmapSource}}"/> to the constructor.
/// The returned <see cref="BitmapSource"/> will be frozen if possible to make it cross-thread accessible.
/// </remarks>
public sealed class FrameMetadata
{
    /// <summary>
    /// Creates a new <see cref="FrameMetadata"/> instance.
    /// </summary>
    /// <param name="index">Zero-based frame index.</param>
    /// <param name="filePath">Full path to the image file.</param>
    /// <param name="decodeFunc">Function that performs asynchronous decoding of the bitmap.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> or <paramref name="decodeFunc"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is empty or whitespace.</exception>
    public FrameMetadata(int index, string filePath, Func<CancellationToken, Task<BitmapSource>> decodeFunc)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        if (decodeFunc is null)
            throw new ArgumentNullException(nameof(decodeFunc));

        Index = index;
        FilePath = filePath;

        Bitmap = new AsyncLazy<BitmapSource>(async ct =>
        {
            var bmp = await decodeFunc(ct).ConfigureAwait(false);
            if (bmp.CanFreeze)
            {
                try
                {
                    bmp.Freeze();
                }
                catch
                {
                    // Ignored: freezing is an optimization, not required.
                }
            }
            return bmp;
        });
    }

    /// <summary>
    /// Frame index in its sequence.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Absolute file path to the source image.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Original pixel width of the decoded image (if known prior to decoding).
    /// </summary>
    public int? OriginalPixelWidth { get; init; }

    /// <summary>
    /// Original pixel height of the decoded image (if known prior to decoding).
    /// </summary>
    public int? OriginalPixelHeight { get; init; }

    /// <summary>
    /// Lazily decoded bitmap for this frame. Decoding occurs on first call to <see cref="GetBitmapAsync"/>.
    /// </summary>
    public AsyncLazy<BitmapSource> Bitmap { get; }

    /// <summary>
    /// Returns the cached bitmap if it has already been decoded successfully; otherwise returns null
    /// without initiating a decode.
    /// </summary>
    public BitmapSource? TryGetBitmapCached()
    {
        var task = Bitmap.GetIfCreated();
        if (task is not null && task.IsCompletedSuccessfully)
        {
            return task.Result;
        }
        return null;
    }

    /// <summary>
    /// Asynchronously obtains the bitmap, triggering decoding if it has not yet occurred.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token passed to the decode function.</param>
    public Task<BitmapSource> GetBitmapAsync(CancellationToken cancellationToken) =>
        Bitmap.GetValueAsync(cancellationToken);
}