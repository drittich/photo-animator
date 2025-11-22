using System;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Strategy abstraction that determines an appropriate single-axis decode target size for an image
    /// given the viewport dimensions and the original intrinsic dimensions. The implementation must
    /// never upscale (returns nulls when the original already fits) and should prefer returning a width
    /// target (DecodePixelWidth) over a height target to preserve aspect ratio when either would work.
    /// Returning both dimensions is not supported: only one axis should be provided so WPF maintains
    /// the original aspect ratio automatically.
    /// </summary>
    public interface IDecodeScalingStrategy
    {
        /// <summary>
        /// Computes target decode pixels for an image so that it fits inside the viewport bounds without
        /// upscaling. If scaling is required, only one axis is returned (width preferred); the other axis
        /// is null. If no scaling is required both returned values are null.
        /// </summary>
        /// <param name="viewportWidth">Current viewport/client width in pixels (logical).</param>
        /// <param name="viewportHeight">Current viewport/client height in pixels (logical).</param>
        /// <param name="originalWidth">Intrinsic pixel width of the source image.</param>
        /// <param name="originalHeight">Intrinsic pixel height of the source image.</param>
        /// <returns>Tuple of (targetWidth, targetHeight) where at most one is non-null.</returns>
        (int? targetWidth, int? targetHeight) GetTargetPixelsForViewport(
            int viewportWidth,
            int viewportHeight,
            int originalWidth,
            int originalHeight);
    }
}