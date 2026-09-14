using DlssgSwapper.App.Configuration;
using Microsoft.UI.Xaml;

namespace DlssgSwapper.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Activate();
        ThemeService.Apply(AppSettings.Load().Theme, window);
    }
}
