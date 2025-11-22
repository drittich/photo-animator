using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using PhotoAnimator.App.Models;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Elapsed-time driven playback controller for looping frame sequences.
    /// Uses a <see cref="Stopwatch"/> to compute the ideal frame index from total elapsed seconds.
    /// A lightweight oversampling <see cref="DispatcherTimer"/> (default 10ms minimum interval, adjusted by FPS)
    /// calls <see cref="Tick"/>; actual frame advancement is derived from elapsed time rather than the timer tick itself.
    /// This permits automatic drop-frame behavior: when the UI thread is busy and ticks are delayed, intermediate
    /// frames are skipped and playback tempo (seconds per loop) remains accurate.
    /// The frame index loops using modulo arithmetic and never blocks the UI thread.
    /// </summary>
    public sealed class PlaybackController : IPlaybackController, IDisposable
    {
        private IReadOnlyList<FrameMetadata>? _frames;
        private readonly Dispatcher _dispatcher;
        private Stopwatch _stopwatch;
        private DispatcherTimer _timer;
        private int _currentIndex;
        private bool _isPlaying;
        private int _fps = 12;
        private bool _loopPlayback = true;
        private readonly object _sync = new();
        private long _lastPublishedFrameNumber;
        private long _droppedFrames;

        /// <summary>
        /// Construct a new controller optionally providing a target dispatcher. If null, uses <see cref="Dispatcher.CurrentDispatcher"/>.
        /// </summary>
        /// <param name="dispatcher">Dispatcher to marshal frame change events to.</param>
        public PlaybackController(Dispatcher? dispatcher = null)
        {
            _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
            _stopwatch = new Stopwatch();
            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(10) // minimal baseline, will be adjusted on start/FPS change
            };
            _timer.Tick += OnTimerTick;
        }

        /// <inheritdoc />
        public event EventHandler<FrameChangedEventArgs>? FrameChanged;

        /// <inheritdoc />
        public bool IsPlaying
        {
            get { lock (_sync) return _isPlaying; }
        }

        /// <inheritdoc />
        public int CurrentFrameIndex
        {
            get { lock (_sync) return _currentIndex; }
        }

        /// <inheritdoc />
        public int FramesPerSecond
        {
            get { lock (_sync) return _fps; }
            set
            {
                lock (_sync)
                {
                    if (value < 6 || value > 60) throw new ArgumentOutOfRangeException(nameof(value), "FPS must be between 6 and 60.");
                    if (_fps == value) return;
                    _fps = value;
                    if (_isPlaying && _timer != null)
                    {
                        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _fps / 2.0);
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool LoopPlayback
        {
            get { lock (_sync) return _loopPlayback; }
            set
            {
                lock (_sync)
                {
                    _loopPlayback = value;
                }
            }
        }

        /// <inheritdoc />
        public Task StartAsync(IReadOnlyList<FrameMetadata> frames, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled(ct);
            if (frames == null || frames.Count == 0) throw new ArgumentException("Frames collection must be non-null and contain at least one frame.", nameof(frames));

            int initialIndexToRaise = -1;
            lock (_sync)
            {
                if (_isPlaying) return Task.CompletedTask;
                _frames = frames;
                _currentIndex = 0;
                _lastPublishedFrameNumber = 0;
                _droppedFrames = 0;
                _stopwatch.Restart();
                _isPlaying = true;
                if (_timer != null)
                {
                    _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _fps / 2.0);
                    _timer.Start();
                }
                initialIndexToRaise = 0;
            }

            if (initialIndexToRaise >= 0)
            {
                RaiseFrameChanged(new FrameChangedEventArgs(initialIndexToRaise, 0, 0, 0, TimeSpan.Zero));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Stop()
        {
            lock (_sync)
            {
                if (!_isPlaying) return;
                _isPlaying = false;
                _stopwatch.Stop();
                if (_timer != null)
                {
                    _timer.Stop();
                }
            }
        }

        /// <inheritdoc />
        public void Rewind()
        {
            FrameChangedEventArgs? args = null;
            lock (_sync)
            {
                _currentIndex = 0;
                _lastPublishedFrameNumber = 0;
                _droppedFrames = 0;
                if (_stopwatch.IsRunning)
                {
                    _stopwatch.Restart();
                }
                else
                {
                    _stopwatch.Reset();
                }
                args = new FrameChangedEventArgs(0, 0, 0, 0, TimeSpan.Zero);
            }
            if (args != null) RaiseFrameChanged(args);
        }

        /// <inheritdoc />
        public void Tick()
        {
            FrameChangedEventArgs? argsToRaise = null;
            bool shouldAutoStop = false;
            lock (_sync)
            {
                if (!_isPlaying || _frames == null) return;
                TimeSpan elapsed = _stopwatch.Elapsed;
                double elapsedSeconds = elapsed.TotalSeconds;
                int frameCount = _frames.Count;
                double totalFramesExact = elapsedSeconds * _fps;
                long totalFramesFloor = (long)Math.Floor(totalFramesExact);
                long droppedSinceLast = Math.Max(0, totalFramesFloor - _lastPublishedFrameNumber - 1);

                int newIndex;
                if (_loopPlayback)
                {
                    newIndex = PlaybackMath.CalculateFrameIndex(elapsedSeconds, _fps, frameCount);
                }
                else
                {
                    // Non-looping: clamp at last frame; stop when past end.
                    if (totalFramesFloor >= frameCount)
                    {
                        newIndex = frameCount - 1;
                        shouldAutoStop = true;
                    }
                    else
                    {
                        newIndex = (int)Math.Floor(totalFramesExact);
                    }
                }

                if (newIndex != _currentIndex || droppedSinceLast > 0)
                {
                    _currentIndex = newIndex;
                    _droppedFrames += droppedSinceLast;
                    _lastPublishedFrameNumber = totalFramesFloor;
                    argsToRaise = new FrameChangedEventArgs(
                        newIndex,
                        totalFramesFloor,
                        droppedSinceLast,
                        _droppedFrames,
                        elapsed);
                }
            }

            if (shouldAutoStop)
            {
                // Perform stop outside lock to avoid deadlocks; no frame change event on stop.
                Stop();
            }

            if (argsToRaise != null)
            {
                RaiseFrameChanged(argsToRaise);
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            Tick();
        }

        /// <summary>
        /// Raises the <see cref="FrameChanged"/> event on the dispatcher thread if required.
        /// </summary>
        /// <param name="args">Frame change payload.</param>
        private void RaiseFrameChanged(FrameChangedEventArgs args)
        {
            var handler = FrameChanged;
            if (handler == null) return;

            void Raise() => handler(this, args);

            if (_dispatcher.CheckAccess())
            {
                Raise();
            }
            else
            {
                _dispatcher.InvokeAsync(Raise, DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Disposes the controller, stopping playback and detaching timer handlers.
        /// </summary>
        public void Dispose()
        {
            Stop();
            if (_timer != null)
            {
                _timer.Tick -= OnTimerTick;
                _timer = null!;
            }
            GC.SuppressFinalize(this);
        }
    }
}
