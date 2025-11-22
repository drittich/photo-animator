using System;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Event payload for playback frame changes, including drop-frame metrics and elapsed time.
/// </summary>
public sealed class FrameChangedEventArgs : EventArgs
{
    public FrameChangedEventArgs(int frameIndex, long absoluteFrameNumber, long droppedSinceLast, long droppedTotal, TimeSpan elapsed)
    {
        FrameIndex = frameIndex;
        AbsoluteFrameNumber = absoluteFrameNumber;
        DroppedSinceLast = droppedSinceLast;
        DroppedTotal = droppedTotal;
        Elapsed = elapsed;
    }

    /// <summary>
    /// Zero-based frame index in the current sequence (looping if applicable).
    /// </summary>
    public int FrameIndex { get; }

    /// <summary>
    /// Absolute frame number since playback start (monotonic, non-looping counter).
    /// </summary>
    public long AbsoluteFrameNumber { get; }

    /// <summary>
    /// Number of frames skipped since the previous published frame.
    /// </summary>
    public long DroppedSinceLast { get; }

    /// <summary>
    /// Cumulative dropped frames since playback start.
    /// </summary>
    public long DroppedTotal { get; }

    /// <summary>
    /// Elapsed wall-clock time since playback started.
    /// </summary>
    public TimeSpan Elapsed { get; }
}
