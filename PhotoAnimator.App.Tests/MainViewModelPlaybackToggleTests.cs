using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoAnimator.App.Models;
using PhotoAnimator.App.Services;
using PhotoAnimator.App.ViewModels;
using Xunit;

namespace PhotoAnimator.App.Tests
{
    /// <summary>
    /// Tests focused on MainViewModel play / toggle behavior with a fake playback controller.
    /// </summary>
    public sealed class MainViewModelPlaybackToggleTests
    {
        private static FrameMetadata CreateFrame(int index)
        {
            // Decode function is never actually invoked in these tests; provide a trivial bitmap.
            return new FrameMetadata(index, $"frame_{index}.jpg", ct =>
            {
                BitmapSource bmp = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgr32, null);
                return Task.FromResult(bmp);
            });
        }

        private static MainViewModel CreateVm(out FakePlaybackController fakeController, int frameCount)
        {
            var folderScanner = new StubFolderScanner();
            var imageDecode = new StubImageDecodeService();
            var frameCache = new StubFrameCache();
            var concurrency = new StubConcurrencySettings();
            var settings = new StubAppSettingsService();
            fakeController = new FakePlaybackController();

            var vm = new MainViewModel(folderScanner, imageDecode, frameCache, fakeController, concurrency, settings);

            for (int i = 0; i < frameCount; i++)
            {
                vm.Frames.Add(CreateFrame(i));
            }

            return vm;
        }

        [Fact]
        public void Play_Rewinds_When_AtLastFrame_NonLoop()
        {
            var vm = CreateVm(out var controller, frameCount: 5);
            vm.LoopPlayback = false;
            vm.SetCurrentFrameIndex(vm.Frames.Count - 1); // last frame

            // Execute Play
            vm.PlayCommand.Execute(null);

            Assert.True(controller.RewindCalled);
            Assert.True(controller.StartAsyncCalled);
            Assert.Equal(0, controller.StartAsyncStartFrameIndex);
            Assert.Collection(controller.CallLog,
                entry => Assert.Equal("Rewind", entry),
                entry => Assert.Equal("Start", entry));
        }

        [Fact]
        public void Play_NoRewind_When_AtLastFrame_LoopPlayback()
        {
            var vm = CreateVm(out var controller, frameCount: 4);
            vm.LoopPlayback = true;
            vm.SetCurrentFrameIndex(vm.Frames.Count - 1);

            vm.PlayCommand.Execute(null);

            Assert.False(controller.RewindCalled);
            Assert.True(controller.StartAsyncCalled);
            Assert.Equal(vm.Frames.Count - 1, controller.StartAsyncStartFrameIndex);
        }

        [Fact]
        public void Toggle_From_Playing_Stops()
        {
            var vm = CreateVm(out var controller, frameCount: 4);
            vm.SetCurrentFrameIndex(1);
            vm.PlayCommand.Execute(null);
            Assert.True(vm.IsPlaying);
            Assert.True(controller.IsPlaying);

            vm.TogglePlayPauseCommand.Execute(null);

            Assert.True(controller.StopCalled);
            Assert.False(vm.IsPlaying);
            Assert.False(controller.IsPlaying);
        }

        [Fact]
        public void Toggle_From_Stopped_Plays()
        {
            var vm = CreateVm(out var controller, frameCount: 4);
            vm.SetCurrentFrameIndex(2);

            vm.TogglePlayPauseCommand.Execute(null);

            Assert.True(controller.StartAsyncCalled);
            Assert.Equal(2, controller.StartAsyncStartFrameIndex);
            Assert.True(vm.IsPlaying);
        }

        [Fact]
        public void Toggle_From_Stopped_AtLastFrame_NonLoop_RewindsThenPlays()
        {
            var vm = CreateVm(out var controller, frameCount: 6);
            vm.LoopPlayback = false;
            vm.SetCurrentFrameIndex(vm.Frames.Count - 1);

            vm.TogglePlayPauseCommand.Execute(null);

            Assert.True(controller.RewindCalled);
            Assert.True(controller.StartAsyncCalled);
            Assert.Equal(0, controller.StartAsyncStartFrameIndex);
            Assert.Collection(controller.CallLog,
                c => Assert.Equal("Rewind", c),
                c => Assert.Equal("Start", c));
        }

        [Fact]
        public void Toggle_Ignored_When_NoFrames()
        {
            var vm = CreateVm(out var controller, frameCount: 0);

            bool canExecute = vm.TogglePlayPauseCommand.CanExecute(null);

            Assert.False(canExecute);
            Assert.False(controller.StartAsyncCalled);
        }

        [Fact]
        public void Toggle_Ignored_During_Preloading()
        {
            var vm = CreateVm(out var controller, frameCount: 3);

            // Reflectively set _isPreloading = true
            var field = typeof(MainViewModel).GetField("_isPreloading", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(vm, true);

            // Directly query CanExecute (command lambda reads private field)
            bool canExecute = vm.TogglePlayPauseCommand.CanExecute(null);

            Assert.False(canExecute);
        }

        [Fact]
        public void SingleFrame_Play_Toggles_Correctly()
        {
            var vm = CreateVm(out var controller, frameCount: 1);
            vm.LoopPlayback = false;
            vm.SetCurrentFrameIndex(0);

            // Initial play
            vm.PlayCommand.Execute(null);
            Assert.True(vm.IsPlaying);
            Assert.True(controller.IsPlaying);

            // Simulate auto-stop by raising a frame-changed event with controller IsPlaying=false.
            controller.TestAdvanceTo(0, isPlaying: false);
            Assert.False(controller.IsPlaying);
            Assert.False(vm.IsPlaying); // ViewModel should have observed stop.

            // Toggle again (should rewind then start)
            vm.TogglePlayPauseCommand.Execute(null);
            Assert.True(controller.StartAsyncCalled);
            Assert.True(controller.RewindCalled);
            Assert.Equal(0, controller.StartAsyncStartFrameIndex);
            Assert.True(vm.IsPlaying);
        }

        /// <summary>
        /// Fake playback controller implementing minimal behavior plus call tracking.
        /// </summary>
        private sealed class FakePlaybackController : IPlaybackController
        {
            public event EventHandler<FrameChangedEventArgs>? FrameChanged;

            public bool IsPlaying { get; private set; }
            public int CurrentFrameIndex { get; private set; }
            public int FramesPerSecond { get; set; } = 12;
            public bool LoopPlayback { get; set; } = true;

            public bool RewindCalled { get; private set; }
            public bool StopCalled { get; private set; }
            public bool StartAsyncCalled { get; private set; }
            public int? StartAsyncStartFrameIndex { get; private set; }
            public List<string> CallLog { get; } = new();

            private IReadOnlyList<FrameMetadata>? _frames;

            public Task StartAsync(IReadOnlyList<FrameMetadata> frames, CancellationToken ct)
                => StartAsync(frames, 0, 0, TimeSpan.Zero, ct);

            public Task StartAsync(IReadOnlyList<FrameMetadata> frames, int startFrameIndex, long startAbsoluteFrameNumber, TimeSpan startElapsed, CancellationToken ct)
            {
                _frames = frames;
                IsPlaying = true;
                StartAsyncCalled = true;
                StartAsyncStartFrameIndex = startFrameIndex;
                CallLog.Add("Start");

                // Do not modify CurrentFrameIndex (MainViewModel sets it prior to Start).
                RaiseFrameChanged(startFrameIndex, startAbsoluteFrameNumber, 0, 0, startElapsed);
                return Task.CompletedTask;
            }

            public void Stop()
            {
                if (!IsPlaying) return;
                IsPlaying = false;
                StopCalled = true;
                CallLog.Add("Stop");
            }

            public void Rewind()
            {
                RewindCalled = true;
                CallLog.Add("Rewind");
                CurrentFrameIndex = 0;
                RaiseFrameChanged(0, 0, 0, 0, TimeSpan.Zero);
            }

            public void Tick()
            {
                // Not required for these tests.
            }

            public void TestAdvanceTo(int frameIndex, bool isPlaying)
            {
                CurrentFrameIndex = frameIndex;
                IsPlaying = isPlaying;
                RaiseFrameChanged(frameIndex, frameIndex, 0, 0, TimeSpan.FromSeconds(frameIndex / Math.Max(1, FramesPerSecond)));
            }

            private void RaiseFrameChanged(int frameIndex, long absoluteFrameNumber, long droppedSinceLast, long droppedTotal, TimeSpan elapsed)
            {
                FrameChanged?.Invoke(this, new FrameChangedEventArgs(frameIndex, absoluteFrameNumber, droppedSinceLast, droppedTotal, elapsed));
            }
        }

        #region Stub Dependencies

        private sealed class StubFolderScanner : IFolderScanner
        {
            public Task<IReadOnlyList<FrameMetadata>> ScanAsync(string folderPath, CancellationToken ct)
                => Task.FromResult<IReadOnlyList<FrameMetadata>>(Array.Empty<FrameMetadata>());

            public bool IsValidFolder(string folderPath) => true;
        }

        private sealed class StubImageDecodeService : IImageDecodeService
        {
            public Task<BitmapSource> DecodeAsync(string filePath, int? targetPixelWidth, int? targetPixelHeight, CancellationToken ct)
            {
                BitmapSource bmp = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgr32, null);
                return Task.FromResult(bmp);
            }

            public Task<(int pixelWidth, int pixelHeight)> ProbeDimensionsAsync(string filePath, CancellationToken ct)
                => Task.FromResult((1, 1));
        }

        private sealed class StubFrameCache : IFrameCache
        {
            public int PreloadSoftCap => 0;
            public void Clear() { }
            public BitmapSource? GetIfDecoded(int frameIndex) => null;
            public Task<BitmapSource?> GetOrDecodeAsync(FrameMetadata frame, int frameIndex, CancellationToken ct)
                => Task.FromResult<BitmapSource?>(null);
            public Task PreloadAsync(IReadOnlyList<FrameMetadata> frames, int? targetPixelWidth, int? targetPixelHeight, IProgress<int>? progress, CancellationToken ct)
                => Task.CompletedTask;
            public void UpdateViewportSize(int pixelWidth, int pixelHeight) { }
        }

        private sealed class StubConcurrencySettings : IConcurrencySettings
        {
            private int _max = 1;
            public int MaxParallelDecodes => _max;
            public void SetMaxParallelDecodes(int value) => _max = value;
        }

        private sealed class StubAppSettingsService : IAppSettingsService
        {
            private readonly List<string> _recent = new();
            public string? LastFolder => _recent.Count > 0 ? _recent[^1] : null;
            public IReadOnlyList<string> RecentFolders => _recent;
            public void RecordFolder(string folderPath)
            {
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    _recent.Insert(0, folderPath);
                }
            }
        }

        #endregion
    }
}