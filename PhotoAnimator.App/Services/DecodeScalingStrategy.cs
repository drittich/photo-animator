using System;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Default implementation of <see cref="IDecodeScalingStrategy"/> that selects a single-axis
    /// downscale target so the image fits fully inside a viewport without ever upscaling.
    /// Prefers returning a width target; if width would not reduce size but height would, returns height.
    /// Uses an overscan factor of 1.0 (no extra margin). Returns (null,null) when no scaling required.
    /// </summary>
    public sealed class DecodeScalingStrategy : IDecodeScalingStrategy
    {
        /// <inheritdoc />
        public (int? targetWidth, int? targetHeight) GetTargetPixelsForViewport(
            int viewportWidth,
            int viewportHeight,
            int originalWidth,
            int originalHeight)
        {
            if (viewportWidth <= 0 || viewportHeight <= 0 ||
                originalWidth <= 0 || originalHeight <= 0)
            {
                return (null, null);
            }

            // If already fits wholly, no scaling.
            if (originalWidth <= viewportWidth && originalHeight <= viewportHeight)
            {
                return (null, null);
            }

            // Determine ratio needed based on the larger overflow (longest dimension relative ratio).
            double widthRatio = (double)originalWidth / viewportWidth;
            double heightRatio = (double)originalHeight / viewportHeight;
            double ratio = Math.Max(widthRatio, heightRatio);
            if (ratio <= 1.0)
            {
                return (null, null); // Should have been caught above, safety.
            }

            int scaledWidth = (int)Math.Round(originalWidth / ratio);
            int scaledHeight = (int)Math.Round(originalHeight / ratio);

            // Prefer width axis if it actually reduces size.
            if (scaledWidth < originalWidth)
            {
                return (scaledWidth, null);
            }

            // Else fall back to height if that reduces.
            if (scaledHeight < originalHeight)
            {
                return (null, scaledHeight);
            }

            // Fallback: no effective reduction.
            return (null, null);
        }
    }
}