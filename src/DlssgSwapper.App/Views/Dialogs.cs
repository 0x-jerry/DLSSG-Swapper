using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DlssgSwapper.App.Views;

internal static class Dialogs
{
    public sealed record Confirmation(bool Confirmed, bool OptionChecked);

    public static async Task<Confirmation> ConfirmDestructiveAsync(
        string title,
        string message,
        string confirmText,
        string? optionText = null,
        bool optionDefault = false)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        CheckBox? option = null;
        if (optionText != null)
        {
            option = new CheckBox { Content = optionText, IsChecked = optionDefault };
            body.Children.Add(option);
        }

        var dialog = Create(title, body);
        dialog.PrimaryButtonText = confirmText;
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.PrimaryButtonStyle = Application.Current.Resources["DangerButtonStyle"] as Style;

        bool confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return new Confirmation(confirmed, option?.IsChecked == true);
    }

    public static void Error(string title, string message) => _ = ShowErrorAsync(title, message);

    private static async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            var dialog = Create(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            dialog.CloseButtonText = "Close";
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // Reporting a failure must never turn into a second failure.
        }
    }

    private static ContentDialog Create(string title, object content) => new()
    {
        Title = title,
        Content = content,
        XamlRoot = (App.MainWindow?.Content as FrameworkElement)?.XamlRoot,
    };
}
