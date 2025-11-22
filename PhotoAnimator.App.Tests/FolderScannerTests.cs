using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoAnimator.App.Services;
using Xunit;

namespace PhotoAnimator.App.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"photoanim_{Guid.NewGuid():N}");

    public FolderScannerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ScanAsync_SortsAlphabeticallyCaseInsensitive()
    {
        var files = new[]
        {
            Path.Combine(_tempDir, "b.JPG"),
            Path.Combine(_tempDir, "a.jpeg"),
            Path.Combine(_tempDir, "c.jpg")
        };
        foreach (var file in files)
        {
            await File.WriteAllTextAsync(file, "test");
        }

        var scanner = new FolderScanner();
        var frames = await scanner.ScanAsync(_tempDir, CancellationToken.None);

        var names = frames.Select(f => Path.GetFileName(f.FilePath)).ToArray();
        Assert.Equal(new[] { "a.jpeg", "b.JPG", "c.jpg" }, names);
        Assert.True(frames.Select(f => f.Index).SequenceEqual(new[] { 0, 1, 2 }));
    }

    [Fact]
    public async Task ScanAsync_IgnoresNonJpegFiles()
    {
        var files = new[]
        {
            Path.Combine(_tempDir, "frame1.jpg"),
            Path.Combine(_tempDir, "frame2.jpeg"),
            Path.Combine(_tempDir, "not-image.txt")
        };
        foreach (var file in files)
        {
            await File.WriteAllTextAsync(file, "test");
        }

        var scanner = new FolderScanner();
        var frames = await scanner.ScanAsync(_tempDir, CancellationToken.None);

        var names = frames.Select(f => Path.GetFileName(f.FilePath)).ToArray();
        Assert.Equal(new[] { "frame1.jpg", "frame2.jpeg" }, names);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Swallow cleanup issues in tests.
        }
    }
}
