using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PhotoAnimator.App.Models;
using PhotoAnimator.App.Services;
using PhotoAnimator.App.Commands;

namespace PhotoAnimator.App.ViewModels
{
    /// <summary>
    /// Main application ViewModel coordinating folder scanning, frame preloading, playback control,
    /// FPS selection, scrubbing, and concurrency adjustments. It exposes an observable collection of
    /// <see cref="FrameMetadata"/> plus playback-related state and command surfaces for the UI.
    /// Asynchronous loading consists of scanning the selected folder for JPEG frames and then preloading
    /// decoded bitmaps via <see cref="IFrameCache.PreloadAsync"/> with progress reporting.
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly IFolderScanner _folderScanner;
        private readonly IImageDecodeService _imageDecodeService; // Reserved for future decode customization.
        private readonly IFrameCache _frameCache;
        private readonly IPlaybackController _playbackController;
        private readonly IConcurrencySettings _concurrencySettings;

        private ObservableCollection<FrameMetadata> _frames;
        private int _currentFrameIndex;
        private bool _isPlaying;
        private int _selectedFps = 12;
        private string? _folderPath;
        private bool _isPreloading;
        private int _preloadCount;
        private int _preloadTotal;
        private CancellationTokenSource? _loadCts;
        private int _frameCount;
        private readonly IAppSettingsService _settingsService;
        private readonly ObservableCollection<RecentFolderEntry> _recentFolders;
        private bool _isTrayOpen = true;
        private bool _isHelpVisible;
        private long _droppedFrames;
        private TimeSpan _elapsed = TimeSpan.Zero;
        private string _elapsedDisplay = "00:00:00";
        private long _lastAbsoluteFrameNumber;

        // Commands
        private readonly RelayCommand _playCommand;
        private readonly RelayCommand _stopCommand;
        private readonly RelayCommand _rewindCommand;
        private readonly RelayCommand _reloadCommand;
        private readonly RelayCommand _openFolderCommand;
        private readonly IntParameterCommand _scrubCommand;

        /// <summary>
        /// Initializes a new instance of <see cref="MainViewModel"/>.
        /// </summary>
        public MainViewModel(
            IFolderScanner folderScanner,
            IImageDecodeService imageDecodeService,
            IFrameCache frameCache,
            IPlaybackController playbackController,
            IConcurrencySettings concurrencySettings,
            IAppSettingsService settingsService)
        {
            _folderScanner = folderScanner ?? throw new ArgumentNullException(nameof(folderScanner));
            _imageDecodeService = imageDecodeService ?? throw new ArgumentNullException(nameof(imageDecodeService));
            _frameCache = frameCache ?? throw new ArgumentNullException(nameof(frameCache));
            _playbackController = playbackController ?? throw new ArgumentNullException(nameof(playbackController));
            _concurrencySettings = concurrencySettings ?? throw new ArgumentNullException(nameof(concurrencySettings));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            _frames = new ObservableCollection<FrameMetadata>();
            _recentFolders = new ObservableCollection<RecentFolderEntry>(
                (_settingsService.RecentFolders ?? Array.Empty<string>())
                    .Take(6)
                    .Select(path => new RecentFolderEntry(path, CountImagesInFolder(path))));
            _isTrayOpen = _recentFolders.Count > 0;
            UpdateElapsedDisplay(TimeSpan.Zero, 0, 0);

            // Initialize commands.
            _playCommand = new RelayCommand(Play, () => !_isPlaying && _frames.Count > 0 && !_isPreloading);
            _stopCommand = new RelayCommand(Stop, () => _isPlaying);
            _rewindCommand = new RelayCommand(Rewind, () => _frames.Count > 0);
            _reloadCommand = new RelayCommand(() => { if (_folderPath != null) _ = ReloadAsync(); }, () => _folderPath != null && !_isPreloading);
            _openFolderCommand = new RelayCommand(() => { /* Placeholder, actual folder path must be set via FolderPath then Load */ }, () => !_isPreloading);
            _scrubCommand = new IntParameterCommand(Scrub, () => _frames.Count > 0);
        }

        /// <summary>
        /// Observable collection of frame metadata representing the currently loaded sequence.
        /// </summary>
        public ObservableCollection<FrameMetadata> Frames => _frames;

        /// <summary>
        /// Recently opened folders (most recent first).
        /// </summary>
        public ObservableCollection<RecentFolderEntry> RecentFolders => _recentFolders;

        /// <summary>
        /// True when the recent-folders tray is expanded.
        /// </summary>
        public bool IsTrayOpen
        {
            get => _isTrayOpen;
            set
            {
                if (_isTrayOpen == value) return;
                _isTrayOpen = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Controls visibility of the help overlay.
        /// </summary>
        public bool IsHelpVisible
        {
            get => _isHelpVisible;
            set
            {
                if (_isHelpVisible == value) return;
                _isHelpVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Cumulative dropped frames for the current playback session.
        /// </summary>
        public long DroppedFrames
        {
            get => _droppedFrames;
            private set
            {
                if (_droppedFrames == value) return;
                _droppedFrames = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Elapsed playback time display formatted as mm:ss:ff.
        /// </summary>
        public string ElapsedTimeDisplay
        {
            get => _elapsedDisplay;
            private set
            {
                if (_elapsedDisplay == value) return;
                _elapsedDisplay = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// True when user interaction should be enabled (disabled during preload).
        /// </summary>
        public bool IsInteractionEnabled => !_isPreloading;

        /// <summary>
        /// Last folder persisted in settings, used to seed the folder picker.
        /// </summary>
        public string? LastOpenedFolder => _settingsService.LastFolder;

        /// <summary>
        /// Zero-based index of the current frame. Updated during playback and scrubbing.
        /// </summary>
        public int CurrentFrameIndex
        {
            get => _currentFrameIndex;
            private set
            {
                if (_currentFrameIndex == value) return;
                _currentFrameIndex = value;
                OnPropertyChanged();
                if (!_isPlaying && _selectedFps > 0)
                {
                    var elapsed = TimeSpan.FromSeconds(_currentFrameIndex / (double)_selectedFps);
                    UpdateElapsedDisplay(elapsed, null, _currentFrameIndex);
                }
            }
        }

        /// <summary>
        /// Target frames-per-second for playback (6-60 inclusive). Changing this while playing
        /// updates <see cref="IPlaybackController.FramesPerSecond"/> without restarting playback.
        /// </summary>
        public int SelectedFps
        {
            get => _selectedFps;
            set
            {
                if (_selectedFps == value) return;
                if (value < 6 || value > 60) throw new ArgumentOutOfRangeException(nameof(value), "FPS must be between 6 and 60.");
                _selectedFps = value;
                OnPropertyChanged();
                UpdateElapsedDisplay(_elapsed, _lastAbsoluteFrameNumber, _currentFrameIndex);
                if (IsPlaying)
                {
                    _playbackController.FramesPerSecond = _selectedFps;
                }
            }
        }

        /// <summary>
        /// When true playback loops (wraps) to the first frame after the last; when false playback stops automatically on the last frame.
        /// Proxies <see cref="IPlaybackController.LoopPlayback"/>.
        /// </summary>
        public bool LoopPlayback
        {
            get => _playbackController.LoopPlayback;
            set
            {
                if (_playbackController.LoopPlayback == value) return;
                _playbackController.LoopPlayback = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// True while elapsed-time playback is active.
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (_isPlaying == value) return;
                _isPlaying = value;
                OnPropertyChanged();
                RefreshCommandStates();
            }
        }

        /// <summary>
        /// Absolute path of the currently selected folder containing frames.
        /// </summary>
        public string? FolderPath
        {
            get => _folderPath;
            set
            {
                if (_folderPath == value) return;
                _folderPath = value;
                OnPropertyChanged();
                RefreshCommandStates();
            }
        }

        /// <summary>
        /// True while a preload operation is actively decoding frames.
        /// </summary>
        public bool IsPreloading
        {
            get => _isPreloading;
            private set
            {
                if (_isPreloading == value) return;
                _isPreloading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInteractionEnabled));
                RefreshCommandStates();
            }
        }

        /// <summary>
        /// Number of frames decoded so far in the current preload sequence.
        /// </summary>
        public int PreloadCount
        {
            get => _preloadCount;
            private set
            {
                if (_preloadCount == value) return;
                _preloadCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreloadPercent));
            }
        }

        /// <summary>
        /// Total number of frames expected to be decoded in the current preload sequence.
        /// </summary>
        public int PreloadTotal
        {
            get => _preloadTotal;
            private set
            {
                if (_preloadTotal == value) return;
                _preloadTotal = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreloadPercent));
            }
        }

        /// <summary>
        /// Percentage of preload completion (0 when total is 0).
        /// </summary>
        public double PreloadPercent => _preloadTotal == 0 ? 0 : (double)_preloadCount / _preloadTotal;

        /// <summary>
        /// Total frames currently available after scanning the folder (decoded or lazy).
        /// </summary>
        public int FrameCount
        {
            get => _frameCount;
            private set
            {
                if (_frameCount == value) return;
                _frameCount = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Maximum number of parallel frame decode operations. Proxies the underlying concurrency settings.
        /// Setting this invokes <see cref="IConcurrencySettings.SetMaxParallelDecodes(int)"/> which may validate range.
        /// </summary>
        public int MaxParallelDecodes
        {
            get => _concurrencySettings.MaxParallelDecodes;
            set
            {
                _concurrencySettings.SetMaxParallelDecodes(value);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Command to begin playback over loaded frames.
        /// </summary>
        public ICommand PlayCommand => _playCommand;

        /// <summary>
        /// Command to stop ongoing playback.
        /// </summary>
        public ICommand StopCommand => _stopCommand;

        /// <summary>
        /// Command to rewind to the first frame (index 0) without starting playback.
        /// </summary>
        public ICommand RewindCommand => _rewindCommand;

        /// <summary>
        /// Command that scrubs to a specified frame index provided as the command parameter (int).
        /// </summary>
        public ICommand ScrubCommand => _scrubCommand;

        /// <summary>
        /// Command to reload frames from the currently selected folder (if any).
        /// </summary>
        public ICommand ReloadCommand => _reloadCommand;

        /// <summary>
        /// Command to open a folder; set <see cref="FolderPath"/> externally and invoke this to trigger loading.
        /// For activating load directly with a path, use <see cref="OpenFolder(string)"/>.
        /// </summary>
        public ICommand OpenFolderCommand => _openFolderCommand;

        /// <summary>
        /// Performs folder validation and initiates asynchronous scan+preload sequence for the specified path.
        /// Cancels any in-progress load operation first.
        /// </summary>
        /// <param name="folderPath">Absolute folder path to load.</param>
        public void OpenFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            if (!_folderScanner.IsValidFolder(folderPath)) return;

            // Cancel previous load.
            _loadCts?.Cancel();

            FolderPath = folderPath;
            _ = LoadFramesAsync();
        }

        /// <summary>
        /// Reloads the current folder preserving the FPS and attempting to restore playback state.
        /// </summary>
        public Task ReloadAsync()
        {
            if (FolderPath == null) return Task.CompletedTask;
            return LoadFramesAsync(preservePlaybackState: true);
        }

        /// <summary>
        /// Internal playback start logic. Assigns FPS to controller, starts playback and updates state.
        /// </summary>
        private void Play()
        {
            if (_frames.Count == 0) return;
            _playbackController.FramesPerSecond = _selectedFps;

            int startIndex = Math.Clamp(_currentFrameIndex, 0, Math.Max(0, _frames.Count - 1));
            long absoluteStart = Math.Max(0, _lastAbsoluteFrameNumber);
            var startElapsed = _elapsed < TimeSpan.Zero ? TimeSpan.Zero : _elapsed;

            _ = _playbackController.StartAsync(_frames, startIndex, absoluteStart, startElapsed, CancellationToken.None);
            IsPlaying = true;
        }

        /// <summary>
        /// Internal stop logic; stops controller and updates state.
        /// </summary>
        private void Stop()
        {
            _playbackController.Stop();
            IsPlaying = false;
        }

        /// <summary>
        /// Internal rewind logic; rewinds controller and sets current frame index to 0.
        /// </summary>
        private void Rewind()
        {
            _playbackController.Rewind();
            ResetPlaybackMetrics();
            CurrentFrameIndex = 0;
        }

        /// <summary>
        /// Scrubs to a specific frame index, stopping playback first if active.
        /// </summary>
        /// <param name="index">Target frame index.</param>
        private void Scrub(int index)
        {
            if (index < 0 || index >= _frames.Count) return;
            if (IsPlaying)
            {
                Stop();
            }
            CurrentFrameIndex = index;
        }

        /// <summary>
        /// Sets the current frame index without modifying playback state (used during live scrubbing).
        /// </summary>
        /// <param name="index">Target frame index.</param>
        public void SetCurrentFrameIndex(int index)
        {
            if (index < 0 || index >= _frames.Count) return;
            CurrentFrameIndex = index;
        }

        /// <summary>
        /// Asynchronously scans the current <see cref="FolderPath"/> for frames and preloads decoded bitmaps.
        /// Progress updates <see cref="PreloadCount"/>. Cancels any previous load via internal CTS.
        /// Subscribes to playback frame change events after successful preload.
        /// </summary>
        private async Task LoadFramesAsync(bool preservePlaybackState = false)
        {
            if (FolderPath == null) return;

            // Cancel previous.
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;
            bool resumePlayback = preservePlaybackState && _isPlaying;
            int desiredIndex = preservePlaybackState ? _currentFrameIndex : 0;
            bool loadSucceeded = false;

            if (_isPlaying)
            {
                Stop();
            }

            try
            {
                IsPreloading = true;
                PreloadCount = 0;
                PreloadTotal = 0;
                FrameCount = 0;
                CurrentFrameIndex = 0;
                ResetPlaybackMetrics();

                _frameCache.Clear();
                _frames.Clear();

                // Resume on UI context (no ConfigureAwait(false)) so collection modifications are thread-safe.
                var scanned = await _folderScanner.ScanAsync(FolderPath, ct);
                FrameCount = scanned.Count;
                UpdateRecentFolderCount(FolderPath, FrameCount);
                PreloadTotal = Math.Min(scanned.Count, _frameCache.PreloadSoftCap);
                PreloadCount = 0;
                foreach (var fm in scanned)
                {
                    ct.ThrowIfCancellationRequested();
                    _frames.Add(fm);
                }
                OnPropertyChanged(nameof(Frames));

                var progress = new Progress<int>(count => PreloadCount = Math.Min(count, PreloadTotal));

                // Keep continuation on UI thread for property updates.
                await _frameCache.PreloadAsync(scanned, null, null, progress, ct);
                loadSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellation (expected).
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] LoadFramesAsync error: {ex.Message}");
            }
            finally
            {
                IsPreloading = false;
                PreloadCount = Math.Min(PreloadCount, PreloadTotal);
                RefreshCommandStates();
            }

            // Subscribe to playback frame changes (idempotent subscription).
            _playbackController.FrameChanged -= OnPlaybackFrameChanged;
            _playbackController.FrameChanged += OnPlaybackFrameChanged;

            if (loadSucceeded && FolderPath != null)
            {
                _settingsService.RecordFolder(FolderPath);
                SyncRecentFolders();
            }

            if (!ct.IsCancellationRequested && _frames.Count > 0)
            {
                int clampedIndex = Math.Clamp(desiredIndex, 0, _frames.Count - 1);
                CurrentFrameIndex = clampedIndex;
                if (resumePlayback || !preservePlaybackState)
                {
                    Play();
                }
            }

            RefreshCommandStates();
        }

        /// <summary>
        /// Updates <see cref="CurrentFrameIndex"/> when the playback controller signals a change.
        /// </summary>
        private void OnPlaybackFrameChanged(object? sender, FrameChangedEventArgs args)
        {
            DroppedFrames = args.DroppedTotal;
            UpdateElapsedDisplay(args.Elapsed, args.AbsoluteFrameNumber, args.FrameIndex);

            if (args.FrameIndex != _currentFrameIndex)
            {
                CurrentFrameIndex = args.FrameIndex;
            }

            if (!_playbackController.IsPlaying && IsPlaying)
            {
                IsPlaying = false;
            }
        }

        private void UpdateElapsedDisplay(TimeSpan elapsed, long? absoluteFrameNumber, int? frameIndexOverride)
        {
            _elapsed = elapsed;
            if (absoluteFrameNumber.HasValue)
            {
                _lastAbsoluteFrameNumber = absoluteFrameNumber.Value;
            }
            else if (frameIndexOverride.HasValue)
            {
                _lastAbsoluteFrameNumber = frameIndexOverride.Value;
            }

            int fps = Math.Max(1, _selectedFps);
            int framesComponent;
            if (absoluteFrameNumber.HasValue)
            {
                framesComponent = (int)(absoluteFrameNumber.Value % fps);
            }
            else if (frameIndexOverride.HasValue)
            {
                framesComponent = Math.Abs(frameIndexOverride.Value % fps);
            }
            else
            {
                framesComponent = (int)((elapsed.TotalSeconds * fps) % fps);
            }

            int minutes = (int)elapsed.TotalMinutes;
            int seconds = elapsed.Seconds;
            ElapsedTimeDisplay = $"{minutes:00}:{seconds:00}:{framesComponent:00}";
        }

        private void ResetPlaybackMetrics()
        {
            DroppedFrames = 0;
            _lastAbsoluteFrameNumber = 0;
            UpdateElapsedDisplay(TimeSpan.Zero, 0, _currentFrameIndex);
        }

        private void SyncRecentFolders()
        {
            var existing = _recentFolders.ToDictionary(r => r.FullPath, StringComparer.OrdinalIgnoreCase);
            _recentFolders.Clear();
            foreach (var path in _settingsService.RecentFolders.Take(6))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (!existing.TryGetValue(path, out var entry))
                {
                    entry = new RecentFolderEntry(path, CountImagesInFolder(path));
                }

                _recentFolders.Add(entry);
            }

            IsTrayOpen = _recentFolders.Count > 0;
            OnPropertyChanged(nameof(RecentFolders));
            OnPropertyChanged(nameof(LastOpenedFolder));
        }

        private void UpdateRecentFolderCount(string? folderPath, int frameCount)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || frameCount < 0) return;

            var entry = _recentFolders.FirstOrDefault(r => string.Equals(r.FullPath, folderPath, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.ImageCount = frameCount;
            }
            else
            {
                _recentFolders.Add(new RecentFolderEntry(folderPath, frameCount));
            }
        }

        private static int CountImagesInFolder(string folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return 0;
                int jpg = Directory.EnumerateFiles(folderPath, "*.jpg", SearchOption.TopDirectoryOnly).Count();
                int jpeg = Directory.EnumerateFiles(folderPath, "*.jpeg", SearchOption.TopDirectoryOnly).Count();
                return jpg + jpeg;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetFolderName(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? folderPath : name;
        }

        public sealed class RecentFolderEntry : INotifyPropertyChanged
        {
            private int _imageCount;

            public RecentFolderEntry(string fullPath, int imageCount)
            {
                FullPath = fullPath;
                _imageCount = imageCount;
            }

            public string FullPath { get; }

            public string FolderName => GetFolderName(FullPath);

            public int ImageCount
            {
                get => _imageCount;
                set
                {
                    if (_imageCount == value) return;
                    _imageCount = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Raises CanExecuteChanged for relevant commands to refresh UI state.
        /// </summary>
        private void RefreshCommandStates()
        {
            _playCommand.RaiseCanExecuteChanged();
            _stopCommand.RaiseCanExecuteChanged();
            _rewindCommand.RaiseCanExecuteChanged();
            _reloadCommand.RaiseCanExecuteChanged();
            _openFolderCommand.RaiseCanExecuteChanged();
            _scrubCommand.RaiseCanExecuteChanged();
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises <see cref="PropertyChanged"/> for the specified property.
        /// </summary>
        /// <param name="propertyName">Name of property (auto-supplied by compiler when omitted).</param>
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Internal ICommand implementation supporting an integer command parameter.
        /// </summary>
        private sealed class IntParameterCommand : ICommand
        {
            private readonly Action<int> _execute;
            private readonly Func<bool>? _canExecute;

            public IntParameterCommand(Action<int> execute, Func<bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

            public void Execute(object? parameter)
            {
                int value = 0;
                if (parameter is int i) value = i;
                else if (parameter is string s && int.TryParse(s, out var parsed)) value = parsed;
                _execute(value);
            }

            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
