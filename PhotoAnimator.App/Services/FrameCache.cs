using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
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
        private const int MaxConcurrentDecodeCap = 4;
        private const int SoftPreloadLimit = 500;
        private const int SafeAxisDecodeLimit = 4096;

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

            int targetCount = Math.Min(frames.Count, SoftPreloadLimit);
            int parallel = Math.Max(1, Math.Min(_settings.MaxParallelDecodes, MaxConcurrentDecodeCap));
            bool heavyMode = frames.Count > SoftPreloadLimit;
            if (heavyMode)
            {
                parallel = Math.Min(parallel, 2);
            }

            int decodedCount = 0;
            long totalBytes = 0;
            bool memoryLimitReached = false;

            var semaphore = new SemaphoreSlim(parallel, parallel);
            var tasks = new List<Task>(targetCount);

            try
            {
                for (int i = 0; i < targetCount; i++)
                {
                    if (Volatile.Read(ref memoryLimitReached))
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
                            {
                                ReportProgress(ref decodedCount, progress);
                                return;
                            }
                            var fm = frames[frameIndex];
                            var cached = fm.TryGetBitmapCached();
                            if (cached != null)
                            {
                                _bitmaps.TryAdd(frameIndex, cached);
                                ReportProgress(ref decodedCount, progress);
                                return;
                            }
                            var bitmap = await DecodeFrameAsync(fm, frameIndex, ct).ConfigureAwait(false);

                            if (bitmap != null)
                            {
                                _bitmaps.TryAdd(frameIndex, bitmap);

                                // Progress update.
                                ReportProgress(ref decodedCount, progress);

                                // Memory accounting.
                                long estimate = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
                                long newTotal = Interlocked.Add(ref totalBytes, estimate);
                                if (newTotal > MemoryLimitBytes)
                                {
                                    Volatile.Write(ref memoryLimitReached, true);
                                }
                            }
                            else
                            {
                                ReportProgress(ref decodedCount, progress);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Debug.WriteLine($"[FrameCache] Decode failure for frame {frameIndex}: {ex.Message}. Skipping.");
                            ReportProgress(ref decodedCount, progress);
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
        /// Retrieves a bitmap from the cache if present, otherwise decodes it with the same safeguards as preload.
        /// </summary>
        public async Task<BitmapSource?> GetOrDecodeAsync(FrameMetadata frame, int frameIndex, CancellationToken ct)
        {
            if (frame is null) throw new ArgumentNullException(nameof(frame));
            if (_bitmaps.TryGetValue(frameIndex, out var cached))
            {
                return cached;
            }

            try
            {
                var bitmap = await DecodeFrameAsync(frame, frameIndex, ct).ConfigureAwait(false);
                if (bitmap != null)
                {
                    _bitmaps.TryAdd(frameIndex, bitmap);
                }
                return bitmap;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FrameCache] On-demand decode error for frame {frameIndex}: {ex.Message}. Skipping.");
                return null;
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
        /// Maximum number of frames eagerly preloaded before deferring to lazy decode.
        /// </summary>
        public int PreloadSoftCap => SoftPreloadLimit;

        private async Task<BitmapSource?> DecodeFrameAsync(FrameMetadata fm, int frameIndex, CancellationToken ct)
        {
            BitmapSource? bitmap = null;
            int? probedWidth = null;
            int? probedHeight = null;

            // Adaptive scaling path: probe + choose axis; ignore provided targetPixelWidth/Height here
            // (they remain for API compatibility and future external use).
            try
            {
                var (pw, ph) = await _decodeService.ProbeDimensionsAsync(fm.FilePath, ct).ConfigureAwait(false);
                probedWidth = pw;
                probedHeight = ph;
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
                var (safetyWidth, safetyHeight) = GetSafetyDecodeTarget(probedWidth, probedHeight);
                if (safetyWidth.HasValue || safetyHeight.HasValue)
                {
                    try
                    {
                        bitmap = await _decodeService
                            .DecodeAsync(fm.FilePath, safetyWidth, safetyHeight, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Debug.WriteLine($"[FrameCache] Safety decode failed for frame {frameIndex}: {ex.Message}. Trying original decode.");
                    }
                }
            }

            if (bitmap == null)
            {
                // Fallback to frame's own lazy decode (could be full-size).
                bitmap = await fm.GetBitmapAsync(ct).ConfigureAwait(false);
            }

            return bitmap != null ? EnforceMaxCacheSize(bitmap) : null;
        }

        private static void ReportProgress(ref int decodedCount, IProgress<int>? progress)
        {
            int newValue = Interlocked.Increment(ref decodedCount);
            progress?.Report(newValue);
        }

        private static (int? targetWidth, int? targetHeight) GetSafetyDecodeTarget(int? probedWidth, int? probedHeight)
        {
            if (!probedWidth.HasValue || !probedHeight.HasValue)
            {
                return (null, null);
            }

            int w = probedWidth.Value;
            int h = probedHeight.Value;
            if (w <= SafeAxisDecodeLimit && h <= SafeAxisDecodeLimit)
            {
                return (null, null);
            }

            double ratio = Math.Max((double)w / SafeAxisDecodeLimit, (double)h / SafeAxisDecodeLimit);
            if (ratio <= 1.0)
            {
                return (null, null);
            }

            int scaledWidth = (int)Math.Round(w / ratio);
            int scaledHeight = (int)Math.Round(h / ratio);
            if (scaledWidth >= scaledHeight)
            {
                return (scaledWidth, null);
            }

            return (null, scaledHeight);
        }

        /// <summary>
        /// No-op scaling strategy used by the legacy constructor to preserve previous behavior.
        /// </summary>
        private sealed class NullScalingStrategy : IDecodeScalingStrategy
        {
            public (int? targetWidth, int? targetHeight) GetTargetPixelsForViewport(int viewportWidth, int viewportHeight, int originalWidth, int originalHeight)
                => (null, null);
        }

        private static BitmapSource EnforceMaxCacheSize(BitmapSource bitmap)
        {
            if (bitmap.PixelWidth <= FallbackViewportWidth && bitmap.PixelHeight <= FallbackViewportHeight)
            {
                return bitmap;
            }

            double scale = Math.Min(
                (double)FallbackViewportWidth / bitmap.PixelWidth,
                (double)FallbackViewportHeight / bitmap.PixelHeight);

            if (scale >= 1.0)
            {
                return bitmap;
            }

            var transform = new ScaleTransform(scale, scale);
            var resized = new TransformedBitmap(bitmap, transform);
            if (resized.CanFreeze)
            {
                resized.Freeze();
            }
            return resized;
        }
    }
}
