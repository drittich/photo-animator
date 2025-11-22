using PhotoAnimator.App.Services;
using Xunit;

namespace PhotoAnimator.App.Tests;

public class PlaybackMathTests
{
    [Fact]
    public void CalculateFrameIndex_DropsFractionalFrames()
    {
        // 0.25s at 20 FPS => ideal frame 5.0 (floor to 5)
        int index = PlaybackMath.CalculateFrameIndex(0.25, 20, 10);
        Assert.Equal(5, index);
    }

    [Fact]
    public void CalculateFrameIndex_LoopsWhenElapsedExceedsSequence()
    {
        // 10 seconds at 12 FPS on 7 frames => 120 % 7 = 1
        int index = PlaybackMath.CalculateFrameIndex(10, 12, 7);
        Assert.Equal(1, index);
    }

    [Fact]
    public void CalculateFrameIndex_SameElapsedDifferentFpsProducesDifferentIndex()
    {
        var slowIndex = PlaybackMath.CalculateFrameIndex(1.0, 6, 8);  // 6 % 8 = 6
        var fastIndex = PlaybackMath.CalculateFrameIndex(1.0, 24, 8); // 24 % 8 = 0 (looped twice)

        Assert.Equal(6, slowIndex);
        Assert.Equal(0, fastIndex);
    }

    [Fact]
    public void CalculateFrameIndex_AllowsHigherFps()
    {
        var index = PlaybackMath.CalculateFrameIndex(1.0, 60, 12); // 60 % 12 = 0
        Assert.Equal(0, index);
    }
}
