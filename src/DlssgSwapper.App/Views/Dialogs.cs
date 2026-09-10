using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;

namespace DlssgSwapper.App.Views;

internal static class Dialogs
{
    public static async Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText)
    {
        var dialog = Create(title, message);
        dialog.PrimaryButtonText = confirmText;
        dialog.PrimaryButtonAppearance = ControlAppearance.Danger;
        dialog.CloseButtonText = "Cancel";

        return await dialog.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    public static void Error(string title, string message) => _ = ShowErrorAsync(title, message);

    private static async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            await Create(title, message).ShowDialogAsync();
        }
        catch (Exception)
        {
            // Reporting a failure must never turn into a second failure.
        }
    }

    private static MessageBox Create(string title, string message)
    {
        var dialog = new MessageBox
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
            },
        };

        if (Application.Current?.MainWindow is { } owner)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return dialog;
    }
}
