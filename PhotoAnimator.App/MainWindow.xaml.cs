using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input;
using PhotoAnimator.App.Services;
using PhotoAnimator.App.ViewModels;
using PhotoAnimator.App.Controls;
using WinForms = System.Windows.Forms;

namespace PhotoAnimator.App;

public partial class MainWindow : Window
{
    private readonly FolderScanner _folderScanner;
    private readonly ImageDecodeService _imageDecodeService;
    private readonly ConcurrencySettings _concurrencySettings;
    private readonly FrameCache _frameCache;
    private readonly PlaybackController _playbackController;
    private readonly IFolderDialogService _folderDialogService;
    private readonly MainViewModel _viewModel;
    private readonly DoubleBufferedImageControl? _imageSurface;
    private Slider? _scrubSlider;
    private bool _wasPlayingBeforeScrub;
    private bool _isScrubbing;

    public int[] FpsOptions { get; } = new[] { 6, 8, 10, 12, 15, 18, 20, 24 };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
 
        _folderScanner = new FolderScanner();
        _imageDecodeService = new ImageDecodeService();
        _concurrencySettings = new ConcurrencySettings();
        var scaling = new DecodeScalingStrategy();
        _frameCache = new FrameCache(_concurrencySettings, scaling, _imageDecodeService);
        _playbackController = new PlaybackController();
        _folderDialogService = new FolderDialogService();
        _viewModel = new MainViewModel(_folderScanner, _imageDecodeService, _frameCache, _playbackController, _concurrencySettings);
 
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
    }

    private void OnPlaybackFrameChanged(object? sender, int index)
    {
        if (index < 0) return;
        var bmp = _frameCache.GetIfDecoded(index);
        if (bmp is BitmapSource bitmap && _imageSurface != null)
        {
            _imageSurface.UpdateFrame(bitmap);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _folderDialogService.SelectFolder();
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
            var bmp = _frameCache.GetIfDecoded(idx);
            if (bmp is BitmapSource bitmap && _imageSurface != null)
            {
                _imageSurface.UpdateFrame(bitmap);
            }
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreloadCount))
        {
            AdjustSliderMaximum();
        }
    }
 
    private void AdjustSliderMaximum()
    {
        if (_scrubSlider == null) return;
        int max = Math.Max(0, _viewModel.PreloadCount - 1);
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
        int newIndex = Math.Clamp(_viewModel.CurrentFrameIndex + delta, 0, Math.Max(0, _viewModel.PreloadCount - 1));
        if (newIndex != _viewModel.CurrentFrameIndex)
        {
            _viewModel.SetCurrentFrameIndex(newIndex);
            var bmp = _frameCache.GetIfDecoded(newIndex);
            if (bmp is BitmapSource bitmap && _imageSurface != null)
            {
                _imageSurface.UpdateFrame(bitmap);
            }
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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
        }
    }
}