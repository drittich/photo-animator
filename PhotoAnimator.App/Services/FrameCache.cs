using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PhotoAnimator.App.Models;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Implementation of <see cref="IFrameCache"/> providing optional adaptive scaling during preload and
    /// memory / concurrency safeguards for very large frame sequences. When constructed with a scaling strategy
    /// and decode service it will probe intrinsic dimensions and perform a scaled decode (single-axis) prior to
    /// the lazy full-size decode, reducing peak memory pressure. Existing behavior preserved when using the
    /// legacy constructor.
    /// </summary>
    public sealed class FrameCache : IFrameCache
    {
        private readonly ConcurrentDictionary<int, BitmapSource> _bitmaps = new();
        private readonly IConcurrencySettings _settings;
        private readonly IDecodeScalingStrategy _scalingStrategy;
        private readonly IImageDecodeService _decodeService;

        private const long MemoryLimitBytes = 500L * 1024 * 1024; // 500 MB soft cap.
        private const int FallbackViewportWidth = 1920;
        private const int FallbackViewportHeight = 1080;

        /// <summary>
        /// Legacy constructor preserved for backward compatibility. Uses a no-op scaling strategy and
        /// internally creates a default <see cref="ImageDecodeService"/> instance.
        /// </summary>
        public FrameCache(IConcurrencySettings concurrencySettings)
            : this(
                  concurrencySettings,
                  new NullScalingStrategy(),
                  new ImageDecodeService())
        {
        }

        /// <summary>
        /// New constructor enabling adaptive scaling and custom decode service injection.
        /// </summary>
        public FrameCache(
            IConcurrencySettings concurrencySettings,
            IDecodeScalingStrategy scalingStrategy,
            IImageDecodeService decodeService)
        {
            _settings = concurrencySettings ?? throw new ArgumentNullException(nameof(concurrencySettings));
            _scalingStrategy = scalingStrategy ?? throw new ArgumentNullException(nameof(scalingStrategy));
            _decodeService = decodeService ?? throw new ArgumentNullException(nameof(decodeService));
        }

        /// <summary>
        /// Preloads frames into the cache with adaptive scaling (when strategy returns target axis) and
        /// memory + concurrency safeguards:
        ///  - If frames.Count > 500: heavyMode limits parallel decodes to at most 2.
        ///  - After each successful decode, estimated memory (width*height*4) is added; once total exceeds
        ///    500 MB, remaining frames are not scheduled (lazy decode will still occur later if needed).
        /// </summary>
        public async Task PreloadAsync(
            IReadOnlyList<FrameMetadata> frames,
            int? targetPixelWidth,
            int? targetPixelHeight,
            IProgress<int>? progress,
            CancellationToken ct)
        {
            if (frames is null) throw new ArgumentNullException(nameof(frames));
            if (frames.Count == 0) return;

            int parallel = Math.Max(1, _settings.MaxParallelDecodes);
            bool heavyMode = frames.Count > 500;
            if (heavyMode)
            {
                parallel = Math.Min(parallel, 2);
            }

            int decodedCount = 0;
            long totalBytes = 0;
            bool memoryLimitReached = false;

            var semaphore = new SemaphoreSlim(parallel, parallel);
            var tasks = new List<Task>(frames.Count);

            try
            {
                for (int i = 0; i < frames.Count; i++)
                {
                    if (memoryLimitReached)
                    {
                        // Stop scheduling new decodes; leave remaining frames for lazy decoding later.
                        Debug.WriteLine("[FrameCache] Memory soft cap reached; halting further preload scheduling.");
                        break;
                    }

                    int frameIndex = i;
                    ct.ThrowIfCancellationRequested();

                    tasks.Add(Task.Run(async () =>
                    {
                        ct.ThrowIfCancellationRequested();
                        await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            ct.ThrowIfCancellationRequested();

                            // Skip if already decoded/cached.
                            if (_bitmaps.ContainsKey(frameIndex))
                                return;
                            var fm = frames[frameIndex];
                            var cached = fm.TryGetBitmapCached();
                            if (cached != null)
                            {
                                _bitmaps.TryAdd(frameIndex, cached);
                                int newValCached = Interlocked.Increment(ref decodedCount);
                                progress?.Report(newValCached);
                                return;
                            }

                            BitmapSource? bitmap = null;

                            // Adaptive scaling path: probe + choose axis; ignore provided targetPixelWidth/Height here
                            // (they remain for API compatibility and future external use).
                            try
                            {
                                var (pw, ph) = await _decodeService.ProbeDimensionsAsync(fm.FilePath, ct).ConfigureAwait(false);
                                var (targetW, targetH) = _scalingStrategy.GetTargetPixelsForViewport(
                                    FallbackViewportWidth,
                                    FallbackViewportHeight,
                                    pw,
                                    ph);

                                if (targetW.HasValue || targetH.HasValue)
                                {
                                    bitmap = await _decodeService
                                        .DecodeAsync(fm.FilePath, targetW, targetH, ct)
                                        .ConfigureAwait(false);
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                // Probe failure or scaled decode failure: fallback to lazy full decode.
                                Debug.WriteLine($"[FrameCache] Probe/scaled decode error for frame {frameIndex}: {ex.Message}. Falling back.");
                            }

                            if (bitmap == null)
                            {
                                // Fallback to frame's own lazy decode (could be full-size).
                                bitmap = await fm.GetBitmapAsync(ct).ConfigureAwait(false);
                            }

                            if (bitmap != null)
                            {
                                _bitmaps.TryAdd(frameIndex, bitmap);

                                // Progress update.
                                int newValue = Interlocked.Increment(ref decodedCount);
                                progress?.Report(newValue);

                                // Memory accounting.
                                long estimate = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
                                long newTotal = Interlocked.Add(ref totalBytes, estimate);
                                if (newTotal > MemoryLimitBytes)
                                {
                                    memoryLimitReached = true;
                                }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, ct));
                }

                // Await only the tasks that were scheduled.
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Dispose();
            }
        }

        /// <summary>
        /// Retrieves a bitmap for the specified frame index if already decoded and cached.
        /// </summary>
        public BitmapSource? GetIfDecoded(int frameIndex) =>
            _bitmaps.TryGetValue(frameIndex, out var bmp) ? bmp : null;

        /// <summary>
        /// Clears all cached decoded bitmaps, releasing their memory.
        /// </summary>
        public void Clear() => _bitmaps.Clear();

        /// <summary>
        /// No-op scaling strategy used by the legacy constructor to preserve previous behavior.
        /// </summary>
        private sealed class NullScalingStrategy : IDecodeScalingStrategy
        {
            public (int? targetWidth, int? targetHeight) GetTargetPixelsForViewport(int viewportWidth, int viewportHeight, int originalWidth, int originalHeight)
                => (null, null);
        }
    }
}