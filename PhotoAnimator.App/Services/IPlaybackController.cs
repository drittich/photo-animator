using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoAnimator.App.Models;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Provides elapsed-time driven playback of a sequence of frames.
    /// The controller advances frames based on a <see cref="System.Diagnostics.Stopwatch"/> rather than per-timer tick.
    /// Drop-frame logic: on each <see cref="Tick"/> the ideal frame index is computed from elapsed time (elapsedSeconds * FPS).
    /// If the newly computed index differs from the previously published index the controller raises <see cref="FrameChanged"/>.
    /// This allows the controller to skip (drop) intermediate frames automatically when the UI thread is busy,
    /// keeping playback tempo accurate while minimizing workload. Frames may loop if <see cref="LoopPlayback"/> is true.
    /// </summary>
    public interface IPlaybackController
    {
        /// <summary>
        /// Raised when the current frame index changes. Supplies the new zero-based frame index plus playback metrics.
        /// </summary>
        event EventHandler<FrameChangedEventArgs>? FrameChanged;

        /// <summary>
        /// True while playback is active.
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// The current zero-based frame index.
        /// </summary>
        int CurrentFrameIndex { get; }

        /// <summary>
        /// Target frames-per-second rate. Must be between 6 and 60 (inclusive).
        /// Changing this while playing adjusts scheduling without restarting or rewinding.
        /// </summary>
        int FramesPerSecond { get; set; }

        /// <summary>
        /// If true playback loops (wraps) when elapsed time exceeds sequence duration.
        /// If false playback stops automatically on the last frame and <see cref="IsPlaying"/> becomes false.
        /// </summary>
        bool LoopPlayback { get; set; }

        /// <summary>
        /// Starts playback over the provided frame list if not already playing.
        /// Sets the current frame to 0 immediately and raises <see cref="FrameChanged"/>.
        /// Playback timing is driven by elapsed time; the internal timer oversamples to reduce drift, and
        /// frame advancement uses drop-frame logic so that skipped UI ticks do not slow animation.
        /// </summary>
        /// <param name="frames">Ordered list of frame metadata to loop.</param>
        /// <param name="ct">Cancellation token (checked once on start).</param>
        Task StartAsync(IReadOnlyList<FrameMetadata> frames, CancellationToken ct);

        /// <summary>
        /// Stops playback (no frame change event is raised). Safe to call when not playing.
        /// </summary>
        void Stop();

        /// <summary>
        /// Sets the current frame index to 0 and raises <see cref="FrameChanged"/> without starting playback.
        /// </summary>
        void Rewind();

        /// <summary>
        /// Manual tick; computes elapsed-based ideal frame index and raises <see cref="FrameChanged"/> if it changed.
        /// Useful for tests or external driving in place of the internal timer.
        /// </summary>
        void Tick();
    }
}
