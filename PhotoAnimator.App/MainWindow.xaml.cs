using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input;
using System.Threading;
using PhotoAnimator.App.Services;
using PhotoAnimator.App.ViewModels;
using PhotoAnimator.App.Controls;

namespace PhotoAnimator.App;

public partial class MainWindow : Window
{
    private readonly IFrameCache _frameCache;
    private readonly IPlaybackController _playbackController;
    private readonly IFolderDialogService _folderDialogService;
    private readonly MainViewModel _viewModel;
    private readonly DoubleBufferedImageControl? _imageSurface;
    private Slider? _scrubSlider;
    private bool _wasPlayingBeforeScrub;
    private bool _isScrubbing;
    private CancellationTokenSource? _onDemandDecodeCts;

    public int[] FpsOptions { get; } = new[] { 6, 8, 10, 12, 15, 16, 18, 20, 24, 25, 30, 60 };

    public MainWindow()
        : this(
            App.Services.GetRequired<MainViewModel>(),
            App.Services.GetRequired<IFrameCache>(),
            App.Services.GetRequired<IPlaybackController>(),
            App.Services.GetRequired<IFolderDialogService>())
    {
    }

    public MainWindow(
        MainViewModel viewModel,
        IFrameCache frameCache,
        IPlaybackController playbackController,
        IFolderDialogService folderDialogService)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _frameCache = frameCache ?? throw new ArgumentNullException(nameof(frameCache));
        _playbackController = playbackController ?? throw new ArgumentNullException(nameof(playbackController));
        _folderDialogService = folderDialogService ?? throw new ArgumentNullException(nameof(folderDialogService));

        InitializeComponent();
        Loaded += (_, __) => Focus();

        DataContext = _viewModel;

        _imageSurface = FindName("PART_ImageSurface") as DoubleBufferedImageControl;
        _scrubSlider = FindName("PART_ScrubSlider") as Slider;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AdjustSliderMaximum();

        _playbackController.FrameChanged += OnPlaybackFrameChanged;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeToContent = SizeToContent.Manual;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _playbackController.FrameChanged -= OnPlaybackFrameChanged;
        _onDemandDecodeCts?.Cancel();
        _onDemandDecodeCts?.Dispose();
    }

    private void OnPlaybackFrameChanged(object? sender, FrameChangedEventArgs args)
    {
        if (args.FrameIndex < 0) return;
        _ = ShowFrameAsync(args.FrameIndex, cancelPrevious: true);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _folderDialogService.SelectFolder(_viewModel.LastOpenedFolder);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _viewModel.OpenFolder(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenFolder error: {ex.Message}");
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        var cmd = _viewModel.PlayCommand;
        if (cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        var cmd = _viewModel.StopCommand;
        if (cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnRewindClick(object sender, RoutedEventArgs e)
    {
        var cmd = _viewModel.RewindCommand;
        if (cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        var cmd = _viewModel.ReloadCommand;
        if (cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnRecentFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            string? path = button.Tag switch
            {
                string s => s,
                MainViewModel.RecentFolderEntry entry => entry.FullPath,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(path))
            {
                _viewModel.OpenFolder(path);
            }
        }
    }

    private void ScrubSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isScrubbing) return;
        _isScrubbing = true;
        _wasPlayingBeforeScrub = _viewModel.IsPlaying;
        if (_wasPlayingBeforeScrub)
        {
            var stop = _viewModel.StopCommand;
            if (stop.CanExecute(null)) stop.Execute(null);
        }
    }

    private void ScrubSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isScrubbing || _scrubSlider == null) return;
        int idx = (int)_scrubSlider.Value;
        if (idx != _viewModel.CurrentFrameIndex)
        {
            _viewModel.SetCurrentFrameIndex(idx);
            _ = ShowFrameAsync(idx, cancelPrevious: true);
        }
    }

    private void ScrubSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        if (_wasPlayingBeforeScrub)
        {
            var play = _viewModel.PlayCommand;
            if (play.CanExecute(null)) play.Execute(null);
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        _viewModel.IsHelpVisible = true;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        _viewModel.IsHelpVisible = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FrameCount))
        {
            AdjustSliderMaximum();
        }
    }
 
    private void AdjustSliderMaximum()
    {
        if (_scrubSlider == null) return;
        int max = Math.Max(0, _viewModel.FrameCount - 1);
        _scrubSlider.Maximum = max;
        if (_scrubSlider.Value > max)
        {
            _scrubSlider.Value = max;
        }
    }

    private void TogglePlayPause()
    {
        if (_viewModel.IsPlaying)
        {
            var stop = _viewModel.StopCommand;
            if (stop.CanExecute(null)) stop.Execute(null);
        }
        else
        {
            var play = _viewModel.PlayCommand;
            if (play.CanExecute(null)) play.Execute(null);
        }
    }

    private void StepFrame(int delta)
    {
        int newIndex = Math.Clamp(_viewModel.CurrentFrameIndex + delta, 0, Math.Max(0, _viewModel.FrameCount - 1));
        if (newIndex != _viewModel.CurrentFrameIndex)
        {
            _viewModel.SetCurrentFrameIndex(newIndex);
            _ = ShowFrameAsync(newIndex, cancelPrevious: true);
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_viewModel.IsInteractionEnabled && e.Key is not Key.F1 && e.Key is not Key.Escape)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                TogglePlayPause();
                e.Handled = true;
                break;
            case Key.Left:
                StepFrame(-1);
                e.Handled = true;
                break;
            case Key.Right:
                StepFrame(1);
                e.Handled = true;
                break;
            case Key.Home:
                {
                    var rewind = _viewModel.RewindCommand;
                    if (rewind.CanExecute(null)) rewind.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.R:
                {
                    var reload = _viewModel.ReloadCommand;
                    if (reload.CanExecute(null)) reload.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.F1:
                _viewModel.IsHelpVisible = true;
                e.Handled = true;
                break;
            case Key.Escape:
                if (_viewModel.IsHelpVisible)
                {
                    _viewModel.IsHelpVisible = false;
                    e.Handled = true;
                }
                break;
        }
    }

    private Task ShowFrameAsync(int index, bool cancelPrevious)
    {
        if (_imageSurface == null) return Task.CompletedTask;
        if (index < 0 || index >= _viewModel.FrameCount) return Task.CompletedTask;

        var cached = _frameCache.GetIfDecoded(index);
        if (cached is BitmapSource bitmapCached)
        {
            _imageSurface.UpdateFrame(bitmapCached);
            return Task.CompletedTask;
        }

        if (cancelPrevious)
        {
            _onDemandDecodeCts?.Cancel();
            _onDemandDecodeCts?.Dispose();
        }

        var cts = new CancellationTokenSource();
        _onDemandDecodeCts = cts;

        return FetchAndRenderAsync(index, cts.Token);

        async Task FetchAndRenderAsync(int frameIndex, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested) return;
                var frame = _viewModel.Frames[frameIndex];
                var decoded = await _frameCache.GetOrDecodeAsync(frame, frameIndex, token).ConfigureAwait(true);
                if (decoded is BitmapSource bmp && !token.IsCancellationRequested)
                {
                    _imageSurface.UpdateFrame(bmp);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Frame render error at {frameIndex}: {ex.Message}");
            }
        }
    }
}
