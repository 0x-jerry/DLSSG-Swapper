using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DlssgSwapper.App.Configuration;

public static class ThemeService
{
    public static void Apply(AppTheme theme, Window window, WindowBackdropType backdrop)
    {
        if (theme == AppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme(true);
            SystemThemeWatcher.Watch(window, backdrop);
            return;
        }

        SystemThemeWatcher.UnWatch(window);
        ApplicationThemeManager.Apply(
            theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            backdrop,
            true);
    }
}
