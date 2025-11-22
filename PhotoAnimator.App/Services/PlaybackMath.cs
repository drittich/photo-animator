using System;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Deterministic playback math helpers shared between runtime and tests.
/// </summary>
public static class PlaybackMath
{
    /// <summary>
    /// Calculates the ideal frame index for the given elapsed seconds, FPS, and frame count.
    /// Applies modulo arithmetic to loop and floor to the nearest whole frame (drop-frame behavior).
    /// </summary>
    public static int CalculateFrameIndex(double elapsedSeconds, int fps, int frameCount)
    {
        if (frameCount <= 0) throw new ArgumentException("Frame count must be positive.", nameof(frameCount));
        if (fps < 6 || fps > 60) throw new ArgumentOutOfRangeException(nameof(fps), "FPS must be between 6 and 60.");
        if (elapsedSeconds < 0) elapsedSeconds = 0;

        double idealFrame = elapsedSeconds * fps;
        int index = (int)(idealFrame % frameCount);
        return index;
    }
}
