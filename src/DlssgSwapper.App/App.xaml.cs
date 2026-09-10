using System.Windows;
using DlssgSwapper.App.Configuration;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DlssgSwapper.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        AppTheme theme = AppSettings.Load().Theme;
        if (theme == AppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme(true);
        }
        else
        {
            ApplicationThemeManager.Apply(
                theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                WindowBackdropType.Mica,
                true);
        }
    }
}
