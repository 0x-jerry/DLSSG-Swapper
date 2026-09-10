using System.Windows;
using Wpf.Ui.Appearance;

namespace DlssgSwapper.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
    }
}