using System.Windows;
using PhotoAnimator.App.Infrastructure;
using PhotoAnimator.App.Services;
using PhotoAnimator.App.ViewModels;

namespace PhotoAnimator.App;

/// <summary>
/// Application bootstrapper that wires up the minimal service locator and launches the main window.
/// </summary>
public partial class App : System.Windows.Application
{
    public static ServiceLocator Services { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterServices();

        var window = Services.GetRequired<MainWindow>();
        window.Show();
    }

    private static void RegisterServices()
    {
        Services.RegisterSingleton<IConcurrencySettings>(_ => new ConcurrencySettings());
        Services.RegisterSingleton<IDecodeScalingStrategy>(_ => new DecodeScalingStrategy());
        Services.RegisterSingleton<IImageDecodeService>(_ => new ImageDecodeService());
        Services.RegisterSingleton<IAppSettingsService>(_ => new AppSettingsService());
        Services.RegisterSingleton<IFrameCache>(sp => new FrameCache(
            sp.GetRequired<IConcurrencySettings>(),
            sp.GetRequired<IDecodeScalingStrategy>(),
            sp.GetRequired<IImageDecodeService>()));
        Services.RegisterSingleton<IFolderScanner>(_ => new FolderScanner());
        Services.RegisterSingleton<IFolderDialogService>(_ => new FolderDialogService());
        Services.RegisterSingleton<IPlaybackController>(_ => new PlaybackController());
        Services.RegisterSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequired<IFolderScanner>(),
            sp.GetRequired<IImageDecodeService>(),
            sp.GetRequired<IFrameCache>(),
            sp.GetRequired<IPlaybackController>(),
            sp.GetRequired<IConcurrencySettings>(),
            sp.GetRequired<IAppSettingsService>()));
        Services.RegisterSingleton<MainWindow>(sp => new MainWindow(
            sp.GetRequired<MainViewModel>(),
            sp.GetRequired<IFrameCache>(),
            sp.GetRequired<IPlaybackController>(),
            sp.GetRequired<IFolderDialogService>()));
    }
}
