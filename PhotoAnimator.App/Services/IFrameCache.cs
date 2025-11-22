using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PhotoAnimator.App.Models;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Provides an asynchronous preload cache for decoded <see cref="BitmapSource"/> frames.
    /// Preloads lazily decoded frames (sRGB assumption indirect via underlying decode service).
    /// Concurrency is governed by a snapshot of <see cref="IConcurrencySettings.MaxParallelDecodes"/> taken
    /// at the start of <see cref="PreloadAsync"/>; subsequent changes to settings do not affect the
    /// running preload operation.
    /// Progress reports the total number of frames decoded so far (0..frames.Count).
    /// Cancellation is cooperative via <see cref="CancellationToken"/>.
    /// </summary>
    public interface IFrameCache
    {
        /// <summary>
        /// Preloads (decodes) the provided frame metadata collection into an in-memory cache.
        /// Each frame's lazy bitmap decode is triggered and stored if successful.
        /// The <paramref name="targetPixelWidth"/> and <paramref name="targetPixelHeight"/> are
        /// accepted for future scaling support (current implementation may ignore them).
        /// Concurrency is limited by the captured value of <see cref="IConcurrencySettings.MaxParallelDecodes"/>
        /// at method entry. Progress, if supplied, reports the count of frames decoded so far
        /// (monotonically increasing). Cancellation is honored before starting each decode and
        /// within the decode operation.
        /// </summary>
        /// <param name="frames">Ordered collection of frame metadata to preload.</param>
        /// <param name="targetPixelWidth">Optional target pixel width for scaling (may be ignored currently).</param>
        /// <param name="targetPixelHeight">Optional target pixel height for scaling (may be ignored currently).</param>
        /// <param name="progress">Optional progress reporter of decoded frame count.</param>
        /// <param name="ct">Cancellation token for cooperative cancellation.</param>
        Task PreloadAsync(
            IReadOnlyList<FrameMetadata> frames,
            int? targetPixelWidth,
            int? targetPixelHeight,
            IProgress<int>? progress,
            CancellationToken ct);

        /// <summary>
        /// Retrieves a decoded bitmap if present in the cache for the given frame index.
        /// Returns <c>null</c> if the frame has not yet been decoded or cached.
        /// </summary>
        /// <param name="frameIndex">Zero-based index within the last preloaded frame list.</param>
        /// <returns>The cached <see cref="BitmapSource"/> or <c>null</c>.</returns>
        BitmapSource? GetIfDecoded(int frameIndex);

        /// <summary>
        /// Clears all cached decoded bitmaps, releasing their memory. Subsequent <see cref="GetIfDecoded(int)"/>
        /// calls will return <c>null</c> until <see cref="PreloadAsync"/> is invoked again.
        /// </summary>
        void Clear();
    }
}