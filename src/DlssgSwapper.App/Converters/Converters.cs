using DlssgSwapper.App.ViewModels;
using DlssgSwapper.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DlssgSwapper.App.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public abstract class ResourceStyleConverter : IValueConverter
{
    protected abstract string KeyFor(object value);

    public object? Convert(object value, Type targetType, object parameter, string language) =>
        Application.Current.Resources.TryGetValue(KeyFor(value), out object? style) ? style : null;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class InstallStatePillStyleConverter : ResourceStyleConverter
{
    protected override string KeyFor(object value) => value switch
    {
        InstallKind.Installed => "InstallStatePillInstalled",
        InstallKind.Partial => "InstallStatePillPartial",
        _ => "InstallStatePillNotInstalled",
    };
}

public sealed class SeverityIconStyleConverter : ResourceStyleConverter
{
    protected override string KeyFor(object value) => value switch
    {
        VerificationSeverity.Success => "VerifyIconSuccess",
        VerificationSeverity.Caution => "VerifyIconCaution",
        _ => "VerifyIconNeutral",
    };
}
