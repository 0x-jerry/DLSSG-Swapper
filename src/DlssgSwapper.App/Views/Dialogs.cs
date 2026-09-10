using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;
using UiButton = Wpf.Ui.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfStackPanel = System.Windows.Controls.StackPanel;

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
        var body = new WpfStackPanel();
        body.Children.Add(Paragraph(message));

        WpfCheckBox? option = null;
        if (optionText != null)
        {
            option = new WpfCheckBox
            {
                Content = optionText,
                IsChecked = optionDefault,
                Margin = new Thickness(0, 14, 0, 0),
            };
            body.Children.Add(option);
        }

        var dialog = Create(title, body);
        dialog.PrimaryButtonText = confirmText;
        dialog.PrimaryButtonAppearance = ControlAppearance.Danger;
        dialog.CloseButtonText = "Cancel";
        UseLightTextForDangerButtons(dialog);

        bool confirmed = await dialog.ShowDialogAsync() == MessageBoxResult.Primary;
        return new Confirmation(confirmed, option?.IsChecked == true);
    }

    public static void Error(string title, string message) => _ = ShowErrorAsync(title, message);

    private static async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            await Create(title, Paragraph(message)).ShowDialogAsync();
        }
        catch (Exception)
        {
            // Reporting a failure must never turn into a second failure.
        }
    }

    private static MessageBox Create(string title, object content)
    {
        var dialog = new MessageBox { Title = title, Content = content };

        if (Application.Current?.MainWindow is { } owner)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return dialog;
    }

    // WPF-UI's Danger appearance only recolors the background, so the confirm button keeps the
    // dark default label on a red fill. Scope an implicit style to this dialog that lightens the
    // text of any danger button (the dialog's primary button) without touching Cancel.
    private static void UseLightTextForDangerButtons(MessageBox dialog)
    {
        if (Application.Current?.TryFindResource(typeof(UiButton)) is not Style baseStyle) return;

        var danger = new Trigger { Property = UiButton.AppearanceProperty, Value = ControlAppearance.Danger };
        danger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        danger.Setters.Add(new Setter(UiButton.PressedForegroundProperty, Brushes.White));

        var style = new Style(typeof(UiButton), baseStyle);
        style.Triggers.Add(danger);
        dialog.Resources.Add(typeof(UiButton), style);
    }

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Left,
    };
}
