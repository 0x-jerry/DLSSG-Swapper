using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DlssgSwapper.App.Configuration;

public static class ThemeService
{
    public static void Apply(AppTheme theme, Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        if (window.SystemBackdrop != null) return;

        if (MicaController.IsSupported())
            window.SystemBackdrop = new MicaBackdrop();
        else if (DesktopAcrylicController.IsSupported())
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
    }
}
